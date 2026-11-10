using IcarusSaveLib;
using ProspectRenamer.Core.Services;
using UeSaveGame;
using UeSaveGame.PropertyTypes;
using UeSaveGame.StructData;

namespace ProspectRenamer.Tests;

public sealed class IcarusProspectCodecTests
{
    [Fact]
    public void Codec_RoundTripsSyntheticSaveAndUpdatesBothProspectIdRepresentations()
    {
        var original = CreateSyntheticSave("Gammel Øvelse");
        using var source = new MemoryStream();
        original.Save(source);
        source.Position = 0;

        var codec = new IcarusProspectCodec();
        var document = codec.Load(source);
        codec.SetProspectId(document, "Ny Øvelse");

        using var renamedStream = new MemoryStream();
        codec.Save(document, renamedStream);
        renamedStream.Position = 0;
        var verified = codec.Load(renamedStream);
        var save = Assert.IsType<ProspectSave>(verified.NativeDocument);
        var blobId = ((PropertiesStruct)((StructProperty)save.ProspectData.Single().Property!).Value!)
            .Properties
            .Select(property => property.Property)
            .OfType<StrProperty>()
            .Single()
            .Value!
            .Value;

        Assert.Equal("Ny Øvelse", verified.ProspectId);
        Assert.Equal("Ny Øvelse", save.ProspectInfo.ProspectID);
        Assert.Equal("Ny Øvelse", blobId);
        Assert.NotEmpty(save.ProspectBlob.Hash);
        Assert.True(save.ProspectBlob.UncompressedLength > 0);
    }

    private static ProspectSave CreateSyntheticSave(string name)
    {
        var save = new ProspectSave
        {
            ProspectInfo = new FProspectInfo
            {
                ProspectID = name,
                ProspectDTKey = "OpenWorld_Styx",
                FactionMissionDTKey = "Test_Mission",
                AssociatedMembers = [],
                CustomSettings = []
            }
        };

        var idProperty = new StrProperty(new FString(nameof(FProspectInfo.ProspectID)))
        {
            Value = new FString(name)
        };
        var idTag = new FPropertyTag(
            new FString(nameof(FProspectInfo.ProspectID)),
            new FPropertyTypeName(new FString("StrProperty")),
            EPropertyTagFlags.None)
        {
            Property = idProperty
        };
        var infoData = new PropertiesStruct();
        infoData.Properties.Add(idTag);
        var infoProperty = new StructProperty(new FString("ProspectInfo"))
        {
            StructType = new FPropertyTypeName(new FString("ProspectInfo")),
            Value = infoData
        };
        save.ProspectData.Add(new FPropertyTag(
            new FString("ProspectInfo"),
            new FPropertyTypeName(new FString("StructProperty")),
            EPropertyTagFlags.None)
        {
            Property = infoProperty
        });

        return save;
    }
}
