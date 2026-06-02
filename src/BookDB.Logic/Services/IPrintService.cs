using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

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

public sealed record PrintParameters(
    string OutputPath,
    IReadOnlySet<int>? CollectionIds,
    IReadOnlyList<int>? SearchBookIds,
    Dictionary<string, HashSet<int>>? FacetFilters,
    string? SortColumn,
    bool SortAscending,
    PrintPreset Preset);

public interface IPrintService
{
    IReadOnlyList<string> AllColumnNames { get; }
    IReadOnlyList<string> DefaultColumnNames { get; }
    Task GenerateAsync(PrintParameters parameters, CancellationToken ct = default, IProgress<string>? progress = null);
    void InitializeLicense();
}
