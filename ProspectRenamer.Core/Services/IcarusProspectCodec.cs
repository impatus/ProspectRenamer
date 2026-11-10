using IcarusSaveLib;
using UeSaveGame;
using UeSaveGame.DataTypes;
using UeSaveGame.PropertyTypes;
using UeSaveGame.StructData;

namespace ProspectRenamer.Core.Services;

public sealed class IcarusProspectCodec : IProspectCodec
{
    public DecodedProspect Load(Stream stream)
    {
        try
        {
            var save = ProspectSave.Load(stream) ??
                throw new ProspectFormatException("The file is not a valid Icarus prospect save.");

            if (string.IsNullOrWhiteSpace(save.ProspectInfo.ProspectID))
            {
                throw new ProspectFormatException("The prospect does not contain a valid ProspectID.");
            }

            return new DecodedProspect(
                save.ProspectInfo.ProspectID,
                GetProspectType(save),
                save);
        }
        catch (ProspectFormatException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new ProspectFormatException("The Icarus save parser could not read this prospect.", exception);
        }
    }

    public void SetProspectId(DecodedProspect prospect, string newName)
    {
        if (prospect.NativeDocument is not ProspectSave save)
        {
            throw new ProspectFormatException("The loaded prospect document has an unexpected format.");
        }

        var prospectInfo = save.ProspectInfo;
        prospectInfo.ProspectID = newName;
        save.ProspectInfo = prospectInfo;

        var prospectInfoProperty = save.ProspectData
            .FirstOrDefault(property => property.Name.Equals("ProspectInfo"))?
            .Property as StructProperty;
        var prospectInfoData = prospectInfoProperty?.Value as PropertiesStruct;
        var prospectIdProperty = prospectInfoData?.Properties
            .FirstOrDefault(property => property.Name.Equals(nameof(FProspectInfo.ProspectID)))?
            .Property as StrProperty;

        if (prospectIdProperty is null)
        {
            throw new ProspectFormatException("The internal ProspectInfo.ProspectID field was not found in the save blob.");
        }

        prospectIdProperty.Value = new FString(newName);
        prospect.ProspectId = newName;
    }

    public void Save(DecodedProspect prospect, Stream stream)
    {
        if (prospect.NativeDocument is not ProspectSave save)
        {
            throw new ProspectFormatException("The loaded prospect document has an unexpected format.");
        }

        save.Save(stream);
    }

    private static string GetProspectType(ProspectSave save)
    {
        var info = save.ProspectInfo;
        if (!string.IsNullOrWhiteSpace(info.FactionMissionDTKey))
        {
            return info.FactionMissionDTKey;
        }

        return string.IsNullOrWhiteSpace(info.ProspectDTKey) ? "Unknown" : info.ProspectDTKey;
    }
}
