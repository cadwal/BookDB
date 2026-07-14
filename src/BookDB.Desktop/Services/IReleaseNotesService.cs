using System.Globalization;

namespace BookDB.Desktop.Services;

public interface IReleaseNotesService
{
    /// <summary>The running application version as a 3-part string (same source the About window shows).</summary>
    string CurrentVersion { get; }

    /// <summary>
    /// The release notes for <paramref name="version"/>: an embedded per-locale override
    /// (ReleaseNotes/{version}.{lang}.md) when one ships, otherwise the version's section of the embedded
    /// CHANGELOG. Null when neither exists (e.g. a dev build whose version has no released section) —
    /// callers show no prompt then.
    /// </summary>
    string? GetNotes(string version, CultureInfo? culture = null);
}
