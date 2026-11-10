using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ProspectRenamer.Core.Services;

public sealed class PreparedLoadoutMigration
{
    internal PreparedLoadoutMigration(
        string loadoutsPath,
        string temporaryPath,
        string backupPath,
        string oldProspectId,
        string newProspectId,
        byte[] originalHash,
        int referenceCount)
    {
        LoadoutsPath = loadoutsPath;
        TemporaryPath = temporaryPath;
        BackupPath = backupPath;
        OldProspectId = oldProspectId;
        NewProspectId = newProspectId;
        OriginalHash = originalHash;
        ReferenceCount = referenceCount;
    }

    public string LoadoutsPath { get; }
    public string TemporaryPath { get; }
    public string BackupPath { get; }
    public string OldProspectId { get; }
    public string NewProspectId { get; }
    public int ReferenceCount { get; }
    internal byte[] OriginalHash { get; }
    internal bool IsApplied { get; set; }
}

public sealed class LoadoutMigrationService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false
    };

    private readonly BackupService _backupService;

    public LoadoutMigrationService(BackupService backupService)
    {
        _backupService = backupService;
    }

    public async Task<PreparedLoadoutMigration?> PrepareAsync(
        string prospectsDirectory,
        string accountId,
        string oldProspectId,
        string newProspectId,
        CancellationToken cancellationToken = default)
    {
        var loadoutsPath = ResolveLoadoutsPath(prospectsDirectory);
        if (loadoutsPath is null)
        {
            return null;
        }

        if ((File.GetAttributes(loadoutsPath) & FileAttributes.ReadOnly) != 0)
        {
            throw new UnauthorizedAccessException("Loadouts.json is read-only.");
        }

        var originalBytes = await File.ReadAllBytesAsync(loadoutsPath, cancellationToken);
        var originalHash = SHA256.HashData(originalBytes);
        var encoding = DetectEncoding(originalBytes);
        var originalRoot = ParseRoot(originalBytes, loadoutsPath);
        if (CountProspectReferences(originalRoot, newProspectId) > 0)
        {
            throw new InvalidDataException(
                "Loadouts.json already contains a reference to the requested new ProspectID. Rename was stopped to avoid merging unrelated loadouts.");
        }

        var migratedRoot = originalRoot.DeepClone().AsObject();
        var referenceCount = ReplaceProspectReferences(migratedRoot, oldProspectId, newProspectId);
        if (referenceCount == 0)
        {
            return null;
        }

        var backupPath = await _backupService.CreateAccountFileBackupAsync(
            loadoutsPath,
            accountId,
            "Loadouts",
            cancellationToken);

        var temporaryPath = Path.Combine(
            Path.GetDirectoryName(loadoutsPath)!,
            $".prospectrenamer-loadouts-{Guid.NewGuid():N}.tmp");

        try
        {
            var migratedBytes = EncodeText(migratedRoot.ToJsonString(SerializerOptions), encoding);
            await using (var temporary = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                81920,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await temporary.WriteAsync(migratedBytes, cancellationToken);
                await temporary.FlushAsync(cancellationToken);
                temporary.Flush(true);
            }

            var verifiedBytes = await File.ReadAllBytesAsync(temporaryPath, cancellationToken);
            var verifiedRoot = ParseRoot(verifiedBytes, temporaryPath);
            VerifyOnlyProspectReferencesChanged(
                originalRoot,
                verifiedRoot,
                oldProspectId,
                newProspectId,
                referenceCount);

            return new PreparedLoadoutMigration(
                loadoutsPath,
                temporaryPath,
                backupPath,
                oldProspectId,
                newProspectId,
                originalHash,
                referenceCount);
        }
        catch
        {
            TryDelete(temporaryPath);
            throw;
        }
    }

    public async Task ApplyAsync(
        PreparedLoadoutMigration migration,
        CancellationToken cancellationToken = default)
    {
        var liveBytes = await File.ReadAllBytesAsync(migration.LoadoutsPath, cancellationToken);
        if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(liveBytes), migration.OriginalHash))
        {
            throw new IOException("Loadouts.json changed while the rename was being prepared. Nothing was changed.");
        }

        File.Replace(migration.TemporaryPath, migration.LoadoutsPath, null, true);
        migration.IsApplied = true;

        var verifiedBytes = await File.ReadAllBytesAsync(migration.LoadoutsPath, cancellationToken);
        var verifiedRoot = ParseRoot(verifiedBytes, migration.LoadoutsPath);
        var oldCount = CountProspectReferences(verifiedRoot, migration.OldProspectId);
        var newCount = CountProspectReferences(verifiedRoot, migration.NewProspectId);
        if (oldCount != 0 || newCount < migration.ReferenceCount)
        {
            throw new IOException("The live Loadouts.json did not pass verification after replacement.");
        }
    }

    public async Task RestoreAsync(
        PreparedLoadoutMigration migration,
        CancellationToken cancellationToken = default)
    {
        if (!migration.IsApplied)
        {
            return;
        }

        var rollbackPath = Path.Combine(
            Path.GetDirectoryName(migration.LoadoutsPath)!,
            $".prospectrenamer-loadouts-rollback-{Guid.NewGuid():N}.tmp");

        try
        {
            File.Copy(migration.BackupPath, rollbackPath, false);
            var rollbackBytes = await File.ReadAllBytesAsync(rollbackPath, cancellationToken);
            if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(rollbackBytes), migration.OriginalHash))
            {
                throw new IOException("The Loadouts.json backup failed its integrity check during rollback.");
            }

            File.Replace(rollbackPath, migration.LoadoutsPath, null, true);
            var restoredBytes = await File.ReadAllBytesAsync(migration.LoadoutsPath, cancellationToken);
            if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(restoredBytes), migration.OriginalHash))
            {
                throw new IOException("Loadouts.json rollback could not be verified.");
            }

            migration.IsApplied = false;
        }
        finally
        {
            TryDelete(rollbackPath);
        }
    }

    public void Cleanup(PreparedLoadoutMigration? migration)
    {
        if (migration is not null)
        {
            TryDelete(migration.TemporaryPath);
        }
    }

    private static string? ResolveLoadoutsPath(string prospectsDirectory)
    {
        var normalized = Path.GetFullPath(prospectsDirectory);
        if (!string.Equals(Path.GetFileName(normalized), "Prospects", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var accountDirectory = Directory.GetParent(normalized)?.FullName;
        if (accountDirectory is null)
        {
            return null;
        }

        var candidate = Path.Combine(accountDirectory, "Loadout", "Loadouts.json");
        return File.Exists(candidate) ? candidate : null;
    }

    private static JsonObject ParseRoot(ReadOnlySpan<byte> bytes, string path)
    {
        try
        {
            var encoding = DetectEncoding(bytes);
            var json = encoding.Encoding.GetString(bytes[encoding.PreambleLength..]);
            return JsonNode.Parse(json) as JsonObject
                ?? throw new InvalidDataException($"{Path.GetFileName(path)} does not contain a JSON object root.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"{Path.GetFileName(path)} is not valid JSON: {exception.Message}", exception);
        }
    }

    private static TextEncodingInfo DetectEncoding(ReadOnlySpan<byte> bytes)
    {
        if (bytes.StartsWith(new byte[] { 0x00, 0x00, 0xFE, 0xFF }))
        {
            return new TextEncodingInfo(new UTF32Encoding(true, true, true), 4);
        }

        if (bytes.StartsWith(new byte[] { 0xFF, 0xFE, 0x00, 0x00 }))
        {
            return new TextEncodingInfo(new UTF32Encoding(false, true, true), 4);
        }

        if (bytes.StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }))
        {
            return new TextEncodingInfo(new UTF8Encoding(true, true), 3);
        }

        if (bytes.StartsWith(new byte[] { 0xFF, 0xFE }))
        {
            return new TextEncodingInfo(new UnicodeEncoding(false, true, true), 2);
        }

        if (bytes.StartsWith(new byte[] { 0xFE, 0xFF }))
        {
            return new TextEncodingInfo(new UnicodeEncoding(true, true, true), 2);
        }

        return new TextEncodingInfo(new UTF8Encoding(false, true), 0);
    }

    private static byte[] EncodeText(string text, TextEncodingInfo encoding)
    {
        var content = encoding.Encoding.GetBytes(text);
        if (encoding.PreambleLength == 0)
        {
            return content;
        }

        var preamble = encoding.Encoding.GetPreamble();
        var result = new byte[preamble.Length + content.Length];
        preamble.CopyTo(result, 0);
        content.CopyTo(result, preamble.Length);
        return result;
    }

    private sealed record TextEncodingInfo(Encoding Encoding, int PreambleLength);

    private static int ReplaceProspectReferences(JsonObject root, string oldProspectId, string newProspectId)
    {
        if (root["Loadouts"] is not JsonArray loadouts)
        {
            throw new InvalidDataException("Loadouts.json does not contain the expected Loadouts array.");
        }

        var count = 0;
        foreach (var loadout in loadouts.OfType<JsonObject>())
        {
            if (loadout["AssociatedProspect"] is not JsonObject associatedProspect ||
                associatedProspect["ProspectID"] is not JsonValue idValue ||
                !idValue.TryGetValue<string>(out var prospectId) ||
                !string.Equals(prospectId, oldProspectId, StringComparison.Ordinal))
            {
                continue;
            }

            associatedProspect["ProspectID"] = newProspectId;
            count++;
        }

        return count;
    }

    private static int CountProspectReferences(JsonObject root, string prospectId)
    {
        if (root["Loadouts"] is not JsonArray loadouts)
        {
            throw new InvalidDataException("Loadouts.json does not contain the expected Loadouts array.");
        }

        return loadouts
            .OfType<JsonObject>()
            .Count(loadout =>
                loadout["AssociatedProspect"] is JsonObject associatedProspect &&
                associatedProspect["ProspectID"] is JsonValue idValue &&
                idValue.TryGetValue<string>(out var currentId) &&
                string.Equals(currentId, prospectId, StringComparison.Ordinal));
    }

    private static void VerifyOnlyProspectReferencesChanged(
        JsonObject originalRoot,
        JsonObject migratedRoot,
        string oldProspectId,
        string newProspectId,
        int expectedReferenceCount)
    {
        var oldCount = CountProspectReferences(migratedRoot, oldProspectId);
        var newCount = CountProspectReferences(migratedRoot, newProspectId);
        if (oldCount != 0 || newCount < expectedReferenceCount)
        {
            throw new InvalidDataException("The prepared Loadouts.json did not contain the expected migrated references.");
        }

        var comparisonRoot = migratedRoot.DeepClone().AsObject();
        var revertedCount = ReplaceProspectReferences(comparisonRoot, newProspectId, oldProspectId);
        if (revertedCount < expectedReferenceCount || !JsonNode.DeepEquals(originalRoot, comparisonRoot))
        {
            throw new InvalidDataException("Loadouts.json verification found changes outside the intended ProspectID references.");
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // A non-json transaction file is safer than touching the live loadout again.
        }
    }
}
