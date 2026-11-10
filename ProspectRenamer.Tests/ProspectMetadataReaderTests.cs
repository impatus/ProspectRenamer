using System.Text;
using ProspectRenamer.Core.Services;

namespace ProspectRenamer.Tests;

public sealed class ProspectMetadataReaderTests
{
    [Fact]
    public void Read_ReturnsHeaderWithoutDecodingTheBlob()
    {
        const string json = """
            {
              "ProspectInfo": {
                "ProspectID": "Norsk Øvelse",
                "ProspectDTKey": "OpenWorld_Styx",
                "FactionMissionDTKey": "Mission_Test"
              },
              "ProspectBlob": {
                "BinaryBlob": "not-decoded-for-the-list"
              }
            }
            """;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));

        var metadata = new IcarusProspectMetadataReader().Read(stream);

        Assert.Equal("Norsk Øvelse", metadata.ProspectId);
        Assert.Equal("Mission_Test", metadata.ProspectType);
        Assert.Equal("Mission_Test", metadata.FactionMissionType);
    }

    [Fact]
    public void Read_RejectsAJsonFileWithoutAProspectBlob()
    {
        const string json = """{ "ProspectInfo": { "ProspectID": "Not a save" } }""";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));

        Assert.Throws<ProspectFormatException>(() => new IcarusProspectMetadataReader().Read(stream));
    }

    [Fact]
    public void Read_DetectsUtf16ProspectFiles()
    {
        const string json = """
            {
              "ProspectInfo": { "ProspectID": "Helt på Tryne", "ProspectDTKey": "OpenWorld_Olympus" },
              "ProspectBlob": { "BinaryBlob": "encoded-data" }
            }
            """;
        using var stream = new MemoryStream();
        using (var writer = new StreamWriter(stream, Encoding.Unicode, leaveOpen: true))
        {
            writer.Write(json);
        }
        stream.Position = 0;

        var metadata = new IcarusProspectMetadataReader().Read(stream);

        Assert.Equal("Helt på Tryne", metadata.ProspectId);
        Assert.Equal("OpenWorld_Olympus", metadata.ProspectType);
        Assert.Null(metadata.FactionMissionType);
    }
}
