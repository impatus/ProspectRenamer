using ProspectRenamer.Core.Services;

namespace ProspectRenamer.Tests;

public sealed class ProspectDiscoveryServiceTests
{
    [Fact]
    public void FindAutomaticLocations_FindsAllAccountAndDedicatedServerFolders()
    {
        using var temp = new TestDirectory();
        temp.CreateDirectory("Icarus", "Saved", "PlayerData", "76561198000000001", "Prospects");
        temp.CreateDirectory("Icarus", "Saved", "PlayerData", "76561198000000002", "Prospects");
        temp.CreateDirectory("Icarus", "Saved", "PlayerData", "DedicatedServer", "Prospects");
        temp.CreateDirectory("Icarus", "Saved", "PlayerData", "NotAnAccount");

        var service = new ProspectDiscoveryService(temp.Path);
        var locations = service.FindAutomaticLocations();

        Assert.Equal(3, locations.Count);
        Assert.Contains(locations, location => location.AccountId == "DedicatedServer");
        Assert.Contains(locations, location => location.AccountId == "76561198000000001");
        Assert.All(locations, location => Assert.EndsWith("Prospects", location.DirectoryPath));
    }

    [Fact]
    public void ResolveManualSelection_AcceptsProspectsOrIcarusParent()
    {
        using var temp = new TestDirectory();
        var prospects = temp.CreateDirectory("Icarus", "Saved", "PlayerData", "42", "Prospects");
        var service = new ProspectDiscoveryService(temp.Path);

        var direct = service.ResolveManualSelection(prospects);
        var parent = service.ResolveManualSelection(System.IO.Path.Combine(temp.Path, "Icarus"));

        Assert.Single(direct);
        Assert.Single(parent);
        Assert.Equal(System.IO.Path.GetFullPath(prospects), direct[0].DirectoryPath);
        Assert.Equal(System.IO.Path.GetFullPath(prospects), parent[0].DirectoryPath);
    }
}
