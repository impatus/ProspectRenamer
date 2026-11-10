using System.Text.Json;
using ProspectRenamer.Core.Models;
using ProspectRenamer.Core.Services;

namespace ProspectRenamer.Tests;

public sealed class ProspectServiceRenameTests
{
    [Fact]
    public async Task RenameAsync_WritesVerifiesFinalizesBacksUpThenRemovesOriginal()
    {
        using var temp = new TestDirectory();
        var prospects = temp.CreateDirectory("PlayerData", "123", "Prospects");
        var backups = temp.CreateDirectory("Backups");
        var originalPath = System.IO.Path.Combine(prospects, "Old Prospect.json");
        await File.WriteAllTextAsync(originalPath, "{\"id\":\"Old Prospect\",\"type\":\"Styx\",\"untouched\":\"keep me\"}");

        var codec = new FakeProspectCodec();
        var service = new ProspectService(codec, new BackupService(backups), new NameValidationService(), new FakeMetadataReader());
        var entry = service.LoadEntry(originalPath, "123");

        var result = await service.RenameAsync(entry, "Ny Øvelse");

        Assert.True(result.IsSuccess, result.Error);
        Assert.False(File.Exists(originalPath));
        Assert.True(File.Exists(result.NewFilePath));
        Assert.True(File.Exists(result.BackupPath));
        var renamed = JsonSerializer.Deserialize<FakeDocument>(await File.ReadAllTextAsync(result.NewFilePath!));
        var backupText = await File.ReadAllTextAsync(result.BackupPath!);
        Assert.Equal("Ny Øvelse", renamed!.Id);
        Assert.Equal("keep me", renamed.Untouched);
        Assert.Equal("{\"id\":\"Old Prospect\",\"type\":\"Styx\",\"untouched\":\"keep me\"}", backupText);
        Assert.Empty(Directory.GetFiles(prospects, ".prospectrenamer-*.tmp"));
    }

    [Fact]
    public async Task RenameAsync_DuplicateNameLeavesOriginalUntouchedAndCreatesNoBackup()
    {
        using var temp = new TestDirectory();
        var prospects = temp.CreateDirectory("Prospects");
        var backups = temp.CreateDirectory("Backups");
        var originalPath = System.IO.Path.Combine(prospects, "Old.json");
        await File.WriteAllTextAsync(originalPath, "{\"id\":\"Old\",\"type\":\"Olympus\",\"untouched\":\"keep\"}");
        await File.WriteAllTextAsync(System.IO.Path.Combine(prospects, "Taken.json"), "{}");
        var service = new ProspectService(new FakeProspectCodec(), new BackupService(backups), new NameValidationService(), new FakeMetadataReader());
        var entry = service.LoadEntry(originalPath, "test");

        var result = await service.RenameAsync(entry, "taken");

        Assert.False(result.IsSuccess);
        Assert.True(File.Exists(originalPath));
        Assert.Empty(Directory.GetFiles(backups, "*.json", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task DuplicateAsync_CreatesIndependentSaveAndKeepsOriginal()
    {
        using var temp = new TestDirectory();
        var prospects = temp.CreateDirectory("PlayerData", "123", "Prospects");
        var backups = temp.CreateDirectory("Backups");
        var originalPath = System.IO.Path.Combine(prospects, "Old Prospect.json");
        await File.WriteAllTextAsync(originalPath, "{\"id\":\"Old Prospect\",\"type\":\"Styx\",\"untouched\":\"keep me\"}");

        var service = new ProspectService(
            new FakeProspectCodec(),
            new BackupService(backups),
            new NameValidationService(),
            new FakeMetadataReader());
        var entry = service.LoadEntry(originalPath, "123");

        var result = await service.DuplicateAsync(entry, "Copied Prospect");

        Assert.True(result.IsSuccess, result.Error);
        Assert.True(File.Exists(originalPath));
        Assert.True(File.Exists(result.NewFilePath));
        Assert.True(File.Exists(result.BackupPath));
        var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var original = JsonSerializer.Deserialize<FakeDocument>(await File.ReadAllTextAsync(originalPath), jsonOptions);
        var duplicate = JsonSerializer.Deserialize<FakeDocument>(await File.ReadAllTextAsync(result.NewFilePath!), jsonOptions);
        Assert.Equal("Old Prospect", original!.Id);
        Assert.Equal("Copied Prospect", duplicate!.Id);
        Assert.Equal(original.Untouched, duplicate.Untouched);
        Assert.Empty(Directory.GetFiles(prospects, ".prospectrenamer-*.tmp"));
    }

    [Fact]
    public async Task RenameAsync_MigratesMatchingLocalLoadoutAndBacksItUp()
    {
        using var temp = new TestDirectory();
        var prospects = temp.CreateDirectory("PlayerData", "123", "Prospects");
        var loadoutDirectory = temp.CreateDirectory("PlayerData", "123", "Loadout");
        var backups = temp.CreateDirectory("Backups");
        var originalPath = System.IO.Path.Combine(prospects, "Old Prospect.json");
        var loadoutsPath = System.IO.Path.Combine(loadoutDirectory, "Loadouts.json");
        const string originalLoadouts = "{\"Loadouts\":[{\"Guid\":\"keep\",\"AssociatedProspect\":{\"ProspectID\":\"Old Prospect\",\"ProspectState\":\"Active\"},\"MetaItems\":[{\"Id\":7}]}],\"Other\":{\"Keep\":true}}";
        await File.WriteAllTextAsync(originalPath, "{\"id\":\"Old Prospect\",\"type\":\"Styx\",\"untouched\":\"keep me\"}");
        await File.WriteAllTextAsync(loadoutsPath, originalLoadouts);
        await File.WriteAllTextAsync(originalPath + ".backup", "game backup 0");
        await File.WriteAllTextAsync(originalPath + ".backup_1", "game backup 1");

        var service = CreateService(backups);
        var entry = service.LoadEntry(originalPath, "123");
        var result = await service.RenameAsync(entry, "Renamed Prospect");

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(1, result.MigratedLoadoutReferences);
        Assert.True(File.Exists(result.LoadoutBackupPath));
        Assert.Equal(originalLoadouts, await File.ReadAllTextAsync(result.LoadoutBackupPath!));
        using var migrated = JsonDocument.Parse(await File.ReadAllTextAsync(loadoutsPath));
        var migratedEntry = migrated.RootElement.GetProperty("Loadouts")[0];
        Assert.Equal("Renamed Prospect", migratedEntry.GetProperty("AssociatedProspect").GetProperty("ProspectID").GetString());
        Assert.Equal("keep", migratedEntry.GetProperty("Guid").GetString());
        Assert.True(migrated.RootElement.GetProperty("Other").GetProperty("Keep").GetBoolean());
        Assert.False(File.Exists(originalPath));
        Assert.True(File.Exists(result.NewFilePath));
        Assert.Equal(2, result.ArchivedGameBackups);
        Assert.True(Directory.Exists(result.GameBackupArchivePath));
        Assert.False(File.Exists(originalPath + ".backup"));
        Assert.False(File.Exists(originalPath + ".backup_1"));
        Assert.Equal(2, Directory.GetFiles(result.GameBackupArchivePath!).Length);
        Assert.Empty(Directory.GetFiles(loadoutDirectory, ".prospectrenamer-loadouts-*.tmp"));
    }

    [Fact]
    public async Task DuplicateAsync_DoesNotMigrateLocalLoadout()
    {
        using var temp = new TestDirectory();
        var prospects = temp.CreateDirectory("PlayerData", "123", "Prospects");
        var loadoutDirectory = temp.CreateDirectory("PlayerData", "123", "Loadout");
        var backups = temp.CreateDirectory("Backups");
        var originalPath = System.IO.Path.Combine(prospects, "Old Prospect.json");
        var loadoutsPath = System.IO.Path.Combine(loadoutDirectory, "Loadouts.json");
        const string originalLoadouts = "{\"Loadouts\":[{\"AssociatedProspect\":{\"ProspectID\":\"Old Prospect\"}}]}";
        await File.WriteAllTextAsync(originalPath, "{\"id\":\"Old Prospect\",\"type\":\"Styx\",\"untouched\":\"keep\"}");
        await File.WriteAllTextAsync(loadoutsPath, originalLoadouts);
        await File.WriteAllTextAsync(originalPath + ".backup", "keep this live");

        var service = CreateService(backups);
        var result = await service.DuplicateAsync(service.LoadEntry(originalPath, "123"), "Copy");

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(0, result.MigratedLoadoutReferences);
        Assert.Null(result.LoadoutBackupPath);
        Assert.Equal(originalLoadouts, await File.ReadAllTextAsync(loadoutsPath));
        Assert.Equal(0, result.ArchivedGameBackups);
        Assert.True(File.Exists(originalPath + ".backup"));
    }

    [Fact]
    public async Task RenameAsync_InvalidLoadoutStopsBeforeLiveFilesChange()
    {
        using var temp = new TestDirectory();
        var prospects = temp.CreateDirectory("PlayerData", "123", "Prospects");
        var loadoutDirectory = temp.CreateDirectory("PlayerData", "123", "Loadout");
        var backups = temp.CreateDirectory("Backups");
        var originalPath = System.IO.Path.Combine(prospects, "Old Prospect.json");
        var loadoutsPath = System.IO.Path.Combine(loadoutDirectory, "Loadouts.json");
        const string originalProspect = "{\"id\":\"Old Prospect\",\"type\":\"Styx\",\"untouched\":\"keep\"}";
        await File.WriteAllTextAsync(originalPath, originalProspect);
        await File.WriteAllTextAsync(loadoutsPath, "not-json");

        var service = CreateService(backups);
        var result = await service.RenameAsync(service.LoadEntry(originalPath, "123"), "Renamed");

        Assert.False(result.IsSuccess);
        Assert.True(File.Exists(originalPath));
        Assert.False(File.Exists(System.IO.Path.Combine(prospects, "Renamed.json")));
        Assert.Equal(originalProspect, await File.ReadAllTextAsync(originalPath));
        Assert.Equal("not-json", await File.ReadAllTextAsync(loadoutsPath));
    }

    [Fact]
    public async Task RenameAsync_SourceDeleteFailureRollsBackProspectAndLoadout()
    {
        using var temp = new TestDirectory();
        var prospects = temp.CreateDirectory("PlayerData", "123", "Prospects");
        var loadoutDirectory = temp.CreateDirectory("PlayerData", "123", "Loadout");
        var backups = temp.CreateDirectory("Backups");
        var originalPath = System.IO.Path.Combine(prospects, "Old Prospect.json");
        var loadoutsPath = System.IO.Path.Combine(loadoutDirectory, "Loadouts.json");
        const string originalProspect = "{\"id\":\"Old Prospect\",\"type\":\"Styx\",\"untouched\":\"keep\"}";
        const string originalLoadouts = "{\"Loadouts\":[{\"AssociatedProspect\":{\"ProspectID\":\"Old Prospect\"},\"Keep\":42}]}";
        await File.WriteAllTextAsync(originalPath, originalProspect);
        await File.WriteAllTextAsync(loadoutsPath, originalLoadouts);
        await File.WriteAllTextAsync(originalPath + ".backup", "rollback game backup");

        var service = CreateService(backups);
        var entry = service.LoadEntry(originalPath, "123");
        using var deletionBlocker = new FileStream(originalPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var result = await service.RenameAsync(entry, "Renamed");

        Assert.False(result.IsSuccess);
        Assert.True(File.Exists(originalPath));
        Assert.False(File.Exists(System.IO.Path.Combine(prospects, "Renamed.json")));
        Assert.Equal(originalLoadouts, await File.ReadAllTextAsync(loadoutsPath));
        Assert.Equal("rollback game backup", await File.ReadAllTextAsync(originalPath + ".backup"));
        Assert.Empty(Directory.GetFiles(loadoutDirectory, ".prospectrenamer-loadouts-*.tmp"));
    }

    private static ProspectService CreateService(string backups) =>
        new(new FakeProspectCodec(), new BackupService(backups), new NameValidationService(), new FakeMetadataReader());

    private sealed class FakeProspectCodec : IProspectCodec
    {
        public DecodedProspect Load(Stream stream)
        {
            var document = JsonSerializer.Deserialize<FakeDocument>(stream, JsonOptions) ?? throw new ProspectFormatException("Invalid fake prospect.");
            return new DecodedProspect(document.Id, document.Type, document);
        }

        public void SetProspectId(DecodedProspect prospect, string newName)
        {
            ((FakeDocument)prospect.NativeDocument).Id = newName;
            prospect.ProspectId = newName;
        }

        public void Save(DecodedProspect prospect, Stream stream) =>
            JsonSerializer.Serialize(stream, (FakeDocument)prospect.NativeDocument, JsonOptions);

        private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    }

    private sealed class FakeMetadataReader : IProspectMetadataReader
    {
        public ProspectMetadata Read(Stream stream)
        {
            var document = JsonSerializer.Deserialize<FakeDocument>(stream, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? throw new ProspectFormatException("Invalid fake prospect.");
            return new ProspectMetadata(document.Id, document.Type);
        }
    }

    public sealed class FakeDocument
    {
        public string Id { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string Untouched { get; set; } = string.Empty;
    }
}
