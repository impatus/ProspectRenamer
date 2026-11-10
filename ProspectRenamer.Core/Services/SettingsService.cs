using System.Text.Json;

namespace ProspectRenamer.Core.Services;

public sealed class AppSettings
{
    public bool UseAutoDetect { get; set; } = true;
    public string? CustomFolder { get; set; }
    public bool ShowRenameWarning { get; set; } = true;
    public string? BackupFolder { get; set; }
}

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    public SettingsService(string? settingsRoot = null)
    {
        var root = settingsRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ProspectRenamer");
        SettingsPath = Path.Combine(root, "settings.json");
    }

    public string SettingsPath { get; }

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return new AppSettings();
            }

            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath), SerializerOptions) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        var directory = Path.GetDirectoryName(SettingsPath)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = $"{SettingsPath}.tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(settings, SerializerOptions));
        File.Move(temporaryPath, SettingsPath, true);
    }
}
