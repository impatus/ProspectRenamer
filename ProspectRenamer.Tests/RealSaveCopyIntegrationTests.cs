using System.Text.Json.Nodes;
using ProspectRenamer.Core.Services;

namespace ProspectRenamer.Tests;

public sealed class RealSaveCopyIntegrationTests
{
    [Fact]
    public async Task RenameAsync_OnOptInRealCopies_MigratesLoadoutWithoutTouchingSources()
    {
        var sourceProspect = Environment.GetEnvironmentVariable("PROSPECT_RENAMER_TEST_PROSPECT");
        var sourceLoadouts = Environment.GetEnvironmentVariable("PROSPECT_RENAMER_TEST_LOADOUTS");
        if (string.IsNullOrWhiteSpace(sourceProspect) || string.IsNullOrWhiteSpace(sourceLoadouts))
        {
            return;
        }

        var sourceProspectHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(sourceProspect)));
        var sourceLoadoutsHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(sourceLoadouts)));

        using var temp = new TestDirectory();
        var prospects = temp.CreateDirectory("PlayerData", "RealCopy", "Prospects");
        var loadoutDirectory = temp.CreateDirectory("PlayerData", "RealCopy", "Loadout");
        var backups = temp.CreateDirectory("Backups");
        var copiedProspect = Path.Combine(prospects, Path.GetFileName(sourceProspect));
        var copiedLoadouts = Path.Combine(loadoutDirectory, "Loadouts.json");
        File.Copy(sourceProspect, copiedProspect);
        File.Copy(sourceLoadouts, copiedLoadouts);
        var sourceFileName = Path.GetFileName(sourceProspect);
        var sourceDirectory = Path.GetDirectoryName(sourceProspect)!;
        var copiedSidecars = Directory.GetFiles(sourceDirectory, $"{sourceFileName}.backup*")
            .Where(path => System.Text.RegularExpressions.Regex.IsMatch(
                Path.GetFileName(path),
                $"^{System.Text.RegularExpressions.Regex.Escape(sourceFileName)}\\.backup(?:_\\d+)?$",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            .ToArray();
        foreach (var sidecar in copiedSidecars)
        {
            File.Copy(sidecar, Path.Combine(prospects, Path.GetFileName(sidecar)));
        }

        var service = new ProspectService(
            new IcarusProspectCodec(),
            new BackupService(backups),
            new NameValidationService());
        var entry = service.LoadEntry(copiedProspect, "RealCopy");
        var newName = $"Prospect Renamer Integration {Guid.NewGuid():N}";

        var result = await service.RenameAsync(entry, newName);

        Assert.True(result.IsSuccess, result.Error);
        Assert.False(File.Exists(copiedProspect));
        Assert.True(File.Exists(result.NewFilePath));
        Assert.True(result.MigratedLoadoutReferences > 0);
        Assert.True(File.Exists(result.LoadoutBackupPath));
        Assert.Equal(copiedSidecars.Length, result.ArchivedGameBackups);
        if (copiedSidecars.Length > 0)
        {
            Assert.NotNull(result.GameBackupArchivePath);
            Assert.Equal(copiedSidecars.Length, Directory.GetFiles(result.GameBackupArchivePath!).Length);
        }
        else
        {
            Assert.Null(result.GameBackupArchivePath);
        }
        Assert.DoesNotContain(
            Directory.GetFiles(prospects),
            path => Path.GetFileName(path).StartsWith(sourceFileName + ".backup", StringComparison.OrdinalIgnoreCase));
        var migratedLoadoutBytes = await File.ReadAllBytesAsync(copiedLoadouts);
        Assert.True(migratedLoadoutBytes.Length >= 2);
        Assert.Equal(0xFF, migratedLoadoutBytes[0]);
        Assert.Equal(0xFE, migratedLoadoutBytes[1]);

        await using (var renamedStream = File.OpenRead(result.NewFilePath!))
        {
            var verified = new IcarusProspectCodec().Load(renamedStream);
            Assert.Equal(newName, verified.ProspectId);
        }

        var loadoutsRoot = JsonNode.Parse(await File.ReadAllTextAsync(copiedLoadouts))!.AsObject();
        var migratedReferences = loadoutsRoot["Loadouts"]!.AsArray()
            .OfType<JsonObject>()
            .Count(loadout => string.Equals(
                loadout["AssociatedProspect"]?["ProspectID"]?.GetValue<string>(),
                newName,
                StringComparison.Ordinal));
        Assert.Equal(result.MigratedLoadoutReferences, migratedReferences);

        Assert.Equal(sourceProspectHash, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(sourceProspect))));
        Assert.Equal(sourceLoadoutsHash, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(sourceLoadouts))));
    }

    [Fact]
    public async Task RenameAsync_OnOptInRealSidecarCopies_ArchivesEveryOldIcarusAutosave()
    {
        var sourceProspect = Environment.GetEnvironmentVariable("PROSPECT_RENAMER_TEST_SIDECAR_PROSPECT");
        var sourceLoadouts = Environment.GetEnvironmentVariable("PROSPECT_RENAMER_TEST_LOADOUTS");
        if (string.IsNullOrWhiteSpace(sourceProspect) || string.IsNullOrWhiteSpace(sourceLoadouts))
        {
            return;
        }

        var sourceDirectory = Path.GetDirectoryName(sourceProspect)!;
        var sourceFileName = Path.GetFileName(sourceProspect);
        var sidecars = Directory.GetFiles(sourceDirectory, $"{sourceFileName}.backup*")
            .Where(path => System.Text.RegularExpressions.Regex.IsMatch(
                Path.GetFileName(path),
                $"^{System.Text.RegularExpressions.Regex.Escape(sourceFileName)}\\.backup(?:_\\d+)?$",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            .ToArray();
        Assert.NotEmpty(sidecars);
        var sourceHashes = sidecars.ToDictionary(
            path => Path.GetFileName(path)!,
            path => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))),
            StringComparer.OrdinalIgnoreCase);

        using var temp = new TestDirectory();
        var prospects = temp.CreateDirectory("PlayerData", "SidecarCopy", "Prospects");
        var loadoutDirectory = temp.CreateDirectory("PlayerData", "SidecarCopy", "Loadout");
        var backups = temp.CreateDirectory("Backups");
        var copiedProspect = Path.Combine(prospects, sourceFileName);
        File.Copy(sourceProspect, copiedProspect);
        File.Copy(sourceLoadouts, Path.Combine(loadoutDirectory, "Loadouts.json"));
        foreach (var sidecar in sidecars)
        {
            File.Copy(sidecar, Path.Combine(prospects, Path.GetFileName(sidecar)));
        }

        var service = new ProspectService(
            new IcarusProspectCodec(),
            new BackupService(backups),
            new NameValidationService());
        var entry = service.LoadEntry(copiedProspect, "SidecarCopy");
        var result = await service.RenameAsync(entry, $"Sidecar Integration {Guid.NewGuid():N}");

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(sidecars.Length, result.ArchivedGameBackups);
        Assert.NotNull(result.GameBackupArchivePath);
        var archivedFiles = Directory.GetFiles(result.GameBackupArchivePath!);
        Assert.Equal(sidecars.Length, archivedFiles.Length);
        foreach (var archivedFile in archivedFiles)
        {
            Assert.Equal(
                sourceHashes[Path.GetFileName(archivedFile)],
                Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(archivedFile))));
        }

        Assert.DoesNotContain(
            Directory.GetFiles(prospects),
            path => Path.GetFileName(path).StartsWith(sourceFileName + ".backup", StringComparison.OrdinalIgnoreCase));
        foreach (var source in sidecars)
        {
            Assert.Equal(
                sourceHashes[Path.GetFileName(source)],
                Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(source))));
        }
    }
}
