using ProspectRenamer.Core.Models;

namespace ProspectRenamer.Core.Services;

public sealed class ProspectService
{
    private readonly IProspectCodec _codec;
    private readonly IProspectMetadataReader _metadataReader;
    private readonly BackupService _backupService;
    private readonly NameValidationService _nameValidation;
    private readonly LoadoutMigrationService _loadoutMigrationService;
    private readonly GameBackupArchiveService _gameBackupArchiveService;

    public ProspectService(
        IProspectCodec codec,
        BackupService backupService,
        NameValidationService nameValidation,
        IProspectMetadataReader? metadataReader = null,
        LoadoutMigrationService? loadoutMigrationService = null,
        GameBackupArchiveService? gameBackupArchiveService = null)
    {
        _codec = codec;
        _metadataReader = metadataReader ?? new IcarusProspectMetadataReader();
        _backupService = backupService;
        _nameValidation = nameValidation;
        _loadoutMigrationService = loadoutMigrationService ?? new LoadoutMigrationService(backupService);
        _gameBackupArchiveService = gameBackupArchiveService ?? new GameBackupArchiveService(backupService);
    }

    public async Task<IReadOnlyList<ProspectEntry>> LoadProspectsAsync(
        IEnumerable<ProspectLocation> locations,
        CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            var entries = new List<ProspectEntry>();
            foreach (var location in locations)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!Directory.Exists(location.DirectoryPath))
                {
                    continue;
                }

                string[] files;
                try
                {
                    files = Directory.GetFiles(location.DirectoryPath, "*.json", SearchOption.TopDirectoryOnly);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    entries.Add(new ProspectEntry(
                        Path.Combine(location.DirectoryPath, "<inaccessible>"),
                        "Folder inaccessible",
                        "—",
                        location.AccountId,
                        DateTime.MinValue,
                        false,
                        exception.Message));
                    continue;
                }

                foreach (var file in files)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var entry = LoadEntry(file, location.AccountId);
                    if (!entry.IsMission)
                    {
                        entries.Add(entry);
                    }
                }
            }

            return (IReadOnlyList<ProspectEntry>)entries
                .OrderByDescending(entry => entry.IsValid)
                .ThenBy(entry => entry.ProspectName, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();
        }, cancellationToken);
    }

    public ProspectEntry LoadEntry(string filePath, string accountId)
    {
        try
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var prospect = _metadataReader.Read(stream);
            return new ProspectEntry(
                Path.GetFullPath(filePath),
                prospect.ProspectId,
                prospect.ProspectType,
                accountId,
                File.GetLastWriteTime(filePath),
                true,
                null,
                !string.IsNullOrWhiteSpace(prospect.FactionMissionType));
        }
        catch (Exception exception)
        {
            return new ProspectEntry(
                Path.GetFullPath(filePath),
                Path.GetFileNameWithoutExtension(filePath),
                "Invalid save",
                accountId,
                File.Exists(filePath) ? File.GetLastWriteTime(filePath) : DateTime.MinValue,
                false,
                GetFriendlyError(exception));
        }
    }

    public ValidationResult ValidateNewName(ProspectEntry prospect, string? requestedName) =>
        _nameValidation.Validate(requestedName, prospect.DirectoryPath, prospect.FilePath);

    public async Task<RenameResult> RenameAsync(
        ProspectEntry entry,
        string requestedName,
        CancellationToken cancellationToken = default) =>
        await SaveAsAsync(entry, requestedName, ProspectOperationMode.Rename, cancellationToken);

    public async Task<RenameResult> DuplicateAsync(
        ProspectEntry entry,
        string requestedName,
        CancellationToken cancellationToken = default) =>
        await SaveAsAsync(entry, requestedName, ProspectOperationMode.Duplicate, cancellationToken);

    public async Task<RenameResult> SaveAsAsync(
        ProspectEntry entry,
        string requestedName,
        ProspectOperationMode mode,
        CancellationToken cancellationToken = default)
    {
        var validation = ValidateNewName(entry, requestedName);
        if (!validation.IsValid)
        {
            return RenameResult.Failure(entry.ProspectName, validation.NormalizedName, validation.Error!);
        }

        if (!entry.IsValid)
        {
            return RenameResult.Failure(
                entry.ProspectName,
                validation.NormalizedName,
                $"Invalid prospect saves cannot be {(mode == ProspectOperationMode.Rename ? "renamed" : "duplicated")}.");
        }

        var newName = validation.NormalizedName;
        var sourcePath = Path.GetFullPath(entry.FilePath);
        var destinationPath = Path.Combine(entry.DirectoryPath, $"{newName}.json");
        var temporaryPath = Path.Combine(entry.DirectoryPath, $".prospectrenamer-{Guid.NewGuid():N}.tmp");
        string? backupPath = null;
        PreparedLoadoutMigration? loadoutMigration = null;
        PreparedGameBackupArchive? gameBackupArchive = null;

        try
        {
            if (!File.Exists(sourcePath))
            {
                throw new FileNotFoundException("The original prospect file no longer exists.", sourcePath);
            }

            if ((File.GetAttributes(sourcePath) & FileAttributes.ReadOnly) != 0)
            {
                throw new UnauthorizedAccessException("The prospect file is read-only.");
            }

            var originalInfo = new FileInfo(sourcePath);
            var originalLength = originalInfo.Length;
            var originalWriteTime = originalInfo.LastWriteTimeUtc;

            DecodedProspect document;
            await using (var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true))
            {
                document = _codec.Load(source);
            }

            backupPath = await _backupService.CreateBackupAsync(
                sourcePath,
                entry.AccountId,
                document.ProspectId,
                cancellationToken);

            _codec.SetProspectId(document, newName);

            await using (var temporary = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                81920,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                _codec.Save(document, temporary);
                await temporary.FlushAsync(cancellationToken);
                temporary.Flush(true);
            }

            await using (var verification = new FileStream(temporaryPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true))
            {
                var verified = _codec.Load(verification);
                if (!string.Equals(verified.ProspectId, newName, StringComparison.Ordinal))
                {
                    throw new ProspectFormatException("The verification pass did not find the requested ProspectID.");
                }
            }

            if (mode == ProspectOperationMode.Rename)
            {
                loadoutMigration = await _loadoutMigrationService.PrepareAsync(
                    entry.DirectoryPath,
                    entry.AccountId,
                    entry.ProspectName,
                    newName,
                    cancellationToken);
                gameBackupArchive = await _gameBackupArchiveService.PrepareAsync(
                    sourcePath,
                    entry.AccountId,
                    entry.ProspectName,
                    cancellationToken);
            }

            var currentInfo = new FileInfo(sourcePath);
            if (!currentInfo.Exists || currentInfo.Length != originalLength || currentInfo.LastWriteTimeUtc != originalWriteTime)
            {
                throw new IOException("The original prospect changed while the rename was being prepared. Close or unload the prospect and try again.");
            }

            if (File.Exists(destinationPath))
            {
                throw new IOException("Another prospect created the destination file before the rename could finish.");
            }

            File.Move(temporaryPath, destinationPath);

            if (mode == ProspectOperationMode.Rename)
            {
                try
                {
                    if (loadoutMigration is not null)
                    {
                        await _loadoutMigrationService.ApplyAsync(loadoutMigration, cancellationToken);
                    }

                    if (gameBackupArchive is not null)
                    {
                        await _gameBackupArchiveService.RemoveLiveFilesAsync(gameBackupArchive, cancellationToken);
                    }

                    File.Delete(sourcePath);

                    // Never report Rename as successful while both JSON files are live.
                    if (File.Exists(sourcePath))
                    {
                        throw new IOException("Windows reported that the original was deleted, but it is still present.");
                    }
                }
                catch (Exception deletionException)
                {
                    var rollbackExceptions = new List<Exception>();
                    if (gameBackupArchive is not null)
                    {
                        try
                        {
                            await _gameBackupArchiveService.RestoreLiveFilesAsync(gameBackupArchive, CancellationToken.None);
                        }
                        catch (Exception gameBackupRollbackException)
                        {
                            rollbackExceptions.Add(gameBackupRollbackException);
                        }
                    }

                    if (loadoutMigration?.IsApplied == true)
                    {
                        try
                        {
                            await _loadoutMigrationService.RestoreAsync(loadoutMigration, CancellationToken.None);
                        }
                        catch (Exception loadoutRollbackException)
                        {
                            rollbackExceptions.Add(loadoutRollbackException);
                        }
                    }

                    try
                    {
                        File.Delete(destinationPath);
                    }
                    catch (Exception prospectRollbackException)
                    {
                        rollbackExceptions.Add(prospectRollbackException);
                    }

                    if (rollbackExceptions.Count > 0)
                    {
                        rollbackExceptions.Insert(0, deletionException);
                        throw new IOException(
                            "The rename failed and automatic rollback was incomplete. Use the reported backups before loading the prospect.",
                            new AggregateException(rollbackExceptions));
                    }

                    throw new IOException(
                        "The rename could not be completed, so the new prospect, local loadout migration, and game backups were rolled back. Your original is unchanged.",
                        deletionException);
                }
            }

            return RenameResult.Success(
                entry.ProspectName,
                newName,
                destinationPath,
                backupPath,
                loadoutMigration?.BackupPath,
                loadoutMigration?.ReferenceCount ?? 0,
                gameBackupArchive?.ArchiveDirectory,
                gameBackupArchive?.FileCount ?? 0);
        }
        catch (Exception exception)
        {
            return RenameResult.Failure(
                entry.ProspectName,
                newName,
                GetFriendlyError(exception),
                backupPath,
                loadoutMigration?.BackupPath,
                gameBackupArchive?.ArchiveDirectory);
        }
        finally
        {
            try
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
            catch
            {
                // A harmless non-json temporary file is preferable to risking the live save.
            }

            _loadoutMigrationService.Cleanup(loadoutMigration);
        }
    }

    private static string GetFriendlyError(Exception exception) => exception switch
    {
        ProspectFormatException => exception.Message,
        UnauthorizedAccessException => $"Windows denied access: {exception.Message}",
        FileNotFoundException => exception.Message,
        InvalidDataException => $"Loadout migration failed: {exception.Message}",
        IOException => $"File operation failed: {exception.Message}",
        _ => $"The prospect could not be processed: {exception.Message}"
    };
}
