using ProspectRenamer.Core.Services;

namespace ProspectRenamer.Tests;

public sealed class NameValidationServiceTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("bad<name")]
    [InlineData("name.")]
    [InlineData("CON")]
    [InlineData("com1.notes")]
    public void Validate_RejectsInvalidWindowsNames(string name)
    {
        using var temp = new TestDirectory();
        var result = new NameValidationService().Validate(name, temp.Path);
        Assert.False(result.IsValid);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void Validate_TrimsWhitespaceAndAllowsNorwegianUnicode()
    {
        using var temp = new TestDirectory();
        var result = new NameValidationService().Validate("  Øvelse på Styx  ", temp.Path);
        Assert.True(result.IsValid);
        Assert.Equal("Øvelse på Styx", result.NormalizedName);
    }

    [Fact]
    public void Validate_RejectsDuplicateIgnoringCase()
    {
        using var temp = new TestDirectory();
        File.WriteAllText(System.IO.Path.Combine(temp.Path, "Existing Prospect.json"), "{}");

        var result = new NameValidationService().Validate("existing prospect", temp.Path);

        Assert.False(result.IsValid);
        Assert.Contains("already", result.Error!, StringComparison.OrdinalIgnoreCase);
    }
}
