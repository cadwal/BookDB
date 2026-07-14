using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BookDB.Models;

namespace BookDB.Logic.Services;

public enum PageOrientation
{
    Portrait,
    Landscape
}

public sealed record PrintPreset(
    string Name,
    IReadOnlyList<string> Columns,
    PageOrientation Orientation,
    float FontSize,
    float MarginHorizontalMm,
    float MarginVerticalMm,
    string HeaderText,
    string FooterText)
{
    public const string StandardPresetName = "Standard";

    /// <summary>
    /// Creates the default "Standard" preset.
    /// Columns use CsvExportService key names: "Authors" (not "Author"), "PubDate" (not "Year").
    /// </summary>
    public static PrintPreset CreateDefault(string defaultTitle = "Book List") => new(
        Name: StandardPresetName,
        Columns: new[] { "Title", "Authors", "Series", "PubDate", "Format", "Location" },
        Orientation: PageOrientation.Portrait,
        FontSize: 10,
        MarginHorizontalMm: 15,
        MarginVerticalMm: 20,
        HeaderText: defaultTitle,
        FooterText: string.Empty);
}

/// <summary>
/// <paramref name="ColumnHeaderLabels"/> and <paramref name="PageNumberFormat"/> carry the localized header
/// labels (column key → label) and footer page-number format ("Page {0} of {1}" shape) into the PDF, so the
/// Logic layer renders localized content without a localization dependency; an absent label falls back to the
/// column key, an absent format to English.
/// </summary>
public sealed record PrintParameters(
    string OutputPath,
    IReadOnlySet<int>? CollectionIds,
    IReadOnlyList<int>? SearchBookIds,
    Dictionary<string, HashSet<int>>? FacetFilters,
    string? SortColumn,
    bool SortAscending,
    PrintPreset Preset,
    IReadOnlyDictionary<string, string>? ColumnHeaderLabels = null,
    string? PageNumberFormat = null);

public interface IPrintService
{
    IReadOnlyList<string> AllColumnNames { get; }
    IReadOnlyList<string> DefaultColumnNames { get; }
    Task GenerateAsync(PrintParameters parameters, CancellationToken ct = default, IProgress<ProgressUpdate<PrintProgressStep>>? progress = null);
    void InitializeLicense();
}
