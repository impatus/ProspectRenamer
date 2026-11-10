using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace ProspectRenamer.Core.Services;

public sealed class PreparedGameBackupArchive
{
    internal PreparedGameBackupArchive(string archiveDirectory, IReadOnlyList<ArchivedGameBackup> files)
    {
        ArchiveDirectory = archiveDirectory;
        Files = files;
    }

    public string ArchiveDirectory { get; }
    public int FileCount => Files.Count;
    internal IReadOnlyList<ArchivedGameBackup> Files { get; }
}

internal sealed class ArchivedGameBackup
{
    public required string LivePath { get; init; }
    public required string ArchivePath { get; init; }
    public required byte[] Hash { get; init; }
    public required DateTime CreationTimeUtc { get; init; }
    public required DateTime LastWriteTimeUtc { get; init; }
    public bool RemovedFromLiveFolder { get; set; }
}

public sealed class GameBackupArchiveService
{
    private readonly BackupService _backupService;
    private readonly TimeProvider _timeProvider;

    public GameBackupArchiveService(BackupService backupService, TimeProvider? timeProvider = null)
    {
        _backupService = backupService;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<PreparedGameBackupArchive?> PrepareAsync(
        string sourceProspectPath,
        string accountId,
        string prospectName,
        CancellationToken cancellationToken = default)
    {
        var sourcePath = Path.GetFullPath(sourceProspectPath);
        var directory = Path.GetDirectoryName(sourcePath)
            ?? throw new DirectoryNotFoundException("The prospect directory could not be resolved.");
        var sourceFileName = Path.GetFileName(sourcePath);
        var pattern = new Regex(
            $"^{Regex.Escape(sourceFileName)}\\.backup(?:_\\d+)?$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var liveBackups = Directory.EnumerateFiles(directory, $"{sourceFileName}.backup*", SearchOption.TopDirectoryOnly)
            .Where(path => pattern.IsMatch(Path.GetFileName(path)))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (liveBackups.Length == 0)
        {
            return null;
        }

        var safeAccount = BackupService.SanitizePathSegment(accountId, "UnknownAccount");
        var safeProspect = BackupService.SanitizePathSegment(prospectName, "UnknownProspect");
        var timestamp = _timeProvider.GetLocalNow().ToString("yyyy-MM-dd_HHmmss");
        var archiveDirectory = Path.Combine(
            _backupService.BackupRoot,
            safeAccount,
            safeProspect,
            "GameBackups",
            $"{timestamp}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(archiveDirectory);

        var archived = new List<ArchivedGameBackup>(liveBackups.Length);
        foreach (var livePath in liveBackups)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fileInfo = new FileInfo(livePath);
            if ((fileInfo.Attributes & FileAttributes.ReadOnly) != 0)
            {
                throw new UnauthorizedAccessException($"The game backup '{fileInfo.Name}' is read-only.");
            }

            var archivePath = Path.Combine(archiveDirectory, fileInfo.Name);
            var sourceHash = await ComputeHashAsync(livePath, cancellationToken);
            await CopyDurablyAsync(livePath, archivePath, cancellationToken);
            var archiveHash = await ComputeHashAsync(archivePath, cancellationToken);
            if (!CryptographicOperations.FixedTimeEquals(sourceHash, archiveHash))
            {
                throw new IOException($"The archived copy of '{fileInfo.Name}' failed its integrity check.");
            }

            archived.Add(new ArchivedGameBackup
            {
                LivePath = livePath,
                ArchivePath = archivePath,
                Hash = sourceHash,
                CreationTimeUtc = fileInfo.CreationTimeUtc,
                LastWriteTimeUtc = fileInfo.LastWriteTimeUtc
            });
        }

        return new PreparedGameBackupArchive(archiveDirectory, archived);
    }

    public async Task RemoveLiveFilesAsync(
        PreparedGameBackupArchive archive,
        CancellationToken cancellationToken = default)
    {
        foreach (var file in archive.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!File.Exists(file.LivePath))
            {
                throw new IOException($"The game backup '{Path.GetFileName(file.LivePath)}' disappeared before it could be archived.");
            }

            var currentHash = await ComputeHashAsync(file.LivePath, cancellationToken);
            if (!CryptographicOperations.FixedTimeEquals(currentHash, file.Hash))
            {
                throw new IOException($"The game backup '{Path.GetFileName(file.LivePath)}' changed while Rename was being prepared.");
            }

            File.Delete(file.LivePath);
            if (File.Exists(file.LivePath))
            {
                throw new IOException($"The game backup '{Path.GetFileName(file.LivePath)}' could not be removed from the live folder.");
            }

            file.RemovedFromLiveFolder = true;
        }
    }

    public async Task RestoreLiveFilesAsync(
        PreparedGameBackupArchive archive,
        CancellationToken cancellationToken = default)
    {
        foreach (var file in archive.Files.Where(file => file.RemovedFromLiveFolder))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(file.LivePath))
            {
                var existingHash = await ComputeHashAsync(file.LivePath, cancellationToken);
                if (!CryptographicOperations.FixedTimeEquals(existingHash, file.Hash))
                {
                    throw new IOException($"Rollback stopped because '{Path.GetFileName(file.LivePath)}' was recreated with different content.");
                }

                file.RemovedFromLiveFolder = false;
                continue;
            }

            await CopyDurablyAsync(file.ArchivePath, file.LivePath, cancellationToken);
            File.SetCreationTimeUtc(file.LivePath, file.CreationTimeUtc);
            File.SetLastWriteTimeUtc(file.LivePath, file.LastWriteTimeUtc);
            var restoredHash = await ComputeHashAsync(file.LivePath, cancellationToken);
            if (!CryptographicOperations.FixedTimeEquals(restoredHash, file.Hash))
            {
                throw new IOException($"The restored game backup '{Path.GetFileName(file.LivePath)}' failed its integrity check.");
            }

            file.RemovedFromLiveFolder = false;
        }
    }

    private static async Task CopyDurablyAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        await using var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
        await using var destination = new FileStream(
            destinationPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            81920,
            FileOptions.Asynchronous | FileOptions.WriteThrough);
        await source.CopyToAsync(destination, cancellationToken);
        await destination.FlushAsync(cancellationToken);
        destination.Flush(true);
    }

    private static async Task<byte[]> ComputeHashAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
        return await SHA256.HashDataAsync(stream, cancellationToken);
    }
}
