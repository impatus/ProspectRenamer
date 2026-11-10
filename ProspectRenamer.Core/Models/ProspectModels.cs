namespace ProspectRenamer.Core.Models;

public enum ProspectLocationKind
{
    Automatic,
    Custom
}

public sealed record ProspectLocation(
    string DirectoryPath,
    string AccountId,
    ProspectLocationKind Kind)
{
    public string DisplayName => string.IsNullOrWhiteSpace(AccountId) ? "Custom" : AccountId;
}

public sealed record ProspectEntry(
    string FilePath,
    string ProspectName,
    string ProspectType,
    string AccountId,
    DateTime LastModified,
    bool IsValid,
    string? ErrorMessage,
    bool IsMission = false)
{
    public string FileName => Path.GetFileName(FilePath);
    public string DirectoryPath => Path.GetDirectoryName(FilePath) ?? string.Empty;
}

public sealed record ProspectMetadata(
    string ProspectId,
    string ProspectType,
    string? FactionMissionType = null);

public sealed record ValidationResult(bool IsValid, string NormalizedName, string? Error)
{
    public static ValidationResult Valid(string normalizedName) => new(true, normalizedName, null);
    public static ValidationResult Invalid(string normalizedName, string error) => new(false, normalizedName, error);
}

public enum ProspectOperationMode
{
    Rename,
    Duplicate
}

public sealed record RenameResult(
    bool IsSuccess,
    string OldName,
    string NewName,
    string? NewFilePath,
    string? BackupPath,
    string? LoadoutBackupPath,
    int MigratedLoadoutReferences,
    string? GameBackupArchivePath,
    int ArchivedGameBackups,
    string? Error)
{
    public static RenameResult Success(
        string oldName,
        string newName,
        string newFilePath,
        string backupPath,
        string? loadoutBackupPath = null,
        int migratedLoadoutReferences = 0,
        string? gameBackupArchivePath = null,
        int archivedGameBackups = 0) =>
        new(
            true,
            oldName,
            newName,
            newFilePath,
            backupPath,
            loadoutBackupPath,
            migratedLoadoutReferences,
            gameBackupArchivePath,
            archivedGameBackups,
            null);

    public static RenameResult Failure(
        string oldName,
        string newName,
        string error,
        string? backupPath = null,
        string? loadoutBackupPath = null,
        string? gameBackupArchivePath = null) =>
        new(false, oldName, newName, null, backupPath, loadoutBackupPath, 0, gameBackupArchivePath, 0, error);
}
