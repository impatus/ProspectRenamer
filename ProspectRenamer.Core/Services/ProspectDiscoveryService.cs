using ProspectRenamer.Core.Models;

namespace ProspectRenamer.Core.Services;

public sealed class ProspectDiscoveryService
{
    private readonly string _localAppData;

    public ProspectDiscoveryService(string? localAppData = null)
    {
        _localAppData = localAppData ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    }

    public IReadOnlyList<ProspectLocation> FindAutomaticLocations()
    {
        var playerData = Path.Combine(_localAppData, "Icarus", "Saved", "PlayerData");
        return FindAccountProspectFolders(playerData, ProspectLocationKind.Automatic);
    }

    public IReadOnlyList<ProspectLocation> ResolveManualSelection(string selectedPath)
    {
        if (string.IsNullOrWhiteSpace(selectedPath))
        {
            return [];
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(selectedPath.Trim());
        }
        catch
        {
            return [];
        }

        if (!Directory.Exists(fullPath))
        {
            return [];
        }

        var results = new List<ProspectLocation>();
        if (Path.GetFileName(fullPath).Equals("Prospects", StringComparison.OrdinalIgnoreCase))
        {
            results.Add(CreateLocation(fullPath, ProspectLocationKind.Custom));
        }

        AddIfProspectsFolder(results, Path.Combine(fullPath, "Prospects"));
        AddAccountFolders(results, Path.Combine(fullPath, "PlayerData"));
        AddAccountFolders(results, Path.Combine(fullPath, "Saved", "PlayerData"));
        AddAccountFolders(results, fullPath);

        return DistinctLocations(results);
    }

    private IReadOnlyList<ProspectLocation> FindAccountProspectFolders(string playerData, ProspectLocationKind kind)
    {
        var results = new List<ProspectLocation>();
        if (!Directory.Exists(playerData))
        {
            return results;
        }

        try
        {
            foreach (var accountDirectory in Directory.EnumerateDirectories(playerData))
            {
                var prospects = Path.Combine(accountDirectory, "Prospects");
                if (Directory.Exists(prospects))
                {
                    results.Add(new ProspectLocation(
                        Path.GetFullPath(prospects),
                        Path.GetFileName(accountDirectory),
                        kind));
                }
            }
        }
        catch (UnauthorizedAccessException)
        {
            // A single inaccessible root is represented by an empty result; the UI reports it cleanly.
        }
        catch (IOException)
        {
        }

        return DistinctLocations(results);
    }

    private static void AddAccountFolders(List<ProspectLocation> results, string candidatePlayerData)
    {
        if (!Directory.Exists(candidatePlayerData))
        {
            return;
        }

        try
        {
            foreach (var child in Directory.EnumerateDirectories(candidatePlayerData))
            {
                AddIfProspectsFolder(results, Path.Combine(child, "Prospects"));
            }
        }
        catch (UnauthorizedAccessException)
        {
        }
        catch (IOException)
        {
        }
    }

    private static void AddIfProspectsFolder(List<ProspectLocation> results, string path)
    {
        if (Directory.Exists(path))
        {
            results.Add(CreateLocation(path, ProspectLocationKind.Custom));
        }
    }

    private static ProspectLocation CreateLocation(string prospectsPath, ProspectLocationKind kind)
    {
        var parent = Directory.GetParent(prospectsPath);
        var account = parent?.Name ?? "Custom";
        return new ProspectLocation(Path.GetFullPath(prospectsPath), account, kind);
    }

    private static IReadOnlyList<ProspectLocation> DistinctLocations(IEnumerable<ProspectLocation> locations) =>
        locations
            .GroupBy(location => location.DirectoryPath.TrimEnd(Path.DirectorySeparatorChar), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(location => location.AccountId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
}
