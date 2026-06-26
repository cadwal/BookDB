using System;

namespace BookDB.Desktop.Theming;

/// <summary>
/// Maps the colour flavour to and from its stored string form. The chosen flavour is persisted in
/// config.json (under <see cref="Key"/>) and applied once at startup. Missing or unrecognised values
/// fall back to <see cref="ThemeFlavour.Default"/>, so the app always has a valid flavour and a corrupt
/// value never throws.
/// </summary>
public static class ThemeSettings
{
    /// <summary>config.json field name under which the chosen flavour is stored.</summary>
    public const string Key = "UiTheme";

    /// <summary>
    /// Maps a stored value back to a flavour, defaulting when null/blank/unrecognised. Only the
    /// flavour names are accepted (case-insensitively); a stray ordinal like "1" is not a flavour.
    /// </summary>
    public static ThemeFlavour Parse(string? stored)
    {
        var trimmed = stored?.Trim();
        foreach (var flavour in Enum.GetValues<ThemeFlavour>())
            if (string.Equals(flavour.ToString(), trimmed, StringComparison.OrdinalIgnoreCase))
                return flavour;
        return ThemeFlavour.Default;
    }

    /// <summary>The string written to config.json for a flavour.</summary>
    public static string ToStorageValue(ThemeFlavour flavour) => flavour.ToString();
}
