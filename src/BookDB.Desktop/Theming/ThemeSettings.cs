using System;
using System.Threading;
using System.Threading.Tasks;
using BookDB.Logic.Services;

namespace BookDB.Desktop.Theming;

/// <summary>
/// Reads and writes the persisted colour flavour. It lives in the same key/value settings table as
/// Language and LogLevel, stored as the flavour's name under <see cref="Key"/>. Missing or
/// unrecognised values fall back to <see cref="ThemeFlavour.Default"/>, so the app always has a valid
/// flavour and a corrupt value never throws.
/// </summary>
public static class ThemeSettings
{
    /// <summary>Settings-table key under which the chosen flavour is stored.</summary>
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

    /// <summary>The string written to the settings table for a flavour.</summary>
    public static string ToStorageValue(ThemeFlavour flavour) => flavour.ToString();

    /// <summary>Loads the persisted flavour via the settings service.</summary>
    public static async Task<ThemeFlavour> LoadAsync(ISettingsService settings, CancellationToken ct = default)
        => Parse(await settings.GetAsync(Key, ct));

    /// <summary>Persists the chosen flavour via the settings service.</summary>
    public static Task SaveAsync(ISettingsService settings, ThemeFlavour flavour, CancellationToken ct = default)
        => settings.SetAsync(Key, ToStorageValue(flavour), ct);
}
