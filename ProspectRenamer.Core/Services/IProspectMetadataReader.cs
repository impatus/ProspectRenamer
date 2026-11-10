using System.Text;
using System.Text.Json;
using ProspectRenamer.Core.Models;

namespace ProspectRenamer.Core.Services;

public interface IProspectMetadataReader
{
    ProspectMetadata Read(Stream stream);
}

/// <summary>
/// Reads only the small, uncompressed prospect header used by the list view.
/// The full IcarusSaveLib parser remains mandatory for every rename operation.
/// </summary>
public sealed class IcarusProspectMetadataReader : IProspectMetadataReader
{
    public ProspectMetadata Read(Stream stream)
    {
        try
        {
            using var reader = new StreamReader(
                stream,
                Encoding.UTF8,
                detectEncodingFromByteOrderMarks: true,
                bufferSize: 16 * 1024,
                leaveOpen: true);
            var json = reader.ReadToEnd();
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip,
                MaxDepth = 64
            });

            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("ProspectInfo", out var prospectInfo) ||
                prospectInfo.ValueKind != JsonValueKind.Object)
            {
                throw new ProspectFormatException("The file does not contain an Icarus ProspectInfo header.");
            }

            var prospectId = ReadRequiredString(prospectInfo, "ProspectID");
            var missionType = ReadOptionalString(prospectInfo, "FactionMissionDTKey");
            var prospectType = ReadOptionalString(prospectInfo, "ProspectDTKey");

            if (!document.RootElement.TryGetProperty("ProspectBlob", out var prospectBlob) ||
                prospectBlob.ValueKind != JsonValueKind.Object ||
                string.IsNullOrWhiteSpace(ReadOptionalString(prospectBlob, "BinaryBlob")))
            {
                throw new ProspectFormatException("The file does not contain an Icarus prospect data blob.");
            }

            return new ProspectMetadata(
                prospectId,
                !string.IsNullOrWhiteSpace(missionType)
                    ? missionType
                    : !string.IsNullOrWhiteSpace(prospectType) ? prospectType : "Unknown",
                missionType);
        }
        catch (ProspectFormatException)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException or IOException or InvalidOperationException)
        {
            throw new ProspectFormatException("The prospect metadata could not be read.", exception);
        }
    }

    private static string ReadRequiredString(JsonElement parent, string propertyName)
    {
        var value = ReadOptionalString(parent, propertyName);
        return string.IsNullOrWhiteSpace(value)
            ? throw new ProspectFormatException($"The prospect header is missing {propertyName}.")
            : value;
    }

    private static string? ReadOptionalString(JsonElement parent, string propertyName) =>
        parent.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
}
