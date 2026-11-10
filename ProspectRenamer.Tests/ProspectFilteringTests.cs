using ProspectRenamer.Core.Models;
using ProspectRenamer.Core.Services;

namespace ProspectRenamer.Tests;

public sealed class ProspectFilteringTests
{
    [Fact]
    public async Task LoadProspectsAsync_HidesMissionInstancesButKeepsPersistentProspects()
    {
        using var temp = new TestDirectory();
        var prospects = temp.CreateDirectory("Account", "Prospects");
        await File.WriteAllTextAsync(
            Path.Combine(prospects, "My Open World.json"),
            CreateSaveHeader("My Open World", "OpenWorld_Styx", string.Empty));
        await File.WriteAllTextAsync(
            Path.Combine(prospects, "9C0190E043C653C2511D678003D26592.json"),
            CreateSaveHeader("9C0190E043C653C2511D678003D26592", "ELY_SQ_Trials", "ELY_SQ_Trials"));

        var service = new ProspectService(
            new NeverUsedCodec(),
            new BackupService(temp.CreateDirectory("Backups")),
            new NameValidationService());

        var entries = await service.LoadProspectsAsync([
            new ProspectLocation(prospects, "Account", ProspectLocationKind.Automatic)
        ]);

        var entry = Assert.Single(entries);
        Assert.Equal("My Open World", entry.ProspectName);
        Assert.False(entry.IsMission);
    }

    private static string CreateSaveHeader(string id, string prospectType, string missionType) => $$"""
        {
          "ProspectInfo": {
            "ProspectID": "{{id}}",
            "ProspectDTKey": "{{prospectType}}",
            "FactionMissionDTKey": "{{missionType}}"
          },
          "ProspectBlob": { "BinaryBlob": "encoded-data" }
        }
        """;

    private sealed class NeverUsedCodec : IProspectCodec
    {
        public DecodedProspect Load(Stream stream) => throw new NotSupportedException();
        public void SetProspectId(DecodedProspect prospect, string newName) => throw new NotSupportedException();
        public void Save(DecodedProspect prospect, Stream stream) => throw new NotSupportedException();
    }
}
