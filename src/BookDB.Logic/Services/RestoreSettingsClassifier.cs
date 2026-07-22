using System;
using System.Linq;

namespace BookDB.Logic.Services;

/// <summary>How a Settings-table row should be treated when restoring a CSV archive.</summary>
public enum SettingsRestoreClass
{
    /// <summary>A user preference carried across the restore (overwrites the live value).</summary>
    Apply,

    /// <summary>Machine-specific UI state (window geometry, column layout, local paths) — the live value is kept.</summary>
    SkipMachineSpecific,

    /// <summary>Not classified — a guard test fails the build so a new key is consciously categorised.</summary>
    Unknown,
}

/// <summary>
/// Classifies a Settings-table key for restore. Preference rows are applied; machine-specific
/// rows (window geometry, column/splitter layout, local file paths) are skipped so a restore into a different
/// machine context does not import the source machine's layout or paths. Every key the app writes must be listed;
/// <c>RestoreSettingsClassifierTests</c> scans the source and fails the build on an unclassified key.
/// Language/UiTheme/LogLevel are not here — since the bootstrap split they live in config.json, not this table.
/// </summary>
public static class RestoreSettingsClassifier
{
    private static readonly string[] SkipPrefixes =
        ["Window", "DetailPanel", "ColumnVisible.", "BookList_"];

    private static readonly string[] SkipExact =
        ["FilterPanelWidth", "LastBackupFolder", "Import.ReaderwareToolPath", "UncategorizedFilterSeeded"];

    private static readonly string[] ApplyPrefixes =
        ["AutoBackup.", "LookupEnabled.", "LookupApiKey."];

    private static readonly string[] ApplyExact =
        ["DefaultCollectionId", "LastSelectedCollectionIds", "Import.OverwritePolicy", "PrintPresets",
         "AuthorFacetLabel", "PrimaryDisplayRole"];

    public static SettingsRestoreClass Classify(string key)
    {
        if (SkipExact.Contains(key) || SkipPrefixes.Any(p => key.StartsWith(p, StringComparison.Ordinal)))
            return SettingsRestoreClass.SkipMachineSpecific;
        if (ApplyExact.Contains(key) || ApplyPrefixes.Any(p => key.StartsWith(p, StringComparison.Ordinal)))
            return SettingsRestoreClass.Apply;
        return SettingsRestoreClass.Unknown;
    }

    /// <summary>True when the row should overwrite the live value during a restore.</summary>
    public static bool ShouldApply(string key) => Classify(key) == SettingsRestoreClass.Apply;
}
