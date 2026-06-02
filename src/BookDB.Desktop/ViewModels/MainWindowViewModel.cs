using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BookDB.Desktop.Helpers;
using BookDB.Desktop.Messages;
using BookDB.Desktop.Services;
using BookDB.Help;
using BookDB.Logic.Services;
using BookDB.Models.Interfaces;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Serilog;

namespace BookDB.Desktop.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly ILookupService _lookupService;
    private readonly IWindowService _windowService;
    private readonly IFilePickerService _filePickerService;
    private readonly IBackupService _backupService;
    private readonly ICsvExportService _csvExportService;
    private readonly ISettingsService _settingsService;
    private readonly IPrintService _printService;

    public FilterPanelViewModel FilterPanel { get; }
    public BookListViewModel BookList { get; }
    public BookDetailViewModel BookDetail { get; }
    public CollectionSelectorViewModel CollectionSelector { get; }

    [ObservableProperty]
    private string _bookCountText = string.Empty;

    [ObservableProperty]
    private string _batchQueueStatusText = string.Empty;

    [ObservableProperty]
    private bool _isBatchQueueRunning;

    /// <summary>
    /// True when the BatchQueueWindow has been minimized to the status bar via the Minimize button.
    /// The status bar shows a clickable indicator; clicking it calls ReopenBatchWindowCommand.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowBatchStatusBar))]
    private bool _isBatchWindowMinimized;

    /// <summary>
    /// True when the batch queue is either running or minimized — shows the status bar section.
    /// </summary>
    public bool ShowBatchStatusBar => IsBatchQueueRunning || IsBatchWindowMinimized;

    [ObservableProperty]
    private double _filterPanelWidth = 200.0;

    [ObservableProperty]
    private double _detailPanelWidth = 400.0;

    [ObservableProperty]
    private bool _detailPanelVisible = true;

    [ObservableProperty]
    private double _windowWidth = 1200.0;

    [ObservableProperty]
    private double _windowHeight = 800.0;

    [ObservableProperty]
    private double _windowLeft = double.NaN;

    [ObservableProperty]
    private double _windowTop = double.NaN;

    public string DetailPanelToggleChevron => DetailPanelVisible ? "\u00AB" : "\u00BB";

    partial void OnDetailPanelVisibleChanged(bool value)
    {
        OnPropertyChanged(nameof(DetailPanelToggleChevron));
    }

    private static readonly OpenWindowEntry NoWindowsSentinel =
        new("(No open windows)", new RelayCommand(() => { }, () => false));

    private static readonly OpenWindowEntry _categorySeparator = OpenWindowEntry.CreateSeparator();

    /// <summary>
    /// The list of currently open secondary windows shown in the _Window menu.
    /// WindowService calls AddOpenWindow/RemoveOpenWindow to keep this up to date.
    /// The sentinel entry is shown when no secondary windows are open.
    /// </summary>
    public ObservableCollection<OpenWindowEntry> OpenWindowEntries { get; }
        = [NoWindowsSentinel];

    public void AddOpenWindow(OpenWindowEntry entry)
    {
        OpenWindowEntries.Remove(NoWindowsSentinel);

        if (entry.Category == WindowCategory.Utility)
        {
            bool hasBookEdit = OpenWindowEntries.Any(e => !e.IsSeparator && e.Category == WindowCategory.BookEdit);
            bool hasSeparator = OpenWindowEntries.Contains(_categorySeparator);
            if (hasBookEdit && !hasSeparator)
                OpenWindowEntries.Add(_categorySeparator);
        }

        OpenWindowEntries.Add(entry);
    }

    public void RemoveOpenWindow(OpenWindowEntry entry)
    {
        OpenWindowEntries.Remove(entry);

        bool hasBookEdit = OpenWindowEntries.Any(e => !e.IsSeparator && e.Category == WindowCategory.BookEdit);
        bool hasUtility  = OpenWindowEntries.Any(e => !e.IsSeparator && e.Category == WindowCategory.Utility);
        if (!hasBookEdit || !hasUtility)
            OpenWindowEntries.Remove(_categorySeparator);

        if (!OpenWindowEntries.Any(e => !e.IsSeparator))
        {
            OpenWindowEntries.Clear();
            OpenWindowEntries.Add(NoWindowsSentinel);
        }
    }

    public MainWindowViewModel(
        FilterPanelViewModel filterPanel,
        BookListViewModel bookList,
        BookDetailViewModel bookDetail,
        CollectionSelectorViewModel collectionSelector,
        ILookupService lookupService,
        IWindowService windowService,
        IMessenger messenger,
        IFilePickerService filePickerService,
        IBackupService backupService,
        ICsvExportService csvExportService,
        ISettingsService settingsService,
        IPrintService printService)
    {
        FilterPanel = filterPanel;
        BookList = bookList;
        BookDetail = bookDetail;
        CollectionSelector = collectionSelector;
        _lookupService = lookupService;
        _windowService = windowService;
        _filePickerService = filePickerService;
        _backupService = backupService;
        _csvExportService = csvExportService;
        _settingsService = settingsService;
        _printService = printService;

        messenger.Register<BookCountChangedMessage>(this, (r, m) =>
        {
            var (filteredTotal, grandTotal) = m.Value;
            ((MainWindowViewModel)r).UpdateBookCountText(filteredTotal, grandTotal);
        });

        messenger.Register<BatchQueueProgressMessage>(this, (_, msg) =>
        {
            IsBatchQueueRunning = msg.IsRunning;
            if (msg.IsRunning)
                BatchQueueStatusText = string.Format(
                    Localization.Resources.StatusBar_BatchQueue_Progress,
                    msg.Current,
                    msg.Total);
            else if (IsBatchWindowMinimized)
                BatchQueueStatusText = Localization.Resources.BatchQueue_CompleteHeader;
            else
                BatchQueueStatusText = string.Empty;
            OnPropertyChanged(nameof(ShowBatchStatusBar));
        });

        // Collections were added/renamed/deleted/reordered in Manage Lookups — refresh the selector.
        messenger.Register<CollectionsChangedMessage>(this, (r, m) =>
            _ = ((MainWindowViewModel)r).ReloadCollectionsAsync());
        // Update ShowBatchStatusBar whenever IsBatchWindowMinimized changes
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(IsBatchWindowMinimized))
                OnPropertyChanged(nameof(ShowBatchStatusBar));
        };
    }

    private void UpdateBookCountText(int filteredTotal, int grandTotal)
    {
        if (filteredTotal == 0 && grandTotal == 0)
        {
            BookCountText = string.Empty;
            return;
        }

        if (filteredTotal == grandTotal)
        {
            // No filter/search active: "5,234 books"
            BookCountText = string.Format(Localization.Resources.StatusBar_BookCount_All, grandTotal);
        }
        else
        {
            // Filter or search active: "147 books (of 5,234)"
            BookCountText = string.Format(Localization.Resources.StatusBar_BookCount_Filtered, filteredTotal, grandTotal);
        }
    }

    // Reloads collections into the selector after they change in Manage Lookups, preserving the
    // current selection where possible (falls back to selecting all if nothing remains selected).
    private async Task ReloadCollectionsAsync()
    {
        try
        {
            var collections = await _lookupService.GetCollectionsAsync();
            var existingIds = collections.Select(c => c.CollectionId).ToHashSet();
            var selectedIds = CollectionSelector.CollectionItems
                .Where(i => i.IsSelected && existingIds.Contains(i.Id))
                .Select(i => i.Id)
                .ToHashSet();
            if (selectedIds.Count == 0)
                selectedIds = existingIds;
            CollectionSelector.Initialize(collections, selectedIds);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "MainWindowViewModel: reloading collections failed");
        }
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        try
        {
            FilterPanelWidth = await LoadDoubleSettingAsync("FilterPanelWidth", FilterPanelWidth, ct);
            DetailPanelWidth = await LoadDoubleSettingAsync("DetailPanelWidth", DetailPanelWidth, ct);

            var detailVisibleStr = await _settingsService.GetAsync("DetailPanelVisible", ct);
            if (detailVisibleStr != null && bool.TryParse(detailVisibleStr, out var dv))
                DetailPanelVisible = dv;

            WindowWidth = await LoadDoubleSettingAsync("WindowWidth", WindowWidth, ct, min: 400);
            WindowHeight = await LoadDoubleSettingAsync("WindowHeight", WindowHeight, ct, min: 300);
            WindowLeft = await LoadDoubleSettingAsync("WindowLeft", WindowLeft, ct);
            WindowTop = await LoadDoubleSettingAsync("WindowTop", WindowTop, ct);

            var collections = await _lookupService.GetCollectionsAsync(ct);

            var selectedIdsStr = await _settingsService.GetAsync("LastSelectedCollectionIds", ct);
            System.Collections.Generic.IReadOnlySet<int> selectedIds;
            if (selectedIdsStr != null)
            {
                selectedIds = selectedIdsStr
                    .Split(',')
                    .Select(s => int.TryParse(s, out var id) ? id : -1)
                    .Where(id => id >= 0)
                    .ToHashSet();
            }
            else
            {
                // First run: all collections selected
                selectedIds = collections.Select(c => c.CollectionId).ToHashSet();
            }

            CollectionSelector.Initialize(collections, selectedIds);

            await BookList.InitializeAsync(ct);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load settings from database; proceeding with defaults");
        }
    }

    [RelayCommand]
    private void ReopenBatchWindow()
    {
        _windowService.OpenBatchQueueWindow();
    }

    [RelayCommand]
    private async Task CatalogByIsbnAsync()
    {
        try
        {
            await _windowService.ShowLookupWizardDialogAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to open Catalog by ISBN wizard");
        }
    }

    [RelayCommand]
    private async Task ShowAboutAsync()
    {
        try
        {
            await AppDialogs.ShowAboutDialogAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to show About dialog");
        }
    }

    [RelayCommand]
    private void RecatalogSelected() => BookList.RecatalogSelectedCommand.Execute(null);


    [RelayCommand]
    private async Task RecatalogAll()
    {
        try
        {
            var allBooks = BookList.Books.Select(b => b.BookId).ToList();
            if (allBooks.Count == 0) return;

            var confirmed = await _windowService.ShowDeleteConfirmationAsync(
                string.Format(Localization.Resources.RecatalogAll_Confirm, allBooks.Count));
            if (confirmed != true) return;

            await _windowService.StartBatchRecatalogAsync(allBooks);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to start re-catalog for all books");
        }
    }

    [RelayCommand]
    private async Task ImportFromReaderware()
    {
        await _windowService.ShowImportWizardAsync();
    }

    [RelayCommand]
    private async Task ImportFromReaderwareDatabase()
    {
        await _windowService.ShowReaderwareDbImportDialogAsync();
    }

    [RelayCommand]
    private async Task OpenManageLookupsAsync()
    {
        await _windowService.ShowManageLookupsAsync();
    }

    [RelayCommand]
    private async Task OpenManageBorrowersAsync()
    {
        await _windowService.ShowManageBorrowersAsync();
    }

    [RelayCommand]
    private async Task OpenSettingsAsync()
    {
        try
        {
            await _windowService.ShowSettingsAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to open Settings");
        }
    }

    [RelayCommand]
    private void OpenStatistics()
    {
        _windowService.OpenStatisticsWindow();
    }

    [RelayCommand]
    private void OpenHelpKeyboardShortcuts() =>
        _windowService.OpenHelpWindow(HelpTab.KeyboardShortcuts);

    [RelayCommand]
    private void OpenHelpFieldGlossary() =>
        _windowService.OpenHelpWindow(HelpTab.FieldGlossary);

    [RelayCommand]
    private void OpenHelpImportGuide() =>
        _windowService.OpenHelpWindow(HelpTab.ImportGuide);

    [RelayCommand]
    private void OpenHelpDataSources() =>
        _windowService.OpenHelpWindow(HelpTab.DataSources);

    [RelayCommand]
    private async Task BackupSqliteAsync()
    {
        await BackupCoreAsync(
            Localization.Resources.Backup_Header_Sqlite,
            "Backup (SQLite)",
            _backupService.GetCandidateSqlitePath,
            (folder, progress, fileName) => _backupService.BackupSqliteAsync(folder, progress: progress, explicitFileName: fileName));
    }

    [RelayCommand]
    private async Task BackupCsvArchiveAsync()
    {
        await BackupCoreAsync(
            Localization.Resources.Backup_Header_CsvArchive,
            "Backup (CSV Archive)",
            _backupService.GetCandidateCsvArchivePath,
            (folder, progress, fileName) => _backupService.BackupCsvArchiveAsync(folder, progress: progress, explicitFileName: fileName));
    }

    private async Task BackupCoreAsync(
        string progressLabel,
        string logContext,
        Func<string, string> getCandidatePath,
        Func<string, IProgress<string>?, string?, Task<string>> performBackup)
    {
        try
        {
            var folder = await _settingsService.GetAsync("LastBackupFolder");
            if (string.IsNullOrEmpty(folder))
                folder = await _filePickerService.PickFolderAsync(Localization.Resources.FilePicker_ChooseBackupDestination);
            if (string.IsNullOrEmpty(folder))
                return;
            await _settingsService.SetAsync("LastBackupFolder", folder);

            var candidatePath = getCandidatePath(folder);
            string? explicitFileName = null;
            if (File.Exists(candidatePath))
            {
                var choice = await AppDialogs.ShowBackupConflictDialogAsync(candidatePath, getCandidatePath);
                if (choice == AppDialogs.BackupConflictChoice.Cancel) return;
                if (choice == AppDialogs.BackupConflictChoice.Overwrite)
                    explicitFileName = Path.GetFileName(candidatePath);
            }

            var (progressWindow, progress) = AppDialogs.ShowProgressWindow(progressLabel);
            string writtenPath;
            try { writtenPath = await performBackup(folder, progress, explicitFileName); }
            finally { progressWindow.Close(); }

            AppDialogs.ShowInfoDialog(string.Format(Localization.Resources.Backup_Saved, Path.GetFileName(writtenPath)));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "{Context} failed", logContext);
            AppDialogs.ShowInfoDialog(string.Format(Localization.Resources.Backup_Failed, ex.Message));
        }
    }

    [RelayCommand]
    private async Task RestoreAsync()
    {
        try
        {
            var backupZipPath = await _filePickerService.PickFileAsync(Localization.Resources.FilePicker_SelectBackupFile, [".zip"]);
            if (string.IsNullOrEmpty(backupZipPath))
                return;

            // Safety backup is mandatory — cannot be skipped
            var safetyConfirmed = await AppDialogs.ShowConfirmDialogAsync(
                Localization.Resources.Restore_Confirm_Title,
                Localization.Resources.Restore_Confirm_Body);
            if (safetyConfirmed != true)
                return;

            var safetyFolder = await _filePickerService.PickFolderAsync(Localization.Resources.FilePicker_ChooseSafetyBackupLocation);
            if (string.IsNullOrEmpty(safetyFolder))
                return;

            var safetyPath = Path.Combine(safetyFolder, "safety-backup.zip");

            var restoreConfirmed = await AppDialogs.ShowConfirmDialogAsync(
                Localization.Resources.Restore_Replace_Title,
                Localization.Resources.Restore_Replace_Body);
            if (restoreConfirmed != true)
                return;

            var (progressWindow, progress) = AppDialogs.ShowProgressWindow(Localization.Resources.Restore_Header);
            try { await _backupService.RestoreAsync(backupZipPath, safetyPath, progress: progress); }
            finally { progressWindow.Close(); }

            await AppDialogs.ShowConfirmDialogAsync(Localization.Resources.Restore_Complete_Title, Localization.Resources.Restore_Complete_Body);
            Environment.Exit(0);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Restore failed");
            AppDialogs.ShowInfoDialog(string.Format(Localization.Resources.Restore_Failed, ex.Message));
        }
    }

    [RelayCommand]
    private async Task ExportCsvAsync()
    {
        try
        {
            var selectedColumns = await _windowService.ShowCsvColumnPickerAsync(
                _csvExportService.AllColumnNames,
                _csvExportService.DefaultColumnNames);
            if (selectedColumns == null)
                return;

            var outputPath = await _filePickerService.SaveFileAsync(Localization.Resources.FilePicker_ExportBooks, "books-export.csv", [".csv"]);
            if (string.IsNullOrEmpty(outputPath))
                return;

            var (progressWindow, progress) = AppDialogs.ShowProgressWindow(Localization.Resources.Export_Header);
            try
            {
                await _csvExportService.ExportAsync(new BookDB.Logic.Services.CsvExportParameters(
                    OutputPath: outputPath,
                    SelectedColumns: selectedColumns,
                    CollectionIds: BookList.ActiveCollectionIds,
                    SearchBookIds: BookList.ActiveSearchBookIds,
                    FacetFilters: BookList.ActiveFacetFilters,
                    SortColumn: BookList.SortColumn,
                    SortAscending: BookList.SortAscending),
                    progress: progress);
            }
            finally { progressWindow.Close(); }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Export CSV failed");
        }
    }

    [RelayCommand]
    private async Task PrintListAsync()
    {
        try
        {
            var parameters = await _windowService.ShowPrintDialogAsync(
                collectionIds: BookList.ActiveCollectionIds,
                searchBookIds: BookList.ActiveSearchBookIds,
                facetFilters: BookList.ActiveFacetFilters,
                sortColumn: BookList.SortColumn,
                sortAscending: BookList.SortAscending,
                bookCount: BookList.FilteredTotal);

            // null means user cancelled — PrintDialogViewModel already called GenerateAsync
            // and opened the viewer before returning non-null parameters.
            if (parameters == null)
                return;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Print list failed");
            AppDialogs.ShowInfoDialog(string.Format(Localization.Resources.PrintList_Failed, ex.Message));
        }
    }

    [RelayCommand]
    private void Exit()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
    }

    public async Task<bool> ConfirmShutdownAsync()
    {
        if (!IsBatchQueueRunning) return true;

        var result = await _windowService.ShowMainShutdownWarningAsync();
        return result == true;
    }

    public async Task PersistSettingsAsync(CancellationToken ct = default)
    {
        await _settingsService.SetAsync("FilterPanelWidth", FilterPanelWidth.ToString(CultureInfo.InvariantCulture), ct);
        await _settingsService.SetAsync("DetailPanelWidth", DetailPanelWidth.ToString(CultureInfo.InvariantCulture), ct);
        await _settingsService.SetAsync("DetailPanelVisible", DetailPanelVisible.ToString(), ct);
        await _settingsService.SetAsync("WindowWidth", WindowWidth.ToString(CultureInfo.InvariantCulture), ct);
        await _settingsService.SetAsync("WindowHeight", WindowHeight.ToString(CultureInfo.InvariantCulture), ct);
        await _settingsService.SetAsync("WindowLeft", WindowLeft.ToString(CultureInfo.InvariantCulture), ct);
        await _settingsService.SetAsync("WindowTop", WindowTop.ToString(CultureInfo.InvariantCulture), ct);

        await BookList.PersistColumnStateAsync(ct);

        var selectedIds = string.Join(",",
            CollectionSelector.CollectionItems
                .Where(c => c.IsSelected)
                .Select(c => c.Id));
        await _settingsService.SetAsync("LastSelectedCollectionIds", selectedIds, ct);
    }
    
    private async Task<double> LoadDoubleSettingAsync(string key, double fallback, CancellationToken ct, double min = double.NegativeInfinity)
    {
        var str = await _settingsService.GetAsync(key, ct);
        if (str != null && double.TryParse(str, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && value >= min)
            return value;
        return fallback;
    }
}
