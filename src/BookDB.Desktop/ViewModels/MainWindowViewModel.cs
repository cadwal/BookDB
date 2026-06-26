using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using BookDB.Desktop.Helpers;
using BookDB.Desktop.Messages;
using BookDB.Desktop.Services;
using BookDB.Data.Interfaces;
using BookDB.Data.PostgreSQL;
using BookDB.Help;
using BookDB.Logic.Services;
using BookDB.Models;
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
    private readonly IApplicationRestartService _restartService;
    private readonly IConnectionHealthMonitor _connectionMonitor;
    private readonly IConnectionFailureClassifier _connectionClassifier;
    private readonly ICsvArchiveRestoreService _csvRestore;
    private readonly IBootstrapConfigService _bootstrapConfig;
    private readonly AppSettings _appSettings;
    private readonly IMigrationTargetBuilder _targetBuilder;
    private readonly ISecretStore _secretStore;

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

    /// <summary>Status-bar indicator for a mid-session connection loss; empty and hidden while healthy.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowConnectionStatus))]
    private string _connectionStatusText = string.Empty;

    // Drive the status-bar pill's appearance: amber + pulsing while retrying, red + steady once lost.
    [ObservableProperty]
    private bool _isConnectionDegraded;

    [ObservableProperty]
    private bool _isConnectionLost;

    public bool ShowConnectionStatus => !string.IsNullOrEmpty(ConnectionStatusText);

    private void UpdateConnectionStatus()
    {
        var state = _connectionMonitor.State;
        IsConnectionDegraded = state == ConnectionHealth.Degraded;
        IsConnectionLost = state == ConnectionHealth.Lost;
        ConnectionStatusText = state switch
        {
            ConnectionHealth.Degraded => Localization.Resources.StatusBar_Connection_Reconnecting,
            ConnectionHealth.Lost => Localization.Resources.StatusBar_Connection_Lost,
            _ => string.Empty,
        };
    }

    private async Task HandleConnectionEscalationAsync()
    {
        if (await _windowService.ShowConnectionLostEscalationDialogAsync())
            Exit();
    }

    // A read-heavy operation (backup, export, print) that fails on a dropped remote connection drives the
    // shared status-bar indicator; the monitor then retries in the background. Returns true when the failure
    // was a connection loss so the caller can skip its generic error dialog.
    private bool ReportIfConnectionLoss(Exception ex) =>
        _connectionMonitor.ReportIfConnectionLoss(_connectionClassifier, ex);

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
        IPrintService printService,
        IApplicationRestartService restartService,
        IConnectionHealthMonitor connectionMonitor,
        IConnectionFailureClassifier connectionClassifier,
        ICsvArchiveRestoreService csvRestore,
        IBootstrapConfigService bootstrapConfig,
        AppSettings appSettings,
        IMigrationTargetBuilder targetBuilder,
        ISecretStore secretStore)
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
        _restartService = restartService;
        _connectionMonitor = connectionMonitor;
        _connectionClassifier = connectionClassifier;
        _csvRestore = csvRestore;
        _bootstrapConfig = bootstrapConfig;
        _appSettings = appSettings;
        _targetBuilder = targetBuilder;
        _secretStore = secretStore;
        _connectionMonitor.StateChanged += (_, _) => Dispatcher.UIThread.Post(UpdateConnectionStatus);
        _connectionMonitor.Reconnected += (_, _) => Dispatcher.UIThread.Post(() => _ = BookList.LoadBooksAsync());
        _connectionMonitor.Escalated += (_, _) => Dispatcher.UIThread.Post(() => _ = HandleConnectionEscalationAsync());

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
    private async Task OpenStatistics()
    {
        await _windowService.OpenStatisticsWindowAsync();
    }

    [RelayCommand]
    private async Task OpenMaintenanceAsync()
    {
        try
        {
            await _windowService.ShowMaintenanceDialogAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to open Maintenance");
        }
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
    private async Task BackupAsync()
    {
        // One entry point mirroring the single Restore item: pick the format (defaulting to the configured
        // auto-backup format), then branch with the same capability fallback the auto-backup uses — a remote
        // backend has no file backup, so the SQLite choice resolves to the CSV archive.
        var configFormat = await _settingsService.GetAsync("AutoBackup.Format") ?? BackupFormatDialogViewModel.SqliteFormat;
        var defaultFolder = await _settingsService.GetAsync("LastBackupFolder") ?? string.Empty;
        var chosen = await _windowService.ShowBackupFormatDialogAsync(_backupService.SupportsFileBackup, configFormat, defaultFolder);
        if (chosen is null)
            return;
        var (format, folder) = chosen.Value;
        await _settingsService.SetAsync("LastBackupFolder", folder);

        if (format == BackupFormatDialogViewModel.CsvFormat || !_backupService.SupportsFileBackup)
            await BackupCoreAsync(
                folder,
                Localization.Resources.Backup_Header_CsvArchive,
                "Backup (CSV Archive)",
                _backupService.GetCandidateCsvArchivePath,
                (f, progress, fileName) => _backupService.BackupCsvArchiveAsync(f, progress: progress, explicitFileName: fileName));
        else
            await BackupCoreAsync(
                folder,
                Localization.Resources.Backup_Header_Sqlite,
                "Backup (SQLite)",
                _backupService.GetCandidateSqlitePath,
                (f, progress, fileName) => _backupService.BackupSqliteAsync(f, progress: progress, explicitFileName: fileName));
    }

    private async Task BackupCoreAsync(
        string folder,
        string progressLabel,
        string logContext,
        Func<string, string> getCandidatePath,
        Func<string, IProgress<string>?, string?, Task<string>> performBackup)
    {
        try
        {
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
            // Run off the UI thread so the progress window can paint: the SQLite backup does its WAL-flush /
            // file-copy / zip synchronously, which would otherwise block the UI thread and leave the just-shown
            // window unpainted (transparent) until it closes. Progress reports marshal back via Progress<T>.
            try { writtenPath = await Task.Run(() => performBackup(folder, progress, explicitFileName)); }
            finally { progressWindow.Close(); }

            AppDialogs.ShowInfoDialog(string.Format(Localization.Resources.Backup_Saved, Path.GetFileName(writtenPath)));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "{Context} failed", logContext);
            // A connection loss is surfaced on the status indicator; otherwise show the backup-failed dialog.
            if (!ReportIfConnectionLoss(ex))
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

            var kind = RestoreArchiveInspector.Detect(backupZipPath);
            if (kind == RestoreArchiveKind.Unknown)
            {
                AppDialogs.ShowInfoDialog(Localization.Resources.Restore_UnknownFormat);
                return;
            }

            // Safety backup is mandatory — cannot be skipped.
            var safetyConfirmed = await AppDialogs.ShowConfirmDialogAsync(
                Localization.Resources.Restore_Confirm_Title,
                Localization.Resources.Restore_Confirm_Body);
            if (safetyConfirmed != true)
                return;

            var safetyFolder = await _filePickerService.PickFolderAsync(Localization.Resources.FilePicker_ChooseSafetyBackupLocation);
            if (string.IsNullOrEmpty(safetyFolder))
                return;

            if (kind == RestoreArchiveKind.SqliteFile)
                await RestoreSqliteFileAsync(backupZipPath, safetyFolder);
            else
                await RestoreCsvArchiveAsync(backupZipPath, safetyFolder);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Restore failed");
            AppDialogs.ShowInfoDialog(string.Format(Localization.Resources.Restore_Failed, ex.Message));
        }
    }

    // A SQLite file backup replaces the local database file; it cannot be applied to a remote backend.
    private async Task RestoreSqliteFileAsync(string backupZipPath, string safetyFolder)
    {
        if (_appSettings.Backend != DatabaseBackend.Sqlite)
        {
            AppDialogs.ShowInfoDialog(Localization.Resources.Restore_SqliteIntoRemote);
            return;
        }

        var restoreConfirmed = await AppDialogs.ShowConfirmDialogAsync(
            Localization.Resources.Restore_Replace_Title,
            Localization.Resources.Restore_Replace_Body);
        if (restoreConfirmed != true)
            return;

        var safetyPath = Path.Combine(safetyFolder, "safety-backup.zip");
        var (progressWindow, progress) = AppDialogs.ShowProgressWindow(Localization.Resources.Restore_Header);
        try { await _backupService.RestoreAsync(backupZipPath, safetyPath, progress: progress); }
        finally { progressWindow.Close(); }

        await AppDialogs.ShowConfirmDialogAsync(Localization.Resources.Restore_Complete_Title, Localization.Resources.Restore_Complete_Body);
        _restartService.Restart();
    }

    // A CSV archive is imported into the active backend, or — when the backup names a different PostgreSQL server —
    // directly into that server, so the restored data lands where the backup's connection points.
    private async Task RestoreCsvArchiveAsync(string backupZipPath, string safetyFolder)
    {
        var archivedConfig = RestoreArchiveInspector.ReadConfig(backupZipPath);

        IMigrationTarget? directTarget = null;
        if (TryDescribeArchivedPostgres(archivedConfig, out var serverDescription, out var options))
        {
            var choice = await AppDialogs.ShowRestoreTargetDialogAsync(serverDescription);
            if (choice == AppDialogs.RestoreTargetChoice.Cancel)
                return;
            if (choice == AppDialogs.RestoreTargetChoice.Archived)
            {
                var password = _secretStore.Get(options!.AccountKey);
                if (string.IsNullOrEmpty(password))
                {
                    AppDialogs.ShowInfoDialog(Localization.Resources.Restore_NoCredentialsForTarget);
                    return;
                }
                directTarget = await _targetBuilder.BuildAsync(
                    DatabaseBackend.PostgreSql, PostgresConnectionStringFactory.Build(options, password));
            }
        }

        try
        {
            // A restore replaces the current library (the archive's preserved keys can't merge onto existing
            // rows); combining libraries is the Import feature's job.
            var confirmReplace = await AppDialogs.ShowConfirmDialogAsync(
                Localization.Resources.Restore_Replace_Title, Localization.Resources.Restore_Replace_Body);
            if (confirmReplace != true)
                return;

            var (progressWindow, progress) = AppDialogs.ShowProgressWindow(Localization.Resources.Restore_Header);
            RestoreResult result;
            try
            {
                var migrationProgress = new Progress<MigrationProgress>(p => progress.Report(DescribeRestoreProgress(p)));
                var target = directTarget is null ? null
                    : new RestoreTargetServices(directTarget.Factory, directTarget.Resync, directTarget.Backup);
                result = await _csvRestore.RestoreAsync(
                    backupZipPath, safetyFolder, progress: migrationProgress, target: target);
            }
            finally { progressWindow.Close(); }

            if (result.Data.Outcome != MigrationOutcome.Completed)
            {
                AppDialogs.ShowInfoDialog(string.Format(Localization.Resources.Restore_Failed, result.Data.ErrorMessage ?? string.Empty));
                return;
            }

            var confirm = new RestoreConfirmationViewModel(archivedConfig ?? new BootstrapConfig(), _bootstrapConfig, _restartService);
            if (directTarget is not null)
            {
                // The data was restored into the archived server — make it the active database and restart.
                await AppDialogs.ShowConfirmDialogAsync(
                    Localization.Resources.Restore_Complete_Title, Localization.Resources.Restore_Complete_Body);
                confirm.ApplyCommand.Execute(null);
                return;
            }

            // Active-backend restore: apply preference keys, and offer (never auto-apply) the archive's backend.
            if (archivedConfig is not null && confirm.HasBackendChange)
            {
                var adopt = await AppDialogs.ShowConfirmDialogAsync(
                    Localization.Resources.Restore_AdoptBackend_Title,
                    string.Format(Localization.Resources.Restore_AdoptBackend_Body, confirm.ArchivedBackendName));
                if (adopt == true)
                    confirm.ApplyCommand.Execute(null);
                else
                    confirm.KeepCurrentCommand.Execute(null);
            }
            else
            {
                // No backend change: still restart to load the restored data, but tell the user first
                // instead of restarting silently.
                await AppDialogs.ShowConfirmDialogAsync(
                    Localization.Resources.Restore_Complete_Title, Localization.Resources.Restore_Complete_Body);
                confirm.KeepCurrentCommand.Execute(null);
            }
        }
        finally
        {
            if (directTarget is not null)
                await directTarget.DisposeAsync();
        }
    }

    // The backup can be restored into the server it came from when its config names a PostgreSQL host that is not
    // already the active database.
    private bool TryDescribeArchivedPostgres(BootstrapConfig? config, out string description, out PostgresOptions? options)
    {
        description = string.Empty;
        options = null;
        if (config is null
            || !Enum.TryParse(config.Backend, ignoreCase: true, out DatabaseBackend backend)
            || backend != DatabaseBackend.PostgreSql
            || string.IsNullOrWhiteSpace(config.Postgres.Host))
            return false;

        var current = _bootstrapConfig.Load();
        var currentIsSameServer =
            Enum.TryParse(current.Backend, ignoreCase: true, out DatabaseBackend currentBackend)
            && currentBackend == DatabaseBackend.PostgreSql
            && current.Postgres.AccountKey == config.Postgres.AccountKey;
        if (currentIsSameServer)
            return false;

        options = config.Postgres;
        description = $"{config.Postgres.Host}/{config.Postgres.Database}";
        return true;
    }

    private static string DescribeRestoreProgress(MigrationProgress p)
    {
        // Show a running count while a table imports (and per-batch for images), and its final tally when it
        // completes, so the window keeps moving instead of only flashing completed tables.
        if (p.Phase == MigrationPhase.Copying && p.Table is { } table && Localization.MigrationText.TryDescribe(table, out var label))
            return p.Total > 0 && p.Copied < p.Total
                ? string.Format(Localization.Resources.MoveLibrary_Progress_TableRunning, label, p.Copied, p.Total)
                : string.Format(Localization.Resources.MoveLibrary_Progress_Table, label, p.Total);
        return Localization.MigrationText.Describe(p.Phase);
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
            ReportIfConnectionLoss(ex);
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
            if (!ReportIfConnectionLoss(ex))
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
