using System.Globalization;
using System.Reflection;
using System.Threading.Tasks;
using BookDB.Help;
using Xunit;

namespace BookDB.Logic.Tests.Help;

public class HelpContentLoaderTests
{
    // English content returns non-empty, non-error string
    [Fact]
    public async Task LoadAsync_EnglishShortcuts_ReturnsNonEmptyContent()
    {
        var result = await HelpContentLoader.LoadAsync("shortcuts", new CultureInfo("en"));
        Assert.False(string.IsNullOrWhiteSpace(result));
        Assert.DoesNotContain("Content not found", result);
    }

    // Swedish file exists and is returned for sv culture
    [Fact]
    public async Task LoadAsync_SwedishShortcuts_ReturnsSwedishContent()
    {
        var result = await HelpContentLoader.LoadAsync("shortcuts", new CultureInfo("sv"));
        Assert.False(string.IsNullOrWhiteSpace(result));
        Assert.DoesNotContain("Content not found", result);
    }

    // Unknown culture falls back to English (no .de.md file exists)
    [Fact]
    public async Task LoadAsync_UnknownCulture_FallsBackToEnglish()
    {
        var result = await HelpContentLoader.LoadAsync("shortcuts", new CultureInfo("de"));
        Assert.False(string.IsNullOrWhiteSpace(result));
        Assert.DoesNotContain("Content not found", result);
    }

    // All 8 embedded resource names match loader expectations
    [Fact]
    public void ResourceNames_MatchLoaderExpectations()
    {
        var assembly = typeof(HelpContentLoader).Assembly;
        var names = assembly.GetManifestResourceNames();
        Assert.Contains("BookDB.Help.Content.shortcuts.md", names);
        Assert.Contains("BookDB.Help.Content.shortcuts.sv.md", names);
        Assert.Contains("BookDB.Help.Content.glossary.md", names);
        Assert.Contains("BookDB.Help.Content.glossary.sv.md", names);
        Assert.Contains("BookDB.Help.Content.import-guide.md", names);
        Assert.Contains("BookDB.Help.Content.import-guide.sv.md", names);
        Assert.Contains("BookDB.Help.Content.data-sources.md", names);
        Assert.Contains("BookDB.Help.Content.data-sources.sv.md", names);
    }
}
