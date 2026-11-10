using ProspectRenamer.Core.Models;

namespace ProspectRenamer.Core.Services;

public sealed class DecodedProspect
{
    public DecodedProspect(string prospectId, string prospectType, object nativeDocument)
    {
        ProspectId = prospectId;
        ProspectType = prospectType;
        NativeDocument = nativeDocument;
    }

    public string ProspectId { get; set; }
    public string ProspectType { get; }
    public object NativeDocument { get; }
}

public interface IProspectCodec
{
    DecodedProspect Load(Stream stream);
    void SetProspectId(DecodedProspect prospect, string newName);
    void Save(DecodedProspect prospect, Stream stream);
}

public sealed class ProspectFormatException : Exception
{
    public ProspectFormatException(string message) : base(message)
    {
    }

    public ProspectFormatException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
