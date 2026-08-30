using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using BookDB.Mobile.Localization;
using Xunit;

namespace BookDB.Mobile.Tests;

/// <summary>
/// The phone's strings hold to the same rule as the desktop's: every locale resx carries the neutral key
/// set, and the hand-maintained typed accessors name keys that actually exist. A key added without its
/// translations falls back to English unnoticed; an accessor with no key behind it returns its own name.
/// </summary>
public class ResourceKeySyncTests
{
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

        var missing = neutral.Except(localized).OrderBy(k => k).ToList();
        var orphaned = localized.Except(neutral).OrderBy(k => k).ToList();

        Assert.True(missing.Count == 0,
            $"Resources.{locale}.resx is missing translations for: {string.Join(", ", missing)}");
        Assert.True(orphaned.Count == 0,
            $"Resources.{locale}.resx has keys absent from the neutral file: {string.Join(", ", orphaned)}");
    }

    [Fact]
    public void EveryTypedAccessorNamesAKeyThatExists()
    {
        var keys = ReadKeys(ResxPath(null));

        var dangling = typeof(Resources)
            .GetProperties(BindingFlags.Public | BindingFlags.Static)
            .Select(p => p.Name)
            .Where(name => !keys.Contains(name))
            .OrderBy(name => name)
            .ToList();

        Assert.True(dangling.Count == 0,
            $"Resources.Designer.cs exposes keys the resx does not define: {string.Join(", ", dangling)}");
    }

    [Fact]
    public void EveryStringHasATypedAccessor()
    {
        var accessors = typeof(Resources)
            .GetProperties(BindingFlags.Public | BindingFlags.Static)
            .Select(p => p.Name)
            .ToHashSet();

        var unreachable = ReadKeys(ResxPath(null)).Except(accessors).OrderBy(k => k).ToList();

        Assert.True(unreachable.Count == 0,
            $"Resources.resx defines keys nothing can reach: {string.Join(", ", unreachable)}");
    }

    /// <summary>Parsed as XML rather than matched with a pattern: every resx carries a commented-out schema
    /// example with `data` elements in it, and only a parser knows they are a comment.</summary>
    private static HashSet<string> ReadKeys(string path)
    {
        Assert.True(File.Exists(path), $"resx not found at: {path}");
        return XDocument.Load(path).Root!.Elements("data")
            .Select(e => e.Attribute("name")?.Value)
            .Where(name => name is not null)
            .ToHashSet()!;
    }

    private static string ResxPath(string? locale)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "BookDB.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        var name = locale is null ? "Resources.resx" : $"Resources.{locale}.resx";
        return Path.Combine(dir.FullName, "src", "BookDB.Mobile", "Localization", name);
    }
}
