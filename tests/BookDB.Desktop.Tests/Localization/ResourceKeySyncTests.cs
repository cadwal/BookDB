using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace BookDB.Desktop.Tests.Localization;

/// <summary>
/// Every locale resx must carry the same key set as the neutral (English) file: a key added without its
/// translations silently falls back to English for those users, and a key removed from neutral but left in a
/// locale is orphaned dead weight. Locale-invariant values (glyphs) may live in neutral only, listed below.
/// </summary>
public class ResourceKeySyncTests
{
    private static readonly string[] Locales = ["de", "es", "fr", "it", "nl", "pt-BR", "pt-PT", "sv"];

    /// <summary>Values identical in every language; the neutral entry serves all locales via fallback.</summary>
    private static readonly HashSet<string> NeutralOnlyKeys = ["BookList_Thumbnail_Badge_Text"];

    [Theory]
    [InlineData("de")]
    [InlineData("es")]
    [InlineData("fr")]
    [InlineData("it")]
    [InlineData("nl")]
    [InlineData("pt-BR")]
    [InlineData("pt-PT")]
    [InlineData("sv")]
    public void LocaleResx_CarriesTheNeutralKeySet(string locale)
    {
        var neutral = ReadKeys(ResxPath(null));
        var localized = ReadKeys(ResxPath(locale));

        var missing = neutral.Except(localized).Except(NeutralOnlyKeys).OrderBy(k => k).ToList();
        var orphaned = localized.Except(neutral).OrderBy(k => k).ToList();

        Assert.True(missing.Count == 0,
            $"Resources.{locale}.resx is missing translations for: {string.Join(", ", missing)}");
        Assert.True(orphaned.Count == 0,
            $"Resources.{locale}.resx has keys absent from the neutral file: {string.Join(", ", orphaned)}");
    }

    private static HashSet<string> ReadKeys(string path)
    {
        Assert.True(File.Exists(path), $"resx not found at: {path}");
        return Regex.Matches(File.ReadAllText(path), "<data name=\"([^\"]+)\"")
            .Select(m => m.Groups[1].Value)
            .ToHashSet();
    }

    private static string ResxPath(string? locale)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "BookDB.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        var name = locale is null ? "Resources.resx" : $"Resources.{locale}.resx";
        return Path.Combine(dir.FullName, "src", "BookDB.Desktop", "Localization", name);
    }
}
