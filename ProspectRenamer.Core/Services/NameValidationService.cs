using ProspectRenamer.Core.Models;

namespace ProspectRenamer.Core.Services;

public sealed class NameValidationService
{
    private static readonly HashSet<string> ReservedNames = BuildReservedNames();

    public ValidationResult Validate(string? requestedName, string folderPath, string? currentFilePath = null)
    {
        var normalized = requestedName?.Trim() ?? string.Empty;
        if (normalized.Length == 0)
        {
            return ValidationResult.Invalid(normalized, "Enter a prospect name.");
        }

        if (normalized.EndsWith(' ') || normalized.EndsWith('.'))
        {
            return ValidationResult.Invalid(normalized, "The name cannot end with a space or dot.");
        }

        if (normalized.Any(character => character < 32 || Path.GetInvalidFileNameChars().Contains(character)))
        {
            return ValidationResult.Invalid(normalized, "The name contains a character Windows cannot use in a file name.");
        }

        var firstSegment = normalized.Split('.')[0];
        if (ReservedNames.Contains(firstSegment))
        {
            return ValidationResult.Invalid(normalized, $"'{firstSegment}' is a reserved Windows file name.");
        }

        if ($"{normalized}.json".Length > 255)
        {
            return ValidationResult.Invalid(normalized, "The name is too long for a Windows file name.");
        }

        if (currentFilePath is not null &&
            string.Equals(Path.GetFileNameWithoutExtension(currentFilePath), normalized, StringComparison.OrdinalIgnoreCase))
        {
            return ValidationResult.Invalid(normalized, "Choose a different name from the current prospect name.");
        }

        if (Directory.Exists(folderPath))
        {
            try
            {
                var currentFullPath = string.IsNullOrWhiteSpace(currentFilePath)
                    ? null
                    : Path.GetFullPath(currentFilePath);
                var duplicate = Directory.EnumerateFiles(folderPath, "*.json", SearchOption.TopDirectoryOnly)
                    .Any(path =>
                        !string.Equals(Path.GetFullPath(path), currentFullPath, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(Path.GetFileNameWithoutExtension(path), normalized, StringComparison.OrdinalIgnoreCase));

                if (duplicate)
                {
                    return ValidationResult.Invalid(normalized, "Another prospect in this folder already uses that name.");
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return ValidationResult.Invalid(normalized, "The prospect folder could not be checked for duplicate names.");
            }
        }

        return ValidationResult.Valid(normalized);
    }

    private static HashSet<string> BuildReservedNames()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "CON", "PRN", "AUX", "NUL"
        };

        for (var index = 1; index <= 9; index++)
        {
            names.Add($"COM{index}");
            names.Add($"LPT{index}");
        }

        return names;
    }
}
