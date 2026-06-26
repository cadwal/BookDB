using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BookDB.Data.Interfaces;
using BookDB.Desktop.Helpers;
using BookDB.Desktop.Localization;
using BookDB.Desktop.Services;
using BookDB.Logic.Services;
using BookDB.Models.Interfaces;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;

namespace BookDB.Desktop.ViewModels;

public sealed partial class PrintDialogViewModel : ObservableObject
{
    private readonly IPrintService _printService;
    private readonly ISettingsService _settingsService;
    private readonly IFilePickerService _filePickerService;

    // CloseDialog returns PrintParameters? (null = cancelled)
    public Action<PrintParameters?>? CloseDialog { get; set; }

    // --- Filter state (passed from MainWindowViewModel) ---
    private IReadOnlySet<int>? _collectionIds;
    private IReadOnlyList<int>? _searchBookIds;
    private Dictionary<string, HashSet<int>>? _facetFilters;
    private string? _sortColumn;
    private bool _sortAscending;

    // --- Scope ---
    [ObservableProperty]
    private int _bookCount;

    public string ScopeSummaryText => BookCount == 0
        ? Resources.Print_ScopeSummary_None
        : BookCount == 1
            ? string.Format(Resources.Print_ScopeSummary_Single, BookCount)
            : string.Format(Resources.Print_ScopeSummary_Multiple, BookCount);

    partial void OnBookCountChanged(int value) =>
        OnPropertyChanged(nameof(ScopeSummaryText));

    // --- Preset management ---
    public ObservableCollection<PrintPreset> Presets { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanModifySelectedPreset))]
    [NotifyPropertyChangedFor(nameof(CanDeleteSelectedPreset))]
    [NotifyPropertyChangedFor(nameof(SelectedPresetDisplayName))]
    [NotifyCanExecuteChangedFor(nameof(RenamePresetCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeletePresetCommand))]
    private PrintPreset? _selectedPreset;

    public bool CanModifySelectedPreset =>
        SelectedPreset != null && SelectedPreset.Name != PrintPreset.StandardPresetName;

    public bool CanDeleteSelectedPreset =>
        SelectedPreset != null && SelectedPreset.Name != PrintPreset.StandardPresetName;

    public string SelectedPresetDisplayName =>
        SelectedPreset?.Name == PrintPreset.StandardPresetName
            ? Resources.Print_StandardPresetDisplayName
            : SelectedPreset?.Name ?? string.Empty;

    // --- Live-edit fields (bound to Zone 4 form controls) ---
    [ObservableProperty] private bool _isPortrait = true;
    [ObservableProperty] private bool _isLandscape;
    [ObservableProperty] private decimal _fontSize = 10;
    [ObservableProperty] private string _headerText = string.Empty;
    [ObservableProperty] private string _footerText = string.Empty;
    [ObservableProperty] private decimal _marginHorizontalMm = 15;
    [ObservableProperty] private decimal _marginVerticalMm = 20;

    // --- Column list ---
    public ObservableCollection<PrintColumnItem> Columns { get; } = [];

    // --- Error feedback ---
    [ObservableProperty] private string _errorMessage = string.Empty;

    // --- Inline preset naming state ---
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowPresetRow))]
    [NotifyPropertyChangedFor(nameof(ShowNamingRow))]
    [NotifyPropertyChangedFor(nameof(IsPreviewDefault))]
    private bool _isNamingMode;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowPresetRow))]
    [NotifyPropertyChangedFor(nameof(ShowDeleteConfirmRow))]
    [NotifyPropertyChangedFor(nameof(IsPreviewDefault))]
    private bool _isDeleteConfirmMode;

    public bool ShowPresetRow => !IsNamingMode && !IsDeleteConfirmMode;
    public bool ShowNamingRow => IsNamingMode;
    public bool ShowDeleteConfirmRow => IsDeleteConfirmMode;
    public bool IsPreviewDefault => !IsNamingMode && !IsDeleteConfirmMode;

    [ObservableProperty] private string _presetNameEditText = string.Empty;
    [ObservableProperty] private string _presetNameError = string.Empty;

    private bool _isRenamingExisting;

    private readonly IConnectionHealthMonitor _connectionMonitor;
    private readonly IConnectionFailureClassifier _connectionClassifier;

    public PrintDialogViewModel(
        IPrintService printService,
        ISettingsService settingsService,
        IFilePickerService filePickerService,
        IConnectionHealthMonitor connectionMonitor,
        IConnectionFailureClassifier connectionClassifier)
    {
        _printService = printService;
        _settingsService = settingsService;
        _filePickerService = filePickerService;
        _connectionMonitor = connectionMonitor;
        _connectionClassifier = connectionClassifier;
    }

    // A print generation that fails on a dropped remote connection drives the shared status-bar indicator and
    // shows the connection-lost message in the dialog rather than a generic print-failed error.
    private string DescribeFailure(Exception ex) =>
        _connectionMonitor.ReportIfConnectionLoss(_connectionClassifier, ex)
            ? Resources.StatusBar_Connection_Lost
            : string.Format(Resources.PrintList_Failed, ex.Message);

    public async Task InitializeAsync(
        IReadOnlySet<int>? collectionIds,
        IReadOnlyList<int>? searchBookIds,
        Dictionary<string, HashSet<int>>? facetFilters,
        string? sortColumn,
        bool sortAscending,
        int bookCount = 0,
        CancellationToken ct = default)
    {
        _collectionIds = collectionIds;
        _searchBookIds = searchBookIds;
        _facetFilters = facetFilters;
        _sortColumn = sortColumn;
        _sortAscending = sortAscending;

        BookCount = bookCount;

        // Load presets from ISettingsService
        List<PrintPreset> loadedPresets;
        try
        {
            var json = await _settingsService.GetAsync("PrintPresets", ct);
            loadedPresets = json is null
                ? [PrintPreset.CreateDefault(Resources.Print_DefaultHeaderTitle)]
                : JsonSerializer.Deserialize<List<PrintPreset>>(json) ?? [PrintPreset.CreateDefault(Resources.Print_DefaultHeaderTitle)];
        }
        catch (Exception ex)
        {
            // Malformed preset JSON — fall back to default (DoS mitigation)
            Log.Error(ex, "PrintDialogViewModel: failed to deserialize PrintPresets; using default");
            loadedPresets = [PrintPreset.CreateDefault(Resources.Print_DefaultHeaderTitle)];
        }

        // Ensure Standard preset always exists
        if (!loadedPresets.Any(p => p.Name == PrintPreset.StandardPresetName))
            loadedPresets.Insert(0, PrintPreset.CreateDefault(Resources.Print_DefaultHeaderTitle));

        Presets.Clear();
        foreach (var preset in loadedPresets)
            Presets.Add(preset);

        // Build column list from IPrintService.AllColumnNames
        Columns.Clear();
        foreach (var key in _printService.AllColumnNames)
        {
            var label = Resources.ResourceManager.GetString("Print_Column_" + key, null) ?? key;
            Columns.Add(new PrintColumnItem(key, label, isSelected: false));
        }

        SelectedPreset = Presets.FirstOrDefault();
    }

    public void SetBookCount(int count)
    {
        BookCount = count;
    }

    partial void OnSelectedPresetChanged(PrintPreset? value)
    {
        if (value == null) return;

        IsPortrait = value.Orientation == PageOrientation.Portrait;
        IsLandscape = value.Orientation == PageOrientation.Landscape;
        FontSize = (decimal)value.FontSize;
        HeaderText = value.HeaderText;
        FooterText = value.FooterText;
        MarginHorizontalMm = (decimal)value.MarginHorizontalMm;
        MarginVerticalMm = (decimal)value.MarginVerticalMm;

        // Update column checkboxes
        var selected = new HashSet<string>(value.Columns, StringComparer.Ordinal);
        foreach (var column in Columns)
            column.IsSelected = selected.Contains(column.Key);
    }

    [RelayCommand]
    private void SelectAllColumns()
    {
        foreach (var column in Columns)
            column.IsSelected = true;
    }

    [RelayCommand]
    private void ClearAllColumns()
    {
        foreach (var column in Columns)
            column.IsSelected = false;
    }

    [RelayCommand]
    private void Cancel() => CloseDialog?.Invoke(null);

    private PrintPreset BuildCurrentPreset(string name)
    {
        var selectedColumns = Columns
            .Where(c => c.IsSelected)
            .Select(c => c.Key)
            .ToList();

        return new PrintPreset(
            Name: name,
            Columns: selectedColumns.Count > 0 ? selectedColumns : _printService.DefaultColumnNames,
            Orientation: IsPortrait ? PageOrientation.Portrait : PageOrientation.Landscape,
            FontSize: (float)FontSize,
            MarginHorizontalMm: (float)MarginHorizontalMm,
            MarginVerticalMm: (float)MarginVerticalMm,
            HeaderText: HeaderText,
            FooterText: FooterText);
    }

    private async Task SavePresetsAsync(CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(Presets.ToList());
        await _settingsService.SetAsync("PrintPresets", json, ct);
    }

    [RelayCommand(CanExecute = nameof(CanExecutePrint))]
    private async Task Preview()
    {
        var preset = BuildCurrentPreset(SelectedPreset?.Name ?? PrintPreset.StandardPresetName);

        // Auto-save preset changes before generating
        if (SelectedPreset != null)
        {
            var index = Presets.IndexOf(SelectedPreset);
            if (index >= 0)
            {
                Presets[index] = preset;
                SelectedPreset = preset;
            }
        }

        await SavePresetsAsync();

        var tempPath = Path.Combine(Path.GetTempPath(), "BookDB-Preview.pdf");
        var parameters = new PrintParameters(
            OutputPath: tempPath,
            CollectionIds: _collectionIds,
            SearchBookIds: _searchBookIds,
            FacetFilters: _facetFilters,
            SortColumn: _sortColumn,
            SortAscending: _sortAscending,
            Preset: preset);

        try
        {
            await _printService.GenerateAsync(parameters);

            // Open in system PDF viewer (cross-platform — see SystemLauncher).
            Helpers.SystemLauncher.Open(tempPath);

            CloseDialog?.Invoke(parameters);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "PrintDialogViewModel: Preview failed");
            ErrorMessage = DescribeFailure(ex);
        }
    }

    private bool CanExecutePrint() => BookCount > 0;

    [RelayCommand(CanExecute = nameof(CanExecutePrint))]
    private async Task SaveAsPdf()
    {
        var outputPath = await _filePickerService.SaveFileAsync(Localization.Resources.FilePicker_SavePdfReport, "BookDB-Report.pdf", [".pdf"]);
        if (string.IsNullOrEmpty(outputPath))
            return;

        var preset = BuildCurrentPreset(SelectedPreset?.Name ?? PrintPreset.StandardPresetName);

        if (SelectedPreset != null)
        {
            var index = Presets.IndexOf(SelectedPreset);
            if (index >= 0)
            {
                Presets[index] = preset;
                SelectedPreset = preset;
            }
        }

        await SavePresetsAsync();

        var parameters = new PrintParameters(
            OutputPath: outputPath,
            CollectionIds: _collectionIds,
            SearchBookIds: _searchBookIds,
            FacetFilters: _facetFilters,
            SortColumn: _sortColumn,
            SortAscending: _sortAscending,
            Preset: preset);

        try
        {
            await _printService.GenerateAsync(parameters);
            CloseDialog?.Invoke(parameters);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "PrintDialogViewModel: SaveAsPdf failed");
            ErrorMessage = DescribeFailure(ex);
        }
    }

    [RelayCommand]
    private void NewPreset()
    {
        _isRenamingExisting = false;
        PresetNameEditText = string.Empty;
        PresetNameError = string.Empty;
        IsNamingMode = true;
    }

    [RelayCommand(CanExecute = nameof(CanModifySelectedPreset))]
    private void RenamePreset()
    {
        _isRenamingExisting = true;
        PresetNameEditText = SelectedPreset?.Name ?? string.Empty;
        PresetNameError = string.Empty;
        IsNamingMode = true;
    }

    [RelayCommand(CanExecute = nameof(CanDeleteSelectedPreset))]
    private void DeletePreset()
    {
        IsDeleteConfirmMode = true;
    }

    [RelayCommand]
    private async Task ConfirmPresetName()
    {
        var name = PresetNameEditText.Trim();

        if (string.IsNullOrEmpty(name))
        {
            PresetNameError = Resources.Print_Error_PresetNameEmpty;
            return;
        }

        var nameExists = Presets.Any(p =>
            string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)
            && (!_isRenamingExisting || p != SelectedPreset));

        if (nameExists)
        {
            PresetNameError = Resources.Print_Error_PresetNameDuplicate;
            return;
        }

        var current = SelectedPreset;
        if (_isRenamingExisting && current != null)
        {
            // Rename: replace existing preset with same fields but new name
            var index = Presets.IndexOf(current);
            if (index < 0)
            {
                // Preset was removed while naming UI was open — bail silently
                IsNamingMode = false;
                PresetNameError = string.Empty;
                return;
            }
            var renamed = BuildCurrentPreset(name);
            try
            {
                Presets[index] = renamed;
                SelectedPreset = renamed;
                await SavePresetsAsync();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "PrintDialogViewModel: failed to persist renamed preset");
                PresetNameError = Resources.Print_Error_PresetSaveFailed;
                return;
            }
        }
        else
        {
            // New: create from current form state
            var newPreset = BuildCurrentPreset(name);
            try
            {
                Presets.Add(newPreset);
                SelectedPreset = newPreset;
                await SavePresetsAsync();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "PrintDialogViewModel: failed to persist new preset");
                PresetNameError = Resources.Print_Error_PresetSaveFailed;
                return;
            }
        }

        IsNamingMode = false;
        PresetNameError = string.Empty;
    }

    [RelayCommand]
    private void CancelPresetName()
    {
        IsNamingMode = false;
        PresetNameError = string.Empty;
    }

    [RelayCommand]
    private async Task ConfirmDelete()
    {
        if (SelectedPreset == null) return;

        Presets.Remove(SelectedPreset);
        SelectedPreset = Presets.FirstOrDefault();
        await SavePresetsAsync();
        IsDeleteConfirmMode = false;
    }

    [RelayCommand]
    private void CancelDelete()
    {
        IsDeleteConfirmMode = false;
    }
}

public sealed partial class PrintColumnItem : ObservableObject
{
    public string Key { get; }
    public string Label { get; }

    [ObservableProperty]
    private bool _isSelected;

    public PrintColumnItem(string key, string label, bool isSelected)
    {
        Key = key;
        Label = label;
        _isSelected = isSelected;
    }
}
