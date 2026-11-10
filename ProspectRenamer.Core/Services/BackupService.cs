namespace ProspectRenamer.Core.Services;

public sealed class BackupService
{
    private readonly TimeProvider _timeProvider;

    public BackupService(string backupRoot, TimeProvider? timeProvider = null)
    {
        BackupRoot = Path.GetFullPath(backupRoot);
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public string BackupRoot { get; }

    public async Task<string> CreateBackupAsync(
        string sourceFile,
        string accountId,
        string prospectName,
        CancellationToken cancellationToken = default)
    {
        var safeAccount = SanitizePathSegment(accountId, "UnknownAccount");
        var safeProspect = SanitizePathSegment(prospectName, "UnknownProspect");
        var destinationDirectory = Path.Combine(BackupRoot, safeAccount, safeProspect);
        return await CreateBackupInDirectoryAsync(
            sourceFile,
            destinationDirectory,
            safeProspect,
            cancellationToken);
    }

    public async Task<string> CreateAccountFileBackupAsync(
        string sourceFile,
        string accountId,
        string fileLabel,
        CancellationToken cancellationToken = default)
    {
        var safeAccount = SanitizePathSegment(accountId, "UnknownAccount");
        var safeLabel = SanitizePathSegment(fileLabel, "AccountFile");
        var destinationDirectory = Path.Combine(BackupRoot, safeAccount, "AccountFiles", safeLabel);
        return await CreateBackupInDirectoryAsync(
            sourceFile,
            destinationDirectory,
            safeLabel,
            cancellationToken);
    }

    private async Task<string> CreateBackupInDirectoryAsync(
        string sourceFile,
        string destinationDirectory,
        string baseLabel,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(destinationDirectory);

        var timestamp = _timeProvider.GetLocalNow().ToString("yyyy-MM-dd_HHmmss");
        var baseName = $"{baseLabel}_{timestamp}";
        var destination = Path.Combine(destinationDirectory, $"{baseName}.json");
        var suffix = 1;
        while (File.Exists(destination))
        {
            destination = Path.Combine(destinationDirectory, $"{baseName}_{suffix++}.json");
        }

        await using var source = new FileStream(sourceFile, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
        await using var target = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
        await source.CopyToAsync(target, cancellationToken);
        await target.FlushAsync(cancellationToken);
        target.Flush(true);
        return destination;
    }

    public static string SanitizePathSegment(string? value, string fallback)
    {
        var candidate = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        foreach (var invalidCharacter in Path.GetInvalidFileNameChars())
        {
            candidate = candidate.Replace(invalidCharacter, '_');
        }

        candidate = candidate.TrimEnd(' ', '.');
        return candidate.Length == 0 ? fallback : candidate;
    }
}
