using ProspectRenamer.Core.Services;

namespace ProspectRenamer.Tests;

public sealed class BackupServiceTests
{
    [Fact]
    public async Task CreateBackupAsync_UsesAccountProspectTimestampAndNeverOverwrites()
    {
        using var temp = new TestDirectory();
        var source = System.IO.Path.Combine(temp.Path, "Old Name.json");
        await File.WriteAllTextAsync(source, "untouched");
        var backups = temp.CreateDirectory("Backups");
        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 8, 12, 22, 15, 30, TimeSpan.Zero));
        var service = new BackupService(backups, clock);

        var first = await service.CreateBackupAsync(source, "7656119", "Old Name");
        var second = await service.CreateBackupAsync(source, "7656119", "Old Name");

        Assert.EndsWith(System.IO.Path.Combine("7656119", "Old Name", "Old Name_2026-08-12_221530.json"), first);
        Assert.EndsWith("_1.json", second);
        Assert.Equal("untouched", await File.ReadAllTextAsync(first));
        Assert.Equal("untouched", await File.ReadAllTextAsync(second));
    }

    [Fact]
    public async Task CreateAccountFileBackupAsync_StoresLoadoutOutsideProspectBackups()
    {
        using var temp = new TestDirectory();
        var source = System.IO.Path.Combine(temp.Path, "Loadouts.json");
        await File.WriteAllTextAsync(source, "{\"Loadouts\":[]}");
        var backups = temp.CreateDirectory("Backups");
        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 8, 13, 0, 15, 30, TimeSpan.Zero));
        var service = new BackupService(backups, clock);

        var backup = await service.CreateAccountFileBackupAsync(source, "7656119", "Loadouts");

        Assert.EndsWith(
            System.IO.Path.Combine("7656119", "AccountFiles", "Loadouts", "Loadouts_2026-08-13_001530.json"),
            backup);
        Assert.Equal("{\"Loadouts\":[]}", await File.ReadAllTextAsync(backup));
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
}
