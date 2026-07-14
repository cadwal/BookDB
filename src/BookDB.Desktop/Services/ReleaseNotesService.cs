using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace BookDB.Desktop.Services;

public sealed class ReleaseNotesService : IReleaseNotesService
{
    private const string ChangelogResource = "BookDB.Desktop.CHANGELOG.md";

    private readonly Func<string, Stream?> _openResource;

    public ReleaseNotesService()
        : this(static name => typeof(ReleaseNotesService).Assembly.GetManifestResourceStream(name))
    {
    }

    // Test seam: resource resolution is injected so the locale-override probe and the CHANGELOG fallback can
    // be exercised without authoring embedded fixtures.
    public ReleaseNotesService(Func<string, Stream?> openResource) => _openResource = openResource;

    public string CurrentVersion
    {
        get
        {
            var version = typeof(ReleaseNotesService).Assembly.GetName().Version;
            // A version-less assembly yields a version no changelog section names — the caller shows no prompt.
            return version is null ? "0.0.0" : $"{version.Major}.{version.Minor}.{version.Build}";
        }
    }

    public string? GetNotes(string version, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentUICulture;

        // Optional translated override, same probe order as help content (region-specific, bare language).
        var overrideText = ReadResource($"BookDB.Desktop.ReleaseNotes.{version}.{culture.Name}.md")
            ?? ReadResource($"BookDB.Desktop.ReleaseNotes.{version}.{culture.TwoLetterISOLanguageName}.md");
        if (!string.IsNullOrWhiteSpace(overrideText))
            return overrideText;

        var changelog = ReadResource(ChangelogResource);
        return changelog is null ? null : ExtractSection(changelog, version);
    }

    private string? ReadResource(string name)
    {
        using var stream = _openResource(name);
        if (stream is null)
            return null;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    // The body of the "## [version]" section — the lines after the heading up to the next "## " heading.
    // The heading itself is excluded (the viewer titles the window instead). Null when the version has no
    // section, or an empty one.
    private static string? ExtractSection(string changelog, string version)
    {
        var section = new StringBuilder();
        var inSection = false;
        using var reader = new StringReader(changelog);
        while (reader.ReadLine() is { } line)
        {
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                if (inSection)
                    break;
                inSection = line.StartsWith($"## [{version}]", StringComparison.Ordinal);
                continue;
            }
            if (inSection)
                section.AppendLine(line);
        }

        var text = section.ToString().Trim();
        return text.Length == 0 ? null : text;
    }
}
