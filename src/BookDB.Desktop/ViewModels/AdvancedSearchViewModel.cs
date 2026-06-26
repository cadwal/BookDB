using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using BookDB.Data.Interfaces;
using BookDB.Desktop.Helpers;
using BookDB.Desktop.Localization;
using BookDB.Desktop.Messages;
using BookDB.Logic.Services;
using BookDB.Models.Entities;
using BookDB.Models.Enums;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Serilog;

namespace BookDB.Desktop.ViewModels;

// ---------------------------------------------------------------------------
// Option records for enum-backed ComboBox items
// ---------------------------------------------------------------------------

public sealed record SearchFieldOption(SearchField Field, string Label)
{
    public override string ToString() => Label;
}

public sealed record SearchOperatorOption(SearchOperator Operator, string Label)
{
    public override string ToString() => Label;
}

public sealed record CombinatorOption(string Key, string Label)
{
    public override string ToString() => Label;
}

// ---------------------------------------------------------------------------
// SearchConditionViewModel — a single condition row in the advanced search
// ---------------------------------------------------------------------------

public partial class SearchConditionViewModel : ObservableObject
{
    [ObservableProperty]
    private SearchFieldOption _selectedField =
        new(SearchField.Title, Resources.SearchField_Title);

    [ObservableProperty]
    private SearchOperatorOption _selectedOperator =
        new(SearchOperator.Contains, Resources.SearchOperator_Contains);

    [ObservableProperty]
    private string _value = string.Empty;

    public static IReadOnlyList<SearchFieldOption> AvailableFields { get; } =
    [
        new(SearchField.Title,     Resources.SearchField_Title),
        new(SearchField.Author,    Resources.SearchField_Author),
        new(SearchField.Publisher, Resources.SearchField_Publisher),
        new(SearchField.Series,    Resources.SearchField_Series),
        new(SearchField.Isbn,      Resources.SearchField_Isbn),
        new(SearchField.Keywords,  Resources.SearchField_Keywords),
        new(SearchField.Comments,  Resources.SearchField_Comments),
        new(SearchField.Category,  Resources.SearchField_Category),
        new(SearchField.Format,    Resources.SearchField_Format),
        new(SearchField.Language,  Resources.SearchField_Language),
        new(SearchField.Rating,    Resources.SearchField_Rating),
        new(SearchField.Status,    Resources.SearchField_Status),
        new(SearchField.Location,  Resources.SearchField_Location),
        new(SearchField.Owner,     Resources.SearchField_Owner),
        new(SearchField.Year,      Resources.SearchField_Year),
    ];

    public static IReadOnlyList<SearchOperatorOption> AvailableOperators { get; } =
    [
        new(SearchOperator.Contains,    Resources.SearchOperator_Contains),
        new(SearchOperator.NotContains, Resources.SearchOperator_NotContains),
        new(SearchOperator.Equals,      Resources.SearchOperator_Equals),
        new(SearchOperator.NotEquals,   Resources.SearchOperator_NotEquals),
        new(SearchOperator.StartsWith,  Resources.SearchOperator_StartsWith),
        new(SearchOperator.EndsWith,    Resources.SearchOperator_EndsWith),
        new(SearchOperator.IsEmpty,     Resources.SearchOperator_IsEmpty),
        new(SearchOperator.IsNotEmpty,  Resources.SearchOperator_IsNotEmpty),
    ];
}

// ---------------------------------------------------------------------------
// AdvancedSearchViewModel
// ---------------------------------------------------------------------------

public partial class AdvancedSearchViewModel : ObservableObject
{
    private readonly IMessenger _messenger;
    private readonly IBookService _bookService;
    private readonly IBookSearchService _bookSearchService;
    private readonly ILookupService _lookupService;
    private readonly IConnectionHealthMonitor _connectionMonitor;
    private readonly IConnectionFailureClassifier _connectionClassifier;

    // Callback provided by WindowService/dialog code-behind to close with a result
    private Action<bool?>? _closeAction;

    // Non-null when the dialog was opened to edit an existing named search.
    private SavedSearch? _editingSearch;

    public ObservableCollection<SearchConditionViewModel> Conditions { get; } = [];

    [ObservableProperty]
    private CombinatorOption _combinator =
        new(Key: "AND", Label: Resources.SearchCombinator_And);

    [ObservableProperty]
    private string _savedSearchName = string.Empty;

    [ObservableProperty]
    private bool _hasResults;

    // Inline feedback shown after a Test run (e.g. "42 matching books"). Empty until tested.
    [ObservableProperty]
    private string _testResultText = string.Empty;

    /// <summary>Window title — reflects whether we are creating a new search or editing an existing one.</summary>
    public string WindowTitle =>
        _editingSearch != null ? Resources.AdvancedSearch_EditTitle : Resources.AdvancedSearch_Title;

    public IReadOnlyList<CombinatorOption> Combinators { get; } =
    [
        new("AND", Resources.SearchCombinator_And),
        new("OR",  Resources.SearchCombinator_Or),
    ];

    public AdvancedSearchViewModel(
        IMessenger messenger,
        IBookService bookService,
        IBookSearchService bookSearchService,
        ILookupService lookupService,
        IConnectionHealthMonitor connectionMonitor,
        IConnectionFailureClassifier connectionClassifier)
    {
        _messenger = messenger;
        _bookService = bookService;
        _bookSearchService = bookSearchService;
        _lookupService = lookupService;
        _connectionMonitor = connectionMonitor;
        _connectionClassifier = connectionClassifier;

        // Start with one empty condition row
        AddConditionRow(new SearchConditionViewModel());
    }

    public void SetCloseAction(Action<bool?> closeAction)
    {
        _closeAction = closeAction;
    }

    // ---------------------------------------------------------------------------
    // Edit mode — populate the dialog from an existing named search
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Loads an existing saved search into the dialog so the user can edit and re-save it.
    /// Saving will update the existing record rather than creating a new one.
    /// </summary>
    public void LoadFromSavedSearch(SavedSearch search)
    {
        _editingSearch = search;
        SavedSearchName = search.Name;
        OnPropertyChanged(nameof(WindowTitle));

        try
        {
            var dto = JsonSerializer.Deserialize<QueryJsonDto>(search.QueryJson);
            if (dto == null) return;

            Combinator = Combinators.FirstOrDefault(
                c => c.Key.Equals(dto.Combinator, StringComparison.OrdinalIgnoreCase)) ?? Combinator;

            if (dto.Conditions is not { Count: > 0 }) return;

            foreach (var existing in Conditions)
                existing.PropertyChanged -= OnConditionChanged;
            Conditions.Clear();

            foreach (var c in dto.Conditions)
            {
                if (!Enum.TryParse<SearchField>(c.Field, ignoreCase: true, out var field) ||
                    !Enum.TryParse<SearchOperator>(c.Operator, ignoreCase: true, out var op))
                    continue;

                AddConditionRow(new SearchConditionViewModel
                {
                    SelectedField = SearchConditionViewModel.AvailableFields.First(f => f.Field == field),
                    SelectedOperator = SearchConditionViewModel.AvailableOperators.First(o => o.Operator == op),
                    Value = c.Value
                });
            }

            // Guarantee at least one row so the dialog is never empty
            if (Conditions.Count == 0)
                AddConditionRow(new SearchConditionViewModel());
        }
        catch (JsonException)
        {
            Log.Warning("Saved search '{Name}' has unrecognized QueryJson format — opening with defaults", search.Name);
        }
    }

    // ---------------------------------------------------------------------------
    // InitializeAsync — load any lookup data needed for field value suggestions
    // ---------------------------------------------------------------------------

    public async Task InitializeAsync()
    {
        try
        {
            // Load lookup lists (Formats, Categories, etc.) if needed for future suggestions
            // Currently the condition rows use free-text value fields, so no pre-loading required
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to initialize AdvancedSearchViewModel");
        }
    }

    // ---------------------------------------------------------------------------
    // Commands
    // ---------------------------------------------------------------------------

    [RelayCommand]
    private void AddCondition()
    {
        AddConditionRow(new SearchConditionViewModel());
        ClearTestResult();
    }

    [RelayCommand]
    private void RemoveCondition(SearchConditionViewModel condition)
    {
        // Keep minimum 1 condition row
        if (Conditions.Count <= 1) return;
        condition.PropertyChanged -= OnConditionChanged;
        Conditions.Remove(condition);
        ClearTestResult();
    }

    // Adds a condition row and subscribes to its changes so a stale test result is cleared.
    private void AddConditionRow(SearchConditionViewModel condition)
    {
        condition.PropertyChanged += OnConditionChanged;
        Conditions.Add(condition);
    }

    private void OnConditionChanged(object? sender, PropertyChangedEventArgs e) => ClearTestResult();

    // Discards the inline test result — called whenever the query definition changes,
    // so the displayed match count never goes stale relative to the current conditions.
    private void ClearTestResult() => TestResultText = string.Empty;

    partial void OnCombinatorChanged(CombinatorOption value) => ClearTestResult();

    [RelayCommand]
    private async Task SearchAsync()
    {
        try
        {
            await RunSearchAsync();
            HasResults = true;
            _closeAction?.Invoke(true);
        }
        catch (Exception ex)
        {
            _connectionMonitor.ReportIfConnectionLoss(_connectionClassifier, ex);
            Log.Error(ex, "Advanced search failed");
        }
    }

    [RelayCommand]
    private async Task TestSearchAsync()
    {
        try
        {
            var count = await RunSearchAsync();
            TestResultText = string.Format(Resources.AdvancedSearch_MatchCountFormat, count);
        }
        catch (Exception ex)
        {
            _connectionMonitor.ReportIfConnectionLoss(_connectionClassifier, ex);
            Log.Error(ex, "Advanced search test failed");
        }
    }

    // Builds the query, runs it, and applies the result to the main book list.
    // Returns the number of matching books. Does NOT close the dialog.
    private async Task<int> RunSearchAsync()
    {
        var conditions = BuildSearchConditions();
        var bookIds = await _bookSearchService.SearchByConditionsAsync(conditions, Combinator.Key);

        // Send the result message — BookListViewModel receives it via IRecipient<AdvancedSearchResultMessage>
        _messenger.Send(new AdvancedSearchResultMessage(bookIds));

        return bookIds.Count;
    }

    [RelayCommand(CanExecute = nameof(CanSaveSearch))]
    private async Task SaveSearchAsync()
    {
        try
        {
            var conditions = BuildSearchConditions();
            var queryJson = SerializeQueryJson(conditions, Combinator.Key);

            if (_editingSearch != null)
            {
                _editingSearch.Name = SavedSearchName;
                _editingSearch.QueryJson = queryJson;
                await _bookService.UpdateSavedSearchAsync(_editingSearch);
            }
            else
            {
                var search = new SavedSearch
                {
                    Name = SavedSearchName,
                    QueryJson = queryJson,
                    CreatedAt = DateTime.UtcNow
                };
                await _bookService.AddSavedSearchAsync(search);
            }

            _messenger.Send(new SavedSearchChangedMessage());
            _closeAction?.Invoke(true);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to save search as named search");
        }
    }

    private bool CanSaveSearch() => !string.IsNullOrWhiteSpace(SavedSearchName);

    [RelayCommand]
    private void Cancel()
    {
        _closeAction?.Invoke(false);
    }

    // ---------------------------------------------------------------------------
    // Partial property change handlers
    // ---------------------------------------------------------------------------

    partial void OnSavedSearchNameChanged(string value)
    {
        SaveSearchCommand.NotifyCanExecuteChanged();
    }

    // ---------------------------------------------------------------------------
    // Private helpers
    // ---------------------------------------------------------------------------

    private IReadOnlyList<SearchCondition> BuildSearchConditions()
    {
        var list = new List<SearchCondition>();
        foreach (var cvm in Conditions)
        {
            list.Add(new SearchCondition(
                cvm.SelectedField.Field,
                cvm.SelectedOperator.Operator,
                cvm.Value));
        }
        return list;
    }

    private static string SerializeQueryJson(
        IReadOnlyList<SearchCondition> conditions,
        string combinator)
    {
        var dto = new QueryJsonDto
        {
            Combinator = combinator,
            Conditions = []
        };

        foreach (var c in conditions)
            dto.Conditions.Add(new ConditionDto
            {
                Field = c.Field.ToString(),
                Operator = c.Operator.ToString(),
                Value = c.Value
            });

        return JsonSerializer.Serialize(dto, new JsonSerializerOptions { WriteIndented = false });
    }

    // ---------------------------------------------------------------------------
    // JSON DTOs for QueryJson persistence
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
