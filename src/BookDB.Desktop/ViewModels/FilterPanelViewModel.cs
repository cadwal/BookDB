using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Threading;
using Serilog;
using BookDB.Desktop.Helpers;
using BookDB.Desktop.Localization;
using BookDB.Desktop.Messages;
using BookDB.Desktop.Services;
using BookDB.Logic.Services;
using BookDB.Models.Entities;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;

namespace BookDB.Desktop.ViewModels;

// ---------------------------------------------------------------------------
// Supporting data model classes
// ---------------------------------------------------------------------------

public partial class FacetLetterGroupViewModel : ObservableObject
{
    private readonly List<FacetValueViewModel> _sourceValues = [];
    private bool _suppressAllCheckedPropagation;

    public string Letter { get; init; } = string.Empty;

    public ObservableCollection<FacetValueViewModel> Values { get; } = [];

    public IReadOnlyList<FacetValueViewModel> AllValues => _sourceValues;

    public int TotalCount => _sourceValues.Count;

    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    private bool? _isAllChecked = false;

    public void SeedSourceValues(IEnumerable<FacetValueViewModel> values)
    {
        foreach (var v in _sourceValues)
            v.PropertyChanged -= OnSourceValuePropertyChanged;

        _sourceValues.Clear();
        Values.Clear();

        foreach (var v in values)
        {
            _sourceValues.Add(v);
            v.PropertyChanged += OnSourceValuePropertyChanged;
        }

        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(AllValues));

        RecomputeAllChecked();
    }

    partial void OnIsExpandedChanged(bool value)
    {
        if (value)
        {
            foreach (var v in _sourceValues)
                Values.Add(v);
        }
        else
        {
            Values.Clear();
        }
    }

    partial void OnIsAllCheckedChanged(bool? value)
    {
        if (_suppressAllCheckedPropagation)
            return;

        if (value == null)
        {
            // User cycled into mixed state — coerce back to false
            IsAllChecked = false;
            return;
        }

        _suppressAllCheckedPropagation = true;
        foreach (var v in _sourceValues)
            v.IsChecked = value.GetValueOrDefault(false);
        _suppressAllCheckedPropagation = false;
    }

    private void OnSourceValuePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(FacetValueViewModel.IsChecked))
            return;

        RecomputeAllChecked();
    }

    private void RecomputeAllChecked()
    {
        if (_sourceValues.Count == 0)
        {
            _suppressAllCheckedPropagation = true;
            IsAllChecked = false;
            _suppressAllCheckedPropagation = false;
            return;
        }

        var checkedCount = _sourceValues.Count(v => v.IsChecked);
        bool? newState = checkedCount == 0 ? false
                       : checkedCount == _sourceValues.Count ? (bool?)true
                       : null;

        if (newState != IsAllChecked)
        {
            _suppressAllCheckedPropagation = true;
            IsAllChecked = newState;
            _suppressAllCheckedPropagation = false;
        }
    }
}

public partial class FacetGroupViewModel : ObservableObject
{
    public string Name { get; init; } = string.Empty;
    public string FacetKey { get; init; } = string.Empty;
    public bool IsGrouped { get; init; }

    [ObservableProperty]
    private bool _isExpanded = false;

    public ObservableCollection<FacetValueViewModel> Values { get; } = [];
    public ObservableCollection<FacetLetterGroupViewModel> LetterGroups { get; } = [];

    /// <summary>Total unique values: sum of letter group counts for grouped facets, or Values.Count for flat facets.</summary>
    public int TotalCount => IsGrouped
        ? LetterGroups.Sum(lg => lg.TotalCount)
        : Values.Count;

    /// <summary>Called by LoadFacetsAsync after populating Values or LetterGroups to refresh TotalCount bindings.</summary>
    public void NotifyTotalCountChanged() => OnPropertyChanged(nameof(TotalCount));
}

public partial class FacetValueViewModel : ObservableObject
{
    public int Id { get; init; }
    /// <summary>For non-Author facets: the display name. For Author: SortName (grouping key).</summary>
    public string Name { get; init; } = string.Empty;
    /// <summary>Author facet only: SortName used for grouping. Empty for non-Author facets.</summary>
    public string SortName { get; init; } = string.Empty;
    /// <summary>Author facet only: full DisplayName (e.g. "John Smith"). Empty for non-Author facets.</summary>
    public string DisplayName { get; init; } = string.Empty;
    /// <summary>Setting-driven display text. For Author: SortName or DisplayName per user setting. For others: same as Name.</summary>
    public string DisplayLabel { get; init; } = string.Empty;
    public int Count { get; init; }

    [ObservableProperty]
    private bool _isChecked;
}

// ---------------------------------------------------------------------------
// FilterPanelViewModel
// ---------------------------------------------------------------------------

public partial class FilterPanelViewModel : ObservableRecipient,
    IRecipient<CollectionSelectionChangedMessage>,
    IRecipient<BookSavedMessage>,
    IRecipient<BooksDeletedMessage>,
    IRecipient<SavedSearchChangedMessage>,
    IRecipient<ClearFiltersRequestedMessage>,
    IRecipient<ImportCompleteMessage>,
    IRecipient<LookupsChangedMessage>,
    IRecipient<FilterToAuthorMessage>,
    IRecipient<SettingsSavedMessage>
{
    // Derive sort comparer from the OS region setting (e.g. Sweden → sv-SE collation).
    // CurrentCulture may be "en-SE" which uses English collation and treats Å/Ä as accented A.
    // We find the lowest-LCID specific culture for the current region, which is the primary
    // native language (sv-SE for SE, nb-NO for NO, de-DE for DE, etc.).
    private static readonly StringComparer s_facetComparer = BuildFacetComparer();

    private static StringComparer BuildFacetComparer()
    {
        try
        {
            var regionCode = RegionInfo.CurrentRegion.TwoLetterISORegionName;
            var candidate = CultureInfo.GetCultures(CultureTypes.SpecificCultures)
                .Where(c => c.Name.EndsWith($"-{regionCode}", StringComparison.OrdinalIgnoreCase))
                .OrderBy(c => c.LCID)
                .FirstOrDefault();
            return StringComparer.Create(candidate ?? CultureInfo.CurrentCulture, ignoreCase: false);
        }
        catch
        {
            return StringComparer.Create(CultureInfo.CurrentCulture, ignoreCase: false);
        }
    }

    private readonly IBookService _bookService;
    private readonly IBookSearchService _bookSearchService;
    private readonly ISettingsService _settingsService;
    private readonly IWindowService _windowService;
    private string _authorFacetLabel = "SortName";
    private IReadOnlySet<int> _activeCollectionIds = new HashSet<int>();

    public ObservableCollection<FacetGroupViewModel> FacetGroups { get; } = [];
    public ObservableCollection<SavedSearch> SavedSearches { get; } = [];

    [ObservableProperty]
    private SavedSearch? _activeSavedSearch;

    [ObservableProperty]
    private bool _isLoanedOutFilterActive;

    partial void OnIsLoanedOutFilterActiveChanged(bool value) => OnFacetSelectionChanged();

    public FilterPanelViewModel(IMessenger messenger, IBookService bookService, IBookSearchService bookSearchService, ISettingsService settingsService, IWindowService windowService)
        : base(messenger)
    {
        _bookService = bookService;
        _bookSearchService = bookSearchService;
        _settingsService = settingsService;
        _windowService = windowService;
        IsActive = true;

        // Initialize the 10 facet groups in order
        FacetGroups.Add(new FacetGroupViewModel { Name = Resources.SearchField_Author,    FacetKey = "Author",    IsGrouped = true, IsExpanded = true });
        FacetGroups.Add(new FacetGroupViewModel { Name = Resources.SearchField_Series,    FacetKey = "Series",    IsGrouped = true });
        FacetGroups.Add(new FacetGroupViewModel { Name = Resources.SearchField_Publisher, FacetKey = "Publisher", IsGrouped = true });
        FacetGroups.Add(new FacetGroupViewModel { Name = Resources.SearchField_Category,  FacetKey = "Category"  });
        FacetGroups.Add(new FacetGroupViewModel { Name = Resources.SearchField_Format,    FacetKey = "Format"    });
        FacetGroups.Add(new FacetGroupViewModel { Name = Resources.SearchField_Language,  FacetKey = "Language"  });
        FacetGroups.Add(new FacetGroupViewModel { Name = Resources.SearchField_Rating,    FacetKey = "Rating"    });
        FacetGroups.Add(new FacetGroupViewModel { Name = Resources.SearchField_Status,    FacetKey = "Status"    });
        FacetGroups.Add(new FacetGroupViewModel { Name = Resources.SearchField_Location,  FacetKey = "Location"  });
        FacetGroups.Add(new FacetGroupViewModel { Name = Resources.SearchField_Owner,     FacetKey = "Owner"     });
    }

    // ---------------------------------------------------------------------------
    // Message handling
    // ---------------------------------------------------------------------------

    private void PostLoadFacets() =>
        UIThreadHelper.PostAsync(LoadFacetsAsync, "load facets");

    public void Receive(CollectionSelectionChangedMessage message)
    {
        _activeCollectionIds = message.Value;
        UIThreadHelper.PostAsync(async () =>
        {
            await LoadFacetsAsync();
            await LoadSavedSearchesAsync();
        }, "load facets and saved searches");
    }

    public void Receive(BookSavedMessage message) => PostLoadFacets();

    public void Receive(BooksDeletedMessage message) => PostLoadFacets();

    public void Receive(SavedSearchChangedMessage message) =>
        UIThreadHelper.PostAsync(LoadSavedSearchesAsync, "load saved searches");

    public void Receive(ClearFiltersRequestedMessage message)
    {
        Dispatcher.UIThread.Post(() =>
        {
            foreach (var group in FacetGroups)
                foreach (var value in GetAllValues(group))
                {
                    value.PropertyChanged -= OnFacetValuePropertyChanged;
                    value.IsChecked = false;
                    value.PropertyChanged += OnFacetValuePropertyChanged;
                }
            ActiveSavedSearch = null;
        });
    }

    public void Receive(ImportCompleteMessage message) => PostLoadFacets();

    public void Receive(FilterToAuthorMessage message)
    {
        Dispatcher.UIThread.Post(() =>
        {
            foreach (var group in FacetGroups)
                foreach (var value in GetAllValues(group))
                {
                    value.PropertyChanged -= OnFacetValuePropertyChanged;
                    value.IsChecked = false;
                    value.PropertyChanged += OnFacetValuePropertyChanged;
                }
            ActiveSavedSearch = null;
            var authorGroup = FacetGroups.FirstOrDefault(g => g.FacetKey == "Author");
            if (authorGroup is not null)
                foreach (var value in GetAllValues(authorGroup))
                    if (value.Id == message.Value)
                        value.IsChecked = true;
            OnFacetSelectionChanged();
        });
    }

    public void Receive(LookupsChangedMessage message) => PostLoadFacets();

    public void Receive(SettingsSavedMessage message) => PostLoadFacets();

    // ---------------------------------------------------------------------------
    // Methods
    // ---------------------------------------------------------------------------

    public async Task LoadFacetsAsync()
    {
        _authorFacetLabel = await _settingsService.GetAsync("AuthorFacetLabel") ?? "SortName";
        foreach (var group in FacetGroups)
        {
            // Capture existing checked state by ID before clearing.
            // Grouped facets hold their values in LetterGroups[*].AllValues (not in group.Values)
            // because letter groups lazy-load; group.Values is empty for grouped facets.
            var previouslyChecked = group.IsGrouped
                ? group.LetterGroups.SelectMany(lg => lg.AllValues).Where(v => v.IsChecked).Select(v => v.Id).ToHashSet()
                : [.. group.Values.Where(v => v.IsChecked).Select(v => v.Id)];

            // Unsubscribe old flat values before clearing (flat facets only)
            if (!group.IsGrouped)
            {
                foreach (var v in group.Values)
                    v.PropertyChanged -= OnFacetValuePropertyChanged;
            }

            group.Values.Clear();
            group.LetterGroups.Clear();

            var counts = await _bookSearchService.GetFacetCountsAsync(_activeCollectionIds, group.FacetKey);

            var isAuthor = group.FacetKey == "Author";
            var facetValues = new List<FacetValueViewModel>(counts.Count);
            foreach (var fc in counts)
            {
                var facetValueViewModel = new FacetValueViewModel
                {
                    Id          = fc.Id,
                    Name        = fc.Name,        // For Author: SortName (or DisplayName fallback). Others: entity Name.
                    SortName    = isAuthor ? fc.Name : string.Empty,
                    DisplayName = isAuthor ? (fc.AlternateName ?? fc.Name) : string.Empty,
                    DisplayLabel = isAuthor
                        ? (string.IsNullOrWhiteSpace(fc.Name)
                            ? (fc.AlternateName ?? string.Empty)
                            : (_authorFacetLabel == "DisplayName" ? (fc.AlternateName ?? fc.Name) : fc.Name))
                        : fc.Name,
                    Count       = fc.Count,
                    // Restore checked state
                    IsChecked   = previouslyChecked.Contains(fc.Id)
                };

                // Subscribe to changes so live filtering fires
                facetValueViewModel.PropertyChanged += OnFacetValuePropertyChanged;
                facetValues.Add(facetValueViewModel);
            }

            // Sort locale-aware so non-ASCII letters (e.g. Å Ä Ö in Swedish) sort after Z
            // rather than by Unicode code point, which gives the wrong order.
            var sortKey = isAuthor
                ? (Func<FacetValueViewModel, string>)(fv => fv.SortName)
                : (fv => fv.Name);
            facetValues.Sort((a, b) => s_facetComparer.Compare(sortKey(a), sortKey(b)));

            if (!group.IsGrouped)
            {
                foreach (var fv in facetValues)
                    group.Values.Add(fv);
                group.NotifyTotalCountChanged();
            }

            if (group.IsGrouped)
            {
                var letterGroups = facetValues
                    .GroupBy(fv =>
                    {
                        var key = group.FacetKey == "Author" ? fv.SortName : fv.Name;
                        return string.IsNullOrEmpty(key) ? "#" : key[0].ToString().ToUpperInvariant();
                    })
                    .OrderBy(g => g.Key, s_facetComparer)
                    .Select(g =>
                    {
                        var lg = new FacetLetterGroupViewModel { Letter = g.Key };
                        lg.SeedSourceValues(g);
                        return lg;
                    });

                foreach (var lg in letterGroups)
                    group.LetterGroups.Add(lg);

                group.NotifyTotalCountChanged();
            }
        }
    }

    private void OnFacetValuePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FacetValueViewModel.IsChecked))
            OnFacetSelectionChanged();
    }

    public void OnFacetSelectionChanged()
    {
        var dict = new Dictionary<string, IReadOnlySet<int>>();

        foreach (var group in FacetGroups)
        {
            var checkedIds = GetAllValues(group)
                .Where(v => v.IsChecked)
                .Select(v => v.Id)
                .ToHashSet();

            if (checkedIds.Count > 0)
                dict[group.FacetKey] = checkedIds;
        }

        Messenger.Send(new FilterChangedMessage(new FilterState(dict, IsLoanedOutFilterActive)));
    }

    public async Task LoadSavedSearchesAsync()
    {
        var searches = await _bookService.GetSavedSearchesAsync();
        SavedSearches.Clear();
        foreach (var s in searches)
            SavedSearches.Add(s);
    }

    // ---------------------------------------------------------------------------
    // Commands
    // ---------------------------------------------------------------------------

    partial void OnActiveSavedSearchChanged(SavedSearch? value)
    {
        if (value == null) return;
        UIThreadHelper.PostAsync(() => ApplySavedSearchAsync(value), $"apply saved search '{value.Name}'");
    }

    [RelayCommand]
    private async Task ApplySavedSearchAsync(SavedSearch search)
    {
        try
        {
            // Clear all facet checkboxes and sync cleared state to BookListViewModel
            foreach (var group in FacetGroups)
                foreach (var value in GetAllValues(group))
                {
                    value.PropertyChanged -= OnFacetValuePropertyChanged;
                    value.IsChecked = false;
                    value.PropertyChanged += OnFacetValuePropertyChanged;
                }
            OnFacetSelectionChanged(); // sends FilterChangedMessage({}) — clears _activeFilterState in BookListViewModel

            var dto = JsonSerializer.Deserialize<QueryJsonDto>(search.QueryJson);
            if (dto?.Conditions == null || dto.Conditions.Count == 0)
                return;

            var conditions = dto.Conditions
                .Select(c => new SearchCondition(
                    Enum.Parse<BookDB.Models.Enums.SearchField>(c.Field, ignoreCase: true),
                    Enum.Parse<BookDB.Models.Enums.SearchOperator>(c.Operator, ignoreCase: true),
                    c.Value))
                .ToList();

            var bookIds = await _bookSearchService.SearchByConditionsAsync(conditions, dto.Combinator);
            Messenger.Send(new AdvancedSearchResultMessage(bookIds));
        }
        catch (JsonException)
        {
            // Malformed QueryJson — log and ignore; no user-visible error
            Log.Warning("Saved search '{Name}' has unrecognized QueryJson format — skipping", search.Name);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to apply saved search '{Name}'", search.Name);
        }
    }

    [RelayCommand]
    private async Task EditSavedSearch(SavedSearch search)
    {
        // Open the advanced search dialog pre-filled with this saved search.
        // Saving updates the existing record and raises SavedSearchChangedMessage,
        // which reloads the list. Avoid triggering ApplySavedSearchAsync via selection.
        await _windowService.ShowAdvancedSearchDialogAsync(search);
    }

    [RelayCommand]
    private async Task DeleteSavedSearch(SavedSearch search)
    {
        await _bookService.DeleteSavedSearchAsync(search.SavedSearchId);
        SavedSearches.Remove(search);

        if (ActiveSavedSearch?.SavedSearchId == search.SavedSearchId)
            ActiveSavedSearch = null;
    }

    [RelayCommand]
    private void ClearFilters()
    {
        IsLoanedOutFilterActive = false;
        foreach (var group in FacetGroups)
            foreach (var value in GetAllValues(group))
            {
                value.PropertyChanged -= OnFacetValuePropertyChanged;
                value.IsChecked = false;
                value.PropertyChanged += OnFacetValuePropertyChanged;
            }

        ActiveSavedSearch = null;
        OnFacetSelectionChanged();
        Messenger.Send(new AdvancedSearchResultMessage(null));
    }

    private static IEnumerable<FacetValueViewModel> GetAllValues(FacetGroupViewModel group) =>
        group.IsGrouped
            ? group.LetterGroups.SelectMany(lg => lg.AllValues)
            : (IEnumerable<FacetValueViewModel>)group.Values;

    // ---------------------------------------------------------------------------
    // Private JSON DTOs for SavedSearch QueryJson deserialization
    // ---------------------------------------------------------------------------

    private sealed class QueryJsonDto
    {
        public string Combinator { get; set; } = "AND";
        public List<ConditionDto> Conditions { get; set; } = [];
    }

    private sealed class ConditionDto
    {
        public string Field { get; set; } = string.Empty;
        public string Operator { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
    }
}
