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
using BookDB.Models.Entities;
using BookDB.Models.Metadata;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Serilog;

namespace BookDB.Desktop.ViewModels;

/// <summary>
/// Represents a single cover option in the merge review — one per source that returned a cover URL.
/// A slot may open as a loading placeholder (IsLoading, no data) and be filled after the dialog
/// is already visible; every display property notifies so the UI absorbs the late arrival.
/// </summary>
public sealed class CoverOption : ObservableObject
{
    public string SourceName { get; init; } = string.Empty;
    public string? RemoteUrl { get; init; }

    public byte[]? ImageData
    {
        get;
        set { if (SetProperty(ref field, value)) OnPropertyChanged(nameof(CoverInfo)); }
    }

    /// <summary>True while this slot's cover is still downloading.</summary>
    public bool IsLoading
    {
        get;
        set => SetProperty(ref field, value);
    }

    public Bitmap? ThumbnailBitmap
    {
        get;
        set => SetProperty(ref field, value);
    }

    public Bitmap? FullBitmap
    {
        get;
        set { if (SetProperty(ref field, value)) OnPropertyChanged(nameof(CoverInfo)); }
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
    private readonly BookMetadata? _currentBook;
    private readonly IBookService? _peopleSource;
    private readonly ILookupService? _collectionSource;
    private readonly FieldDiffRow? _authorsDiffRow;

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

    /// <summary>Title and ISBN of the book under review, shown in an always-visible header so the book stays
    /// recognizable even when every source agrees and there are no diff rows to display.</summary>
    public string IdentityTitle { get; }
    public string? IdentityIsbn { get; }
    public bool HasIdentityIsbn => !string.IsNullOrWhiteSpace(IdentityIsbn);
    public string IdentityIsbnDisplay => string.Format(Resources.MergeReview_Identity_Isbn, IdentityIsbn);

    /// <summary>Localized warning naming sources that were rate-limited (429) and therefore missing from this
    /// review, or null when none were. Bound to a banner so the gap is visible instead of silent.</summary>
    public string? RateLimitedNote { get; }
    public bool HasRateLimitedNote => RateLimitedNote is not null;

    /// <summary>Localized error notice naming sources that errored (non-429) and are therefore missing
    /// from this review, or null when none. A recoverable gap — styled as an error, not a normal state.</summary>
    public string? ErroredNote { get; }
    public bool HasErroredNote => ErroredNote is not null;

    /// <summary>Localized informational note naming sources that were queried but had no record for this
    /// ISBN, or null when none. A normal outcome — styled neutrally, not as a warning.</summary>
    public string? NoResultNote { get; }
    public bool HasNoResultNote => NoResultNote is not null;

    /// <summary>True when there are no field conflicts to resolve (used to show the "no conflicts" text).</summary>
    public bool HasNoConflicts => FieldDiffs.Count == 0;

    /// <summary>Shared type-ahead provider for the author rows; snapshot loads in <see cref="InitializeAsync"/>.</summary>
    public PersonSuggestionProvider PersonSuggestions { get; } = new();

    /// <summary>
    /// Editable author rows seeded from the picked Authors column. Saved contributors come from
    /// these rows, not the raw pick, so the user can fix spelling to reuse an existing person,
    /// drop an author, or add one before saving.
    /// </summary>
    public ObservableCollection<PersonSuggestionRowViewModel> AuthorRows { get; } = [];

    /// <summary>
    /// Informational note above the author editor: reassures the user when every source that
    /// returned authors agrees, or flags that only one source supplied them. Null (no note) when
    /// sources disagree — the grid's Authors conflict row already carries that signal.
    /// </summary>
    public string? AuthorsAgreementNote { get; }
    public bool HasAuthorsAgreementNote => AuthorsAgreementNote is not null;

    /// <summary>Collection choices for new books — real collections only; every new book files into one.</summary>
    public ObservableCollection<Collection> Collections { get; } = [];

    [ObservableProperty]
    private Collection? _selectedCollection;

    public MergeReviewViewModel(
        IReadOnlyList<BookMetadata> sources,
        BookMetadata? currentBook,
        IReadOnlyList<CoverOption> coverOptions,
        IBookMetadataService bookMetadataService,
        IMessenger messenger,
        int? existingBookId,
        int? collectionId,
        Action<bool?> closeDialog,
        IWindowService? windowService = null,
        IBookService? bookService = null,
        ILookupService? lookupService = null,
        IReadOnlyList<string>? rateLimitedSources = null,
        IReadOnlyList<string>? noResultSources = null,
        IReadOnlyList<string>? erroredSources = null)
    {
        _sources = sources;
        _bookService = bookMetadataService;
        _windowService = windowService;
        _messenger = messenger;
        _existingBookId = existingBookId;
        _collectionId = collectionId;
        _closeDialog = closeDialog;
        _currentBook = currentBook;
        _peopleSource = bookService;
        _collectionSource = lookupService;
        IsNewBook = existingBookId is null;

        SourceNames = sources.Select(s => s.SourceName).Distinct().ToList();

        // Author-provenance note: compare the author set each source returned (case-insensitive,
        // same "; "-joined shape ComputeDiffs uses). Consensus among 2+ sources → reassure;
        // a lone provider → flag it; genuine disagreement → stay silent (the grid row shows it).
        var authorSets = sources
            .Where(s => s.Authors.Count > 0)
            .Select(s => (s.SourceName, Key: string.Join("; ", s.Authors).Trim()))
            .ToList();
        var distinctAuthorSets = authorSets
            .Select(a => a.Key)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        AuthorsAgreementNote = authorSets.Count switch
        {
            0 => null,
            1 => string.Format(Resources.MergeReview_Authors_OnlySource, authorSets[0].SourceName),
            _ => distinctAuthorSets == 1 ? Resources.MergeReview_Authors_AllAgree : null
        };

        var rateLimited = (rateLimitedSources ?? []).Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
        RateLimitedNote = rateLimited.Count > 0
            ? string.Format(Resources.MergeReview_RateLimitedNote, string.Join(", ", rateLimited))
            : null;

        var errored = (erroredSources ?? []).Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
        ErroredNote = errored.Count > 0
            ? string.Format(Resources.MergeReview_ErroredNote, string.Join(", ", errored))
            : null;

        var noResult = (noResultSources ?? []).Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
        NoResultNote = noResult.Count > 0
            ? string.Format(Resources.MergeReview_NoResultNote, string.Join(", ", noResult))
            : null;

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

        // Book identity for the always-visible header — prefer the existing book (recatalog), else the first
        // source that carries the value.
        IdentityTitle = _currentBook?.Title
            ?? firstTitle
            ?? sources.FirstOrDefault(s => !string.IsNullOrWhiteSpace(s.Title))?.Title
            ?? Resources.MergeReview_Identity_Untitled;
        IdentityIsbn = _currentBook?.Isbn
            ?? sources.FirstOrDefault(s => !string.IsNullOrWhiteSpace(s.Isbn))?.Isbn;

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

        // Cover options. Some slots may still be downloading: default-select the first
        // that already has data; otherwise the first slot to finish loading claims the selection.
        foreach (var co in coverOptions)
            CoverOptions.Add(co);
        var firstLoaded = CoverOptions.FirstOrDefault(co => co.ImageData is not null);
        if (firstLoaded is not null)
        {
            firstLoaded.IsSelected = true;
        }
        else
        {
            foreach (var co in CoverOptions)
                co.PropertyChanged += OnStreamedCoverArrived;
        }

        // CoverCells: one slot per column, aligned to AllColumnNames (null Cover = placeholder)
        CoverCells = [.. AllColumnNames.Select(name => new CoverCell
        {
            ColumnName = name,
            Cover = CoverOptions.FirstOrDefault(co => co.SourceName == name)
        })];

        // The picked Authors column seeds the row editor, and re-picking re-seeds it —
        // the pick is the starting point, manual edits happen after picking.
        _authorsDiffRow = FieldDiffs.FirstOrDefault(r => r.RawKey == "Authors");
        if (_authorsDiffRow is not null)
        {
            foreach (var option in _authorsDiffRow.SourceValues)
                option.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(SourceValueOption.IsSelected)
                        && s is SourceValueOption { IsSelected: true })
                        SeedAuthorRows();
                };
        }
        SeedAuthorRows();
    }

    /// <summary>
    /// Loads the people snapshot behind the author type-ahead and, for new books, the collection
    /// choices. Called by the window service before the dialog is shown; rows are re-seeded so
    /// they resolve existing-vs-new against the loaded snapshot.
    /// </summary>
    public async Task InitializeAsync()
    {
        if (_peopleSource is not null)
        {
            PersonSuggestions.LoadSnapshot(await _peopleSource.GetPeopleAsync());
            SeedAuthorRows();
        }

        if (IsNewBook && _collectionSource is not null)
        {
            var collections = await _collectionSource.GetCollectionsAsync();
            foreach (var collection in collections)
                Collections.Add(collection);
            SelectedCollection = Collections.FirstOrDefault(c => c.CollectionId == _collectionId)
                ?? Collections.FirstOrDefault();
        }
    }

    private void SeedAuthorRows()
    {
        var picked = _authorsDiffRow?.SourceValues.Find(sv => sv.IsSelected)?.Value;
        IReadOnlyList<string> names = picked is not null
            ? picked.Split("; ", StringSplitOptions.RemoveEmptyEntries)
            : _currentBook?.Authors ?? _sources.FirstOrDefault()?.Authors ?? [];

        AuthorRows.Clear();
        foreach (var name in names)
            AuthorRows.Add(new PersonSuggestionRowViewModel(PersonSuggestions) { SearchText = name });
    }

    [RelayCommand]
    private void AddAuthorRow()
        => AuthorRows.Add(new PersonSuggestionRowViewModel(PersonSuggestions));

    [RelayCommand]
    private void RemoveAuthorRow(PersonSuggestionRowViewModel row)
        => AuthorRows.Remove(row);

    private void OnStreamedCoverArrived(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(CoverOption.ImageData)) return;
        if (sender is not CoverOption { ImageData: not null } loaded) return;

        foreach (var co in CoverOptions)
            co.PropertyChanged -= OnStreamedCoverArrived;
        if (!CoverOptions.Any(co => co.IsSelected))
            loaded.IsSelected = true;
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
    private Task SaveAsync() => SaveAndCloseAsync(openEditor: false);

    [RelayCommand]
    private Task SaveAndOpenEditorAsync() => SaveAndCloseAsync(openEditor: true);

    /// <summary>The collection a new book is saved into: the picker's choice, else the caller-supplied default.</summary>
    private int? ResolveCollectionId()
        => SelectedCollection?.CollectionId ?? _collectionId;

    private async Task SaveAndCloseAsync(bool openEditor)
    {
        int savedBookId;
        try
        {
            var merged = BuildMergedMetadata();
            var selectedCover = CoverOptions.FirstOrDefault(co => co.IsSelected);
            var coverPath = selectedCover?.ImageData;

            if (IsNewBook)
            {
                // Check for duplicate ISBN before creating a new book
                var existing = merged.Isbn is not null && _windowService is not null
                    ? await _bookService.FindBookByIsbnAsync(merged.Isbn)
                    : null;
                if (existing is not null)
                {
                    var choice = await _windowService!.ShowDuplicateIsbnDialogAsync(
                        merged.Isbn!,
                        existing.Title ?? merged.Isbn!);

                    if (choice == DuplicateIsbnResult.Cancel)
                        return;

                    if (choice == DuplicateIsbnResult.UpdateExisting)
                    {
                        await _bookService.UpdateBookFromMetadataAsync(existing.BookId, merged, coverPath);
                        _messenger.Send(new BookSavedMessage(existing.BookId));
                        _closeDialog(true);
                        if (openEditor)
                            await OpenEditorAsync(existing.BookId);
                        return;
                    }
                    // AddAsNew — fall through to normal add
                }

                var added = await _bookService.AddBookFromMetadataAsync(merged, coverPath, ResolveCollectionId());
                savedBookId = added.BookId;
            }
            else
            {
                await _bookService.UpdateBookFromMetadataAsync(_existingBookId!.Value, merged, coverPath);
                savedBookId = _existingBookId.Value;
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
            return;
        }

        if (openEditor)
            await OpenEditorAsync(savedBookId);
    }

    /// <summary>
    /// Opens the saved book in the full editor after the dialog has closed. A failure here must
    /// not undo the finished save, so it only logs.
    /// </summary>
    private async Task OpenEditorAsync(int bookId)
    {
        if (_windowService is null) return;
        try
        {
            await _windowService.OpenFullDetailsWindowAsync(bookId);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to open the editor for book {BookId} after merge review save", bookId);
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

        // Saved contributors come from the row editor (seeded from the picked Authors column),
        // not the raw pick — the rows carry the user's spelling fixes, drops, and additions.
        IReadOnlyList<string> GetAuthors()
            => AuthorRows
                .Select(r => r.NameToPersist)
                .Where(name => name.Length > 0)
                .ToList();

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
