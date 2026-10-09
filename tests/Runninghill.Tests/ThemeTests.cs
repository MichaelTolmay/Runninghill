using System.Text;
using Runninghill.Clients;
using Xunit;

namespace Runninghill.Tests;

/// <summary>Checks brightness precedence, safe fallback, and validation of packaged company themes.</summary>
public sealed class ThemeTests
{
    /// <summary>Explicit brightness wins over the OS, while System follows its current setting.</summary>
    [Theory]
    [InlineData("Light", true, "light")]
    [InlineData("Dark", false, "dark")]
    [InlineData("System", true, "dark")]
    [InlineData("System", false, "light")]
    [InlineData("old-setting", true, "dark")]
    public void ResolvesBrightnessAndStaleCompanies(string mode, bool systemDark, string expected)
    {
        var theme = AppThemes.Resolve("removed-company", mode, systemDark);
        Assert.Equal("Runninghill_" + expected, theme.Id);
        Assert.NotEmpty(theme.Colors);
    }

    /// <summary>Every shipped palette has a partner and exactly the same semantic colour roles.</summary>
    [Fact]
    public void PackagedThemesAreCompletePairs()
    {
        Assert.False(AppThemes.HasInvalidFiles);
        foreach (var theme in AppThemes.All)
        {
            Assert.Contains(AppThemes.All, other => other.Company == theme.Company && other.Mode != theme.Mode);
            Assert.Equal(AppThemes.All[0].Colors.Keys.Order(), theme.Colors.Keys.Order());
        }
    }

    /// <summary>Theme data accepts colours, never executable CSS or network references.</summary>
    [Theory]
    [InlineData("url(https://example.com)")]
    [InlineData("red;display:none")]
    [InlineData("#12345G")]
    public void RejectsUnsafeColourValues(string value)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("{\"colors\":{\"Text\":\"" + value + "\"}}"));
        Assert.Throws<InvalidDataException>(() => AppThemes.Read("Acme_light", stream, new HashSet<string> { "Text" }));
    }

    /// <summary>Company identifiers may include underscores; only the final suffix chooses brightness.</summary>
    [Fact]
    public void ReadsACompanyPaletteWithoutAnAppCodeChange()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("{\"colors\":{\"Text\":\"#123456\"}}"));
        var theme = AppThemes.Read("Acme_Corp_dark", stream, new HashSet<string> { "Text" });
        Assert.Equal("Acme_Corp", theme.Company);
        Assert.Equal("dark", theme.Mode);
        Assert.Equal("#123456", theme.Colors["Text"]);
    }
}
