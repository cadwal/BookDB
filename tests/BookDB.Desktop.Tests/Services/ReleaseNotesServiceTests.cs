using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using BookDB.Desktop.Services;
using Xunit;

namespace BookDB.Desktop.Tests.Services;

public sealed class ReleaseNotesServiceTests
{
    // The real embedded CHANGELOG must yield notes for its newest released version — the version the release
    // pipeline would prompt for. The expected version is read from the source-tree CHANGELOG so the test
    // tracks every release without editing.
    [Fact]
    public void EmbeddedChangelog_YieldsNotesForItsHeadVersion()
    {
        var changelog = File.ReadAllText(Path.Combine(GetRepoRoot(), "CHANGELOG.md"));
        var head = Regex.Match(changelog, @"^## \[(\d+\.\d+\.\d+)\]", RegexOptions.Multiline);
        Assert.True(head.Success, "CHANGELOG.md has no released ## [x.y.z] section.");

        var notes = new ReleaseNotesService().GetNotes(head.Groups[1].Value, CultureInfo.InvariantCulture);

        Assert.False(string.IsNullOrWhiteSpace(notes));
        Assert.DoesNotContain("## [", notes); // the section body only — no heading, no bleed into older releases
    }

    [Fact]
    public void UnknownVersion_ReturnsNull()
        => Assert.Null(new ReleaseNotesService().GetNotes("99.99.99", CultureInfo.InvariantCulture));

    [Fact]
    public void CurrentVersion_IsThreePart()
        => Assert.Matches(@"^\d+\.\d+\.\d+$", new ReleaseNotesService().CurrentVersion);

    [Fact]
    public void LocaleOverride_WinsOverTheChangelogSection()
    {
        var sut = ServiceOver(new Dictionary<string, string>
        {
            ["BookDB.Desktop.ReleaseNotes.2.3.0.sv.md"] = "Svenska nyheter",
            ["BookDB.Desktop.CHANGELOG.md"] = "## [2.3.0]\n\nEnglish notes\n",
        });

        Assert.Equal("Svenska nyheter", sut.GetNotes("2.3.0", new CultureInfo("sv")));
    }

    [Fact]
    public void RegionSpecificOverride_IsProbedBeforeTheBareLanguage()
    {
        var sut = ServiceOver(new Dictionary<string, string>
        {
            ["BookDB.Desktop.ReleaseNotes.2.3.0.pt-BR.md"] = "Notas brasileiras",
            ["BookDB.Desktop.ReleaseNotes.2.3.0.pt.md"] = "Notas portuguesas",
        });

        Assert.Equal("Notas brasileiras", sut.GetNotes("2.3.0", new CultureInfo("pt-BR")));
        Assert.Equal("Notas portuguesas", sut.GetNotes("2.3.0", new CultureInfo("pt-PT")));
    }

    [Fact]
    public void MissingOverride_FallsBackToTheChangelogSection_EndingAtTheNextRelease()
    {
        var sut = ServiceOver(new Dictionary<string, string>
        {
            ["BookDB.Desktop.CHANGELOG.md"] =
                "# Changelog\n\n## [Unreleased]\n\n## [2.3.0] - 2026-07-20\n\n### Added\n- New thing\n\n## [2.2.0] - 2026-07-07\n\n- Old thing\n",
        });

        var notes = sut.GetNotes("2.3.0", new CultureInfo("sv"));

        Assert.Equal("### Added" + Environment.NewLine + "- New thing", notes);
    }

    // A heading that doesn't match the "## [x.y.z]" shape must never throw — it just yields no notes.
    [Theory]
    [InlineData("##[2.3.0]\n- no space after ##\n")]
    [InlineData("## 2.3.0\n- no brackets\n")]
    [InlineData("## [2.3.0-beta]\n- suffixed version is a different version\n")]
    [InlineData("")]
    public void MalformedOrForeignHeading_YieldsNull(string changelog)
    {
        var sut = ServiceOver(new Dictionary<string, string> { ["BookDB.Desktop.CHANGELOG.md"] = changelog });

        Assert.Null(sut.GetNotes("2.3.0", CultureInfo.InvariantCulture));
    }

    private static ReleaseNotesService ServiceOver(Dictionary<string, string> resources)
        => new(name => resources.TryGetValue(name, out var text)
            ? new MemoryStream(Encoding.UTF8.GetBytes(text))
            : null);

    private static string GetRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "BookDB.slnx")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Could not locate repo root from " + AppContext.BaseDirectory);
    }
}
