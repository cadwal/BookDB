using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using BookDB.Logic.Services;
using Xunit;

namespace BookDB.Logic.Tests;

public sealed class RestoreSettingsClassifierTests
{
    [Theory]
    // Machine-specific — kept on the live machine, never imported.
    [InlineData("WindowWidth", SettingsRestoreClass.SkipMachineSpecific)]
    [InlineData("WindowHeight", SettingsRestoreClass.SkipMachineSpecific)]
    [InlineData("WindowLeft", SettingsRestoreClass.SkipMachineSpecific)]
    [InlineData("WindowTop", SettingsRestoreClass.SkipMachineSpecific)]
    [InlineData("FilterPanelWidth", SettingsRestoreClass.SkipMachineSpecific)]
    [InlineData("DetailPanelWidth", SettingsRestoreClass.SkipMachineSpecific)]
    [InlineData("DetailPanelVisible", SettingsRestoreClass.SkipMachineSpecific)]
    [InlineData("ColumnVisible.Author", SettingsRestoreClass.SkipMachineSpecific)]
    [InlineData("BookList_ColumnState", SettingsRestoreClass.SkipMachineSpecific)]
    [InlineData("BookList_SortState", SettingsRestoreClass.SkipMachineSpecific)]
    [InlineData("BookList_ThumbnailVisible", SettingsRestoreClass.SkipMachineSpecific)]
    [InlineData("LastBackupFolder", SettingsRestoreClass.SkipMachineSpecific)]
    [InlineData("Import.ReaderwareToolPath", SettingsRestoreClass.SkipMachineSpecific)]
    // User preferences — carried across the restore.
    [InlineData("AutoBackup.Enabled", SettingsRestoreClass.Apply)]
    [InlineData("AutoBackup.Format", SettingsRestoreClass.Apply)]
    [InlineData("DefaultCollectionId", SettingsRestoreClass.Apply)]
    [InlineData("LastSelectedCollectionIds", SettingsRestoreClass.Apply)]
    [InlineData("Import.OverwritePolicy", SettingsRestoreClass.Apply)]
    [InlineData("LookupEnabled.GoogleBooks", SettingsRestoreClass.Apply)]
    [InlineData("PrintPresets", SettingsRestoreClass.Apply)]
    [InlineData("AuthorFacetLabel", SettingsRestoreClass.Apply)]
    public void Classify_MatchesExpected(string key, SettingsRestoreClass expected)
        => Assert.Equal(expected, RestoreSettingsClassifier.Classify(key));

    [Fact]
    public void Classify_UnknownKey_IsUnknown()
        => Assert.Equal(SettingsRestoreClass.Unknown, RestoreSettingsClassifier.Classify("SomethingBrandNew"));

    /// <summary>
    /// Completeness guard: every Settings key the source writes via the settings service must be classified.
    /// A new SetAsync/GetAsync("key") that nobody categorised fails the build here, forcing a conscious choice.
    /// </summary>
    [Fact]
    public void EverySettingsKeyInSource_IsClassified()
    {
        var srcDir = FindSourceDir();
        var keyPattern = new Regex(@"\b(?:Set|Get)Async\(""([^""]+)""", RegexOptions.Compiled);

        var keys = Directory.EnumerateFiles(srcDir, "*.cs", SearchOption.AllDirectories)
            .SelectMany(file => keyPattern.Matches(File.ReadAllText(file)).Select(m => m.Groups[1].Value))
            .Distinct()
            .ToList();

        Assert.NotEmpty(keys); // the scan must actually find keys, or the guard is vacuous

        var unclassified = keys.Where(k => RestoreSettingsClassifier.Classify(k) == SettingsRestoreClass.Unknown).ToList();
        Assert.True(unclassified.Count == 0,
            "Unclassified Settings keys (add to RestoreSettingsClassifier as Apply or SkipMachineSpecific): "
            + string.Join(", ", unclassified));
    }

    private static string FindSourceDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var src = Path.Combine(dir.FullName, "src");
            if (Directory.Exists(Path.Combine(src, "BookDB.Desktop")))
                return src;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Could not locate the repository's src directory from the test base path.");
    }
}
