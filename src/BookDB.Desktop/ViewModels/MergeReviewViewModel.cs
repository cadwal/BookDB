using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using BookDB.Desktop.Localization;
using BookDB.Desktop.Messages;
using BookDB.Desktop.Services;
using BookDB.Logic.Services;
using BookDB.Models.Metadata;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Serilog;

namespace BookDB.Desktop.ViewModels;

/// <summary>
/// Represents a single cover option in the merge review — one per source that returned a cover URL.
/// </summary>
public sealed class CoverOption : ObservableObject
{
    public string SourceName { get; init; } = string.Empty;
    public byte[]? ImageData { get; init; }
    public string? RemoteUrl { get; init; }

    public Bitmap? ThumbnailBitmap
    {
        get;
        set => SetProperty(ref field, value);
    }

    public Bitmap? FullBitmap
    {
        get;
        set => SetProperty(ref field, value);
    }

    /// <summary>Human-readable dimensions and file size, e.g. "1024×768 · 42 KB".</summary>
    public string? CoverInfo => FullBitmap is not null
                    ? $"{FullBitmap.PixelSize.Width}\u00d7{FullBitmap.PixelSize.Height} \u00b7 {(ImageData?.Length ?? 0) / 1024} KB"
                    : null;

    public bool IsSelected
    {
        get;
        set => SetProperty(ref field, value);
    }
}

/// <summary>
/// A selectable source value within a FieldDiffRow.
/// </summary>
public sealed class SourceValueOption : ObservableObject
{
    public string SourceName { get; init; } = string.Empty;
    public string? Value { get; init; }

    public bool IsSelected
    {
        get;
        set => SetProperty(ref field, value);
    }
}

/// <summary>
/// Wraps a FieldDiff with observable SourceValueOption collection for UI binding.
/// SourceValues is always aligned to AllColumnNames — each column has exactly one entry
/// (with null Value when that source has no data for this field).
/// </summary>
public sealed class FieldDiffRow
{
    /// <summary>Raw internal key from the Logic layer (e.g. "PubDate"). Used for data lookups in BuildMergedMetadata.</summary>
    public string RawKey { get; init; } = string.Empty;
    /// <summary>Localized display name shown in the UI (e.g. "Utgivningsdatum" in Swedish).</summary>
    public string FieldName { get; init; } = string.Empty;
    public string? CurrentValue { get; init; }
    public List<SourceValueOption> SourceValues { get; init; } = [];

    /// <summary>Returns the SourceValueOption for the given column name, or null if absent.</summary>
    public SourceValueOption? GetValueForColumn(string columnName)
        => SourceValues.Find(sv => sv.SourceName == columnName);
}

/// <summary>
/// Represents one column slot in the cover row — either a real CoverOption or an empty placeholder.
/// Aligned to AllColumnNames so the cover UniformGrid stays in sync with field data columns.
/// </summary>
public sealed class CoverCell
{
    public string ColumnName { get; init; } = string.Empty;
    public CoverOption? Cover { get; init; }
    public bool HasCover => Cover is not null;
}

/// <summary>
/// ViewModel for the Merge Review dialog. Shows field-by-field differences between metadata
/// sources and allows the user to select preferred values before saving.
/// </summary>
public sealed partial class MergeReviewViewModel : ObservableObject
{
    private readonly IBookMetadataService _bookService;
    private readonly IWindowService? _windowService;
    private readonly IMessenger _messenger;
    private readonly int? _existingBookId;
    private readonly int? _collectionId;
    private readonly Action<bool?> _closeDialog;
    private readonly IReadOnlyList<BookMetadata> _sources;

    private static readonly Dictionary<string, string> _fieldDisplayNames = new()
    {
        { "Title",        Resources.MergeReview_Field_Title },
        { "Subtitle",     Resources.MergeReview_Field_Subtitle },
        { "Authors",      Resources.MergeReview_Field_Authors },
        { "Publisher",    Resources.MergeReview_Field_Publisher },
        { "PubDate",      Resources.MergeReview_Field_PubDate },
        { "Language",     Resources.MergeReview_Field_Language },
        { "Pages",        Resources.MergeReview_Field_Pages },
        { "Series",       Resources.MergeReview_Field_Series },
        { "SeriesNumber", Resources.MergeReview_Field_SeriesNumber },
        { "Description",  Resources.MergeReview_Field_Description },
    };

    [ObservableProperty]
    private string _windowTitle = Resources.MergeReview_Title_New;

    public ObservableCollection<FieldDiffRow> FieldDiffs { get; } = [];
    public ObservableCollection<CoverOption> CoverOptions { get; } = [];
    public IReadOnlyList<string> SourceNames { get; }
    public IReadOnlyList<string> AllColumnNames { get; }
    public IReadOnlyList<CoverCell> CoverCells { get; private set; } = [];
    public bool IsNewBook { get; }

    /// <summary>True when there are no field conflicts to resolve (used to show the "no conflicts" text).</summary>
    public bool HasNoConflicts => FieldDiffs.Count == 0;

    public MergeReviewViewModel(
        IReadOnlyList<BookMetadata> sources,
        BookMetadata? currentBook,
        IReadOnlyList<CoverOption> coverOptions,
        IBookMetadataService bookMetadataService,
        IMessenger messenger,
        int? existingBookId,
        int? collectionId,
        Action<bool?> closeDialog,
        IWindowService? windowService = null)
    {
        _sources = sources;
        _bookService = bookMetadataService;
        _windowService = windowService;
        _messenger = messenger;
        _existingBookId = existingBookId;
        _collectionId = collectionId;
        _closeDialog = closeDialog;
        IsNewBook = existingBookId is null;

        SourceNames = sources.Select(s => s.SourceName).Distinct().ToList();

        // AllColumnNames: "Current" first (existing books only), then each API source name
        var columns = new List<string>();
        if (!IsNewBook) columns.Add("Current");
        columns.AddRange(SourceNames);
        AllColumnNames = columns;

        // Build title from first source that has one
        var firstTitle = sources.FirstOrDefault(s => s.Title is not null)?.Title;
        WindowTitle = IsNewBook
            ? Resources.MergeReview_Title_New
            : string.Format(Resources.MergeReview_Title_Recatalog, firstTitle ?? string.Empty);

        // Build FieldDiffRows from computed diffs
        var diffs = FieldDiffComputer.ComputeDiffs(sources, currentBook);
        foreach (var diff in diffs)
        {
            var sourceValues = diff.SourceValues
                .Select(sv => new SourceValueOption
                {
                    SourceName = sv.SourceName,
                    Value = sv.Value,
                    IsSelected = false
                })
                .ToList();

            // Insert Current as a selectable option at index 0 for existing books
            if (!IsNewBook && diff.CurrentValue is not null)
            {
                sourceValues.Insert(0, new SourceValueOption
                {
                    SourceName = "Current",
                    Value = diff.CurrentValue,
                    IsSelected = true  // Default to keeping current value
                });
            }
            else if (sourceValues.Count > 0)
            {
                sourceValues[0].IsSelected = true;
            }

            // Pad SourceValues to exactly AllColumnNames.Count entries so columns align
            var aligned = new List<SourceValueOption>();
            foreach (var colName in AllColumnNames)
            {
                var existing = sourceValues.Find(sv => sv.SourceName == colName);
                aligned.Add(existing ?? new SourceValueOption { SourceName = colName, Value = null, IsSelected = false });
            }

            var row = new FieldDiffRow
            {
                RawKey = diff.FieldName,
                FieldName = _fieldDisplayNames.TryGetValue(diff.FieldName, out var displayName)
                    ? displayName
                    : diff.FieldName,   // fallback for unknown fields
                CurrentValue = diff.CurrentValue,
                SourceValues = aligned
            };

            FieldDiffs.Add(row);
        }

        // Cover options
        foreach (var co in coverOptions)
            CoverOptions.Add(co);
        if (CoverOptions.Count > 0)
            CoverOptions[0].IsSelected = true;

        // CoverCells: one slot per column, aligned to AllColumnNames (null Cover = placeholder)
        CoverCells = [.. AllColumnNames.Select(name => new CoverCell
        {
            ColumnName = name,
            Cover = CoverOptions.FirstOrDefault(co => co.SourceName == name)
        })];
    }

    /// <summary>
    /// Selects a specific source value within a field diff row, clearing the others.
    /// </summary>
    public void SelectSourceValue(FieldDiffRow row, SourceValueOption option)
    {
        foreach (var sv in row.SourceValues)
            sv.IsSelected = false;
        option.IsSelected = true;
    }

    [RelayCommand]
    private void AcceptAllFromSource(string sourceName)
    {
        foreach (var row in FieldDiffs)
        {
            foreach (var sv in row.SourceValues)
                sv.IsSelected = false;

            var match = row.SourceValues.Find(sv => sv.SourceName == sourceName);
            if (match is not null)
                match.IsSelected = true;
            else if (row.SourceValues.Count > 0)
                row.SourceValues[0].IsSelected = true;
        }
    }

    [RelayCommand]
    private void AcceptAllFromCurrent()
    {
        AcceptAllFromSource("Current");
    }

    /// <summary>Returns the CoverOption for a given column name, or null if none exists.</summary>
    public CoverOption? GetCoverForColumn(string columnName)
        => CoverOptions.FirstOrDefault(co => co.SourceName == columnName);

    /// <summary>The cover option for the "Current" column, if present.</summary>
    public CoverOption? CurrentCoverOption => CoverOptions.FirstOrDefault(co => co.SourceName == "Current");

    [RelayCommand]
    private void SelectCover(CoverOption option)
    {
        foreach (var co in CoverOptions)
            co.IsSelected = false;
        option.IsSelected = true;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        try
        {
            var merged = BuildMergedMetadata();
            var selectedCover = CoverOptions.FirstOrDefault(co => co.IsSelected);
            var coverPath = selectedCover?.ImageData;

            if (IsNewBook)
            {
                // Check for duplicate ISBN before creating a new book
                if (merged.Isbn is not null && _windowService is not null)
                {
                    var existing = await _bookService.FindBookByIsbnAsync(merged.Isbn);
                    if (existing is not null)
                    {
                        var choice = await _windowService.ShowDuplicateIsbnDialogAsync(
                            merged.Isbn,
                            existing.Title ?? merged.Isbn);

                        if (choice == DuplicateIsbnResult.Cancel)
                            return;

                        if (choice == DuplicateIsbnResult.UpdateExisting)
                        {
                            await _bookService.UpdateBookFromMetadataAsync(existing.BookId, merged, coverPath);
                            _messenger.Send(new BookSavedMessage(existing.BookId));
                            _closeDialog(true);
                            return;
                        }
                        // AddAsNew — fall through to normal add
                    }
                }

                await _bookService.AddBookFromMetadataAsync(merged, coverPath, _collectionId);
            }
            else
            {
                await _bookService.UpdateBookFromMetadataAsync(_existingBookId!.Value, merged, coverPath);
            }

            // Notify BookListViewModel to refresh
            _messenger.Send(new BookSavedMessage(0));
            _closeDialog(true);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to save book from merge review");
            // Do NOT leave the dialog open on failure — close it with false so the
            // caller can mark the item as Skipped and move on to the next review item.
            _closeDialog(false);
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        _closeDialog(false);
    }

    /// <summary>
    /// Builds a merged BookMetadata from the selected values in each FieldDiffRow.
    /// For fields not in the diff list (all sources agreed), takes the value from the first source.
    /// </summary>
    public BookMetadata BuildMergedMetadata()
    {
        // Start with values from the first source
        var first = _sources.Count > 0 ? _sources[0] : null;

        string? Get(string fieldName)
        {
            var row = FieldDiffs.FirstOrDefault(r => r.RawKey == fieldName);
            if (row is not null)
            {
                var selected = row.SourceValues.Find(sv => sv.IsSelected);
                return selected?.Value;
            }
            // Not a diff field — all sources agreed; take first source value
            return first is not null ? GetFieldFromMetadata(first, fieldName) : null;
        }

        IReadOnlyList<string> GetAuthors()
        {
            var row = FieldDiffs.FirstOrDefault(r => r.RawKey == "Authors");
            if (row is not null)
            {
                var selected = row.SourceValues.Find(sv => sv.IsSelected);
                if (selected?.Value is not null)
                    return selected.Value.Split("; ", StringSplitOptions.RemoveEmptyEntries);
            }
            return first?.Authors ?? new List<string>();
        }

        return new BookMetadata(
            Title: Get("Title"),
            Subtitle: Get("Subtitle"),
            Authors: GetAuthors(),
            Publisher: Get("Publisher"),
            PubDate: Get("PubDate"),
            Language: Get("Language"),
            Isbn: first?.Isbn,
            Pages: Get("Pages") is { } pStr && int.TryParse(pStr, out var p) ? p : first?.Pages,
            Description: Get("Description"),
            CoverImageUrl: first?.CoverImageUrl,
            Series: Get("Series"),
            SeriesNumber: Get("SeriesNumber"),
            SourceName: first?.SourceName ?? string.Empty);
    }

    private static string? GetFieldFromMetadata(BookMetadata m, string fieldName) =>
        fieldName switch
        {
            "Title" => m.Title,
            "Subtitle" => m.Subtitle,
            "Publisher" => m.Publisher,
            "PubDate" => m.PubDate,
            "Language" => m.Language,
            "Pages" => m.Pages?.ToString(),
            "Description" => m.Description,
            "Series" => m.Series,
            "SeriesNumber" => m.SeriesNumber,
            _ => null
        };
}
