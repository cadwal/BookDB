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

    // Unknown culture falls back to English (no .fi.md file exists)
    [Fact]
    public async Task LoadAsync_UnknownCulture_FallsBackToEnglish()
    {
        var result = await HelpContentLoader.LoadAsync("shortcuts", new CultureInfo("fi"));
        Assert.False(string.IsNullOrWhiteSpace(result));
        Assert.DoesNotContain("Content not found", result);
    }

    [Fact]
    public async Task LoadAsync_EnglishRemoteDatabases_ReturnsNonEmptyContent()
    {
        var result = await HelpContentLoader.LoadAsync("remote-databases", new CultureInfo("en"));
        Assert.False(string.IsNullOrWhiteSpace(result));
        Assert.DoesNotContain("Content not found", result);
    }

    [Fact]
    public async Task LoadAsync_RemoteDatabasesUnknownCulture_FallsBackToEnglish()
    {
        var result = await HelpContentLoader.LoadAsync("remote-databases", new CultureInfo("fi"));
        Assert.False(string.IsNullOrWhiteSpace(result));
        Assert.DoesNotContain("Content not found", result);
    }

    // Every shipped locale resolves its own translation of every topic, never the English fallback
    [Theory]
    [InlineData("de")]
    [InlineData("es")]
    [InlineData("fr")]
    [InlineData("it")]
    [InlineData("nl")]
    [InlineData("pt-BR")]
    [InlineData("pt-PT")]
    [InlineData("sv")]
    public async Task LoadAsync_ShippedLocale_ResolvesItsOwnVariant(string locale)
    {
        foreach (var topic in new[] { "shortcuts", "glossary", "import-guide", "data-sources", "remote-databases" })
        {
            var english = await HelpContentLoader.LoadAsync(topic, new CultureInfo("en"));
            var localized = await HelpContentLoader.LoadAsync(topic, new CultureInfo(locale));
            Assert.False(string.IsNullOrWhiteSpace(localized));
            Assert.NotEqual(english, localized);
        }
    }

    // The two Portuguese variants ship separate files; the region-specific one must win over the bare language
    [Fact]
    public async Task LoadAsync_PortugueseVariants_AreDistinct()
    {
        var brazilian = await HelpContentLoader.LoadAsync("remote-databases", new CultureInfo("pt-BR"));
        var european = await HelpContentLoader.LoadAsync("remote-databases", new CultureInfo("pt-PT"));
        Assert.NotEqual(brazilian, european);
    }

    // Embedded resource names match loader expectations
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
        Assert.Contains("BookDB.Help.Content.remote-databases.md", names);
        Assert.Contains("BookDB.Help.Content.remote-databases.sv.md", names);
    }
}
