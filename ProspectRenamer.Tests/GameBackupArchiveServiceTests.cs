using System.Security.Cryptography;
using ProspectRenamer.Core.Services;

namespace ProspectRenamer.Tests;

public sealed class GameBackupArchiveServiceTests
{
    [Fact]
    public async Task PrepareRemoveAndRestore_PreservesEveryRecognizedGameBackup()
    {
        using var temp = new TestDirectory();
        var prospects = temp.CreateDirectory("PlayerData", "123", "Prospects");
        var backups = temp.CreateDirectory("Backups");
        var source = Path.Combine(prospects, "Old.json");
        await File.WriteAllTextAsync(source, "main");
        await File.WriteAllTextAsync(source + ".backup", "newest backup");
        await File.WriteAllTextAsync(source + ".backup_1", "older backup");
        await File.WriteAllTextAsync(source + ".backup_bad", "not recognized");
        var service = new GameBackupArchiveService(new BackupService(backups));

        var archive = await service.PrepareAsync(source, "123", "Old");

        Assert.NotNull(archive);
        Assert.Equal(2, archive!.FileCount);
        Assert.Equal(2, Directory.GetFiles(archive.ArchiveDirectory).Length);
        await service.RemoveLiveFilesAsync(archive);
        Assert.False(File.Exists(source + ".backup"));
        Assert.False(File.Exists(source + ".backup_1"));
        Assert.True(File.Exists(source + ".backup_bad"));

        await service.RestoreLiveFilesAsync(archive);
        Assert.Equal("newest backup", await File.ReadAllTextAsync(source + ".backup"));
        Assert.Equal("older backup", await File.ReadAllTextAsync(source + ".backup_1"));
    }

    [Fact]
    public async Task RemoveLiveFiles_StopsIfBackupChangedAfterArchiving()
    {
        using var temp = new TestDirectory();
        var prospects = temp.CreateDirectory("Prospects");
        var backups = temp.CreateDirectory("Backups");
        var source = Path.Combine(prospects, "Old.json");
        await File.WriteAllTextAsync(source, "main");
        await File.WriteAllTextAsync(source + ".backup", "original backup");
        var service = new GameBackupArchiveService(new BackupService(backups));
        var archive = await service.PrepareAsync(source, "123", "Old");
        await File.WriteAllTextAsync(source + ".backup", "changed backup");

        await Assert.ThrowsAsync<IOException>(() => service.RemoveLiveFilesAsync(archive!));

        Assert.Equal("changed backup", await File.ReadAllTextAsync(source + ".backup"));
        var archived = Directory.GetFiles(archive!.ArchiveDirectory).Single();
        Assert.Equal(
            Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("original backup"))),
            Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(archived))));
    }
}
