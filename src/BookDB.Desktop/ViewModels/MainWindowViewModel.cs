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
using BookDB.Desktop.Services.UpdateCheck;
using BookDB.Data.Interfaces;
using BookDB.Data.MySql;
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
    private readonly IReleaseNotesService _releaseNotesService;
    private readonly IUpdateCheckService _updateCheckService;

    private const string LastSeenReleaseNotesVersionKey = "ReleaseNotes.LastSeenVersion";

    // Set by the weekly update check when a newer version is available; consumed by the status-bar hint.
    private InstallChannel _updateChannel;
    private string _latestVersionText = string.Empty;
    private string _currentVersionText = string.Empty;

    [ObservableProperty]
    private bool _showUpdateAvailable;

    public string UpdateAvailableText => Localization.Resources.StatusBar_UpdateAvailable;

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

    // Status-bar storage-kind indicator (fixed for the app's lifetime — changing backend restarts the app).
    // The popup shows connection facts from the bootstrap config; credentials never appear.
    public bool IsRemoteStorage { get; }
    public string StorageBackendName { get; } = string.Empty;
    public string StorageLocationLabel { get; } = string.Empty;
    public string StorageLocationValue { get; } = string.Empty;
    public string StorageDatabaseName { get; } = string.Empty;
    public string StorageUserName { get; } = string.Empty;

    /// <summary>Popup connection-state row for a remote backend; mirrors the health monitor.</summary>
    [ObservableProperty]
    private string _storageConnectionStateText = string.Empty;

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
        StorageConnectionStateText = state switch
        {
            ConnectionHealth.Degraded => Localization.Resources.StatusBar_Connection_Reconnecting,
            ConnectionHealth.Lost => Localization.Resources.StatusBar_Connection_Lost,
            _ => Localization.Resources.StatusBar_Storage_State_Connected,
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

    // Disabled placeholder shown when no secondary windows are open; instance-level so its localized
    // label reflects the culture active when the window is built.
    private readonly OpenWindowEntry _noWindowsSentinel =
        new(Localization.Resources.Menu_Window_NoOpenWindows, new RelayCommand(() => { }, () => false));

    private static readonly OpenWindowEntry _categorySeparator = OpenWindowEntry.CreateSeparator();

    /// <summary>
    /// The currently open secondary windows shown in the _Window menu, grouped BookEdit-then-Utility with a
    /// single separator between the groups. WindowService calls AddOpenWindow/RemoveOpenWindow to keep this
    /// up to date; the sentinel entry is shown when no secondary windows are open.
    /// </summary>
    public ObservableCollection<OpenWindowEntry> OpenWindowEntries { get; } = [];

    public void AddOpenWindow(OpenWindowEntry entry)
    {
        var windows = LiveWindowEntries();
        windows.Add(entry);
        RebuildWindowMenu(windows);
    }

    public void RemoveOpenWindow(OpenWindowEntry entry)
    {
        var windows = LiveWindowEntries();
        windows.Remove(entry);
        RebuildWindowMenu(windows);
    }

    // The real window entries currently listed, in order, without the sentinel/separator chrome.
    private List<OpenWindowEntry> LiveWindowEntries() =>
        OpenWindowEntries.Where(e => !e.IsSeparator && !ReferenceEquals(e, _noWindowsSentinel)).ToList();

    // Rebuilds the menu so BookEdit windows always precede Utility windows with a single separator between
    // the two groups, independent of the order windows opened and closed in. Within-group order is preserved.
    private void RebuildWindowMenu(List<OpenWindowEntry> windows)
    {
        OpenWindowEntries.Clear();
        if (windows.Count == 0)
        {
            OpenWindowEntries.Add(_noWindowsSentinel);
            return;
        }

        var bookEdit = windows.Where(e => e.Category == WindowCategory.BookEdit).ToList();
        var utility = windows.Where(e => e.Category == WindowCategory.Utility).ToList();
        foreach (var e in bookEdit)
            OpenWindowEntries.Add(e);
        if (bookEdit.Count > 0 && utility.Count > 0)
            OpenWindowEntries.Add(_categorySeparator);
        foreach (var e in utility)
            OpenWindowEntries.Add(e);
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
        ISecretStore secretStore,
        IReleaseNotesService releaseNotesService,
        IUpdateCheckService updateCheckService)
    {
        OpenWindowEntries.Add(_noWindowsSentinel);
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
        _releaseNotesService = releaseNotesService;
        _updateCheckService = updateCheckService;

        IsRemoteStorage = _appSettings.Backend.IsRemote();
        // Load() never returns null in production; substituted test doubles may, so fall back to defaults.
        var storageConfig = _bootstrapConfig.Load() ?? new BootstrapConfig();
        switch (_appSettings.Backend)
        {
            case DatabaseBackend.PostgreSql:
                StorageBackendName = Localization.Resources.Settings_Database_Backend_Postgres;
                StorageLocationLabel = Localization.Resources.StatusBar_Storage_Host_Label;
                StorageLocationValue = $"{storageConfig.Postgres.Host}:{storageConfig.Postgres.Port}";
                StorageDatabaseName = storageConfig.Postgres.Database;
                StorageUserName = storageConfig.Postgres.Username;
                break;
            case DatabaseBackend.MySql:
                StorageBackendName = Localization.Resources.Settings_Database_Backend_MySql;
                StorageLocationLabel = Localization.Resources.StatusBar_Storage_Host_Label;
                StorageLocationValue = $"{storageConfig.MySql.Host}:{storageConfig.MySql.Port}";
                StorageDatabaseName = storageConfig.MySql.Database;
                StorageUserName = storageConfig.MySql.Username;
                break;
            default:
                StorageBackendName = Localization.Resources.Settings_Database_Backend_Sqlite;
                StorageLocationLabel = Localization.Resources.StatusBar_Storage_File_Label;
                StorageLocationValue = _appSettings.SqliteLibraryPath ?? string.Empty;
                break;
        }
        UpdateConnectionStatus();

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
            BatchQueueStatusText = ComposeBatchStatusText(
                msg.IsRunning, IsBatchWindowMinimized, msg.Current, msg.Total,
                msg.ToReviewCount, msg.FailedCount);
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

    /// <summary>
    /// Status-bar batch text: progress while running (or the completion header once minimized),
    /// plus the running outcome — items routed to review and failures — so a batch can be
    /// followed and judged without opening its window. Outcome parts appear only when non-zero.
    /// </summary>
    internal static string ComposeBatchStatusText(
        bool isRunning, bool isMinimized, int current, int total, int toReview, int failed)
    {
        string baseText;
        if (isRunning)
            baseText = string.Format(Localization.Resources.StatusBar_BatchQueue_Progress, current, total);
        else if (isMinimized)
            baseText = Localization.Resources.BatchQueue_CompleteHeader;
        else
            return string.Empty;

        var toReviewText = toReview > 0
            ? string.Format(Localization.Resources.StatusBar_BatchQueue_ToReview, toReview)
            : null;
        var failedText = failed > 0
            ? string.Format(Localization.Resources.StatusBar_BatchQueue_Failed, failed)
            : null;

        var outcome = (toReviewText, failedText) switch
        {
            (null, null) => null,
            ({ } review, null) => review,
            (null, { } fail) => fail,
            ({ } review, { } fail) => string.Format(
                Localization.Resources.StatusBar_BatchQueue_OutcomePair, review, fail)
        };

        return outcome is null
            ? baseText
            : string.Format(Localization.Resources.StatusBar_BatchQueue_WithOutcome, baseText, outcome);
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
            // The Uncategorized sentinel is a valid selection even though it is not a real collection id.
            var selectedIds = CollectionSelector.CollectionItems
                .Where(i => i.IsSelected && (existingIds.Contains(i.Id) || i.Id == CollectionFilter.Uncategorized))
                .Select(i => i.Id)
                .ToHashSet();
            if (selectedIds.Count == 0)
                selectedIds = existingIds.Append(CollectionFilter.Uncategorized).ToHashSet();
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
            System.Collections.Generic.HashSet<int> selectedIds;
            if (selectedIdsStr != null)
            {
                // Keep positive collection ids and the Uncategorized sentinel; drop parse failures and junk.
                selectedIds = selectedIdsStr
                    .Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => int.TryParse(s, out var id) ? (int?)id : null)
                    .Where(id => id is > 0 or CollectionFilter.Uncategorized)
                    .Select(id => id!.Value)
                    .ToHashSet();
            }
            else
            {
                // First run: all collections selected, plus Uncategorized so any orphaned books stay visible.
                selectedIds = collections.Select(c => c.CollectionId)
                    .Append(CollectionFilter.Uncategorized)
                    .ToHashSet();
            }

            // One-time upgrade: a selection persisted before the Uncategorized filter existed has no sentinel,
            // so orphaned books would be hidden. Seed it selected exactly once; after that the user's own
            // choice (including deselecting it) is respected because the flag is set.
            var seeded = await _settingsService.GetAsync("UncategorizedFilterSeeded", ct);
            if (!bool.TryParse(seeded, out var wasSeeded) || !wasSeeded)
            {
                selectedIds.Add(CollectionFilter.Uncategorized);
                await _settingsService.SetAsync("UncategorizedFilterSeeded", "true", ct);
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
            await _windowService.ShowAboutAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to show About dialog");
        }
    }

    /// <summary>
    /// First main-window open after an update: offers the "what's new" notes once per version.
    /// Yes and Skip both record the version as seen; Esc/X/Cancel defers to the next start. A fresh
    /// install seeds the version silently, and a version without notes never prompts.
    /// </summary>
    [RelayCommand]
    private async Task OfferReleaseNotesAsync()
    {
        try
        {
            var current = _releaseNotesService.CurrentVersion;
            var lastSeen = await _settingsService.GetAsync(LastSeenReleaseNotesVersionKey);
            if (lastSeen == current) return;
            if (string.IsNullOrEmpty(lastSeen))
            {
                await _settingsService.SetAsync(LastSeenReleaseNotesVersionKey, current);
                return;
            }

            var notes = _releaseNotesService.GetNotes(current);
            if (notes is null) return;

            var choice = await _windowService.ShowReleaseNotesPromptAsync(current);
            if (choice == ReleaseNotesChoice.Defer) return;

            await _settingsService.SetAsync(LastSeenReleaseNotesVersionKey, current);
            if (choice == ReleaseNotesChoice.Show)
                await _windowService.ShowReleaseNotesAsync(current, notes);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to offer release notes");
        }
    }

    [RelayCommand]
    private async Task CheckForUpdatesAsync()
    {
        try
        {
            var status = await _updateCheckService.CheckAsync();
            if (status.IsUpdateAvailable && status.Latest is { } latest)
            {
                _updateChannel = status.Channel;
                _latestVersionText = latest.ToString();
                _currentVersionText = status.Current.ToString();
                ShowUpdateAvailable = true;
            }
            else
            {
                // Cleared immediately once the running version has caught up — no manual dismiss.
                ShowUpdateAvailable = false;
            }
        }
        catch (Exception ex)
        {
            // A failed update check never surfaces to the user.
            Log.Error(ex, "Update check failed");
        }
    }

    [RelayCommand]
    private async Task OpenUpdateHintAsync()
    {
        if (!ShowUpdateAvailable) return;
        await _windowService.ShowUpdateHintAsync(_updateChannel, _latestVersionText, _currentVersionText);
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

            await BookList.RecatalogAllAsync();
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
    private void OpenHelpGettingStarted() =>
        _windowService.OpenHelpWindow(HelpTab.GettingStarted);

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
    private void OpenHelpRemoteDatabases() =>
        _windowService.OpenHelpWindow(HelpTab.RemoteDatabases);

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
                (f, progress, fileName) => _backupService.BackupCsvArchiveAsync(f, progress: BackupProgressLocalizer.Localizing(progress), explicitFileName: fileName));
        else
            await BackupCoreAsync(
                folder,
                Localization.Resources.Backup_Header_Sqlite,
                "Backup (SQLite)",
                _backupService.GetCandidateSqlitePath,
                (f, progress, fileName) => _backupService.BackupSqliteAsync(f, progress: BackupProgressLocalizer.Localizing(progress), explicitFileName: fileName));
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
                var choice = await _windowService.ShowBackupConflictAsync(candidatePath);
                if (choice == BackupConflictChoice.Cancel) return;
                if (choice == BackupConflictChoice.Overwrite)
                    explicitFileName = Path.GetFileName(candidatePath);
            }

            var progress = _windowService.ShowProgressWindow(progressLabel);
            string writtenPath;
            // Run off the UI thread so the progress window can paint: the SQLite backup does its WAL-flush /
            // file-copy / zip synchronously, which would otherwise block the UI thread and leave the just-shown
            // window unpainted (transparent) until it closes. Progress reports marshal back via Progress<T>.
            try { writtenPath = await Task.Run(() => performBackup(folder, progress, explicitFileName)); }
            finally { progress.Close(); }

            _ = _windowService.ShowInfoAsync(string.Format(Localization.Resources.Backup_Saved, Path.GetFileName(writtenPath)));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "{Context} failed", logContext);
            // A connection loss is surfaced on the status indicator; otherwise show the backup-failed dialog.
            if (!ReportIfConnectionLoss(ex))
                _ = _windowService.ShowInfoAsync(string.Format(Localization.Resources.Backup_Failed, ex.Message));
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
                _ = _windowService.ShowInfoAsync(Localization.Resources.Restore_UnknownFormat);
                return;
            }

            // Safety backup is mandatory — cannot be skipped.
            var safetyConfirmed = await _windowService.ShowConfirmAsync(
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
            _ = _windowService.ShowInfoAsync(string.Format(Localization.Resources.Restore_Failed, ex.Message));
        }
    }

    // A SQLite file backup replaces the local database file; it cannot be applied to a remote backend.
    private async Task RestoreSqliteFileAsync(string backupZipPath, string safetyFolder)
    {
        if (_appSettings.Backend != DatabaseBackend.Sqlite)
        {
            _ = _windowService.ShowInfoAsync(Localization.Resources.Restore_SqliteIntoRemote);
            return;
        }

        var restoreConfirmed = await _windowService.ShowConfirmAsync(
            Localization.Resources.Restore_Replace_Title,
            Localization.Resources.Restore_Replace_Body);
        if (restoreConfirmed != true)
            return;

        var progress = _windowService.ShowProgressWindow(Localization.Resources.Restore_Header);
        try { await _backupService.RestoreAsync(backupZipPath, safetyFolder, progress: BackupProgressLocalizer.Localizing(progress)); }
        finally { progress.Close(); }

        await _windowService.ShowConfirmAsync(Localization.Resources.Restore_Complete_Title, Localization.Resources.Restore_Complete_Body);
        _restartService.Restart();
    }

    // A CSV archive is imported into the active backend, or — when the backup names a different PostgreSQL server —
    // directly into that server, so the restored data lands where the backup's connection points.
    private async Task RestoreCsvArchiveAsync(string backupZipPath, string safetyFolder)
    {
        var archivedConfig = RestoreArchiveInspector.ReadConfig(backupZipPath);

        IMigrationTarget? directTarget = null;
        if (DescribeArchivedRemoteTarget(archivedConfig) is { } archivedTarget)
        {
            var choice = await _windowService.ShowRestoreTargetAsync(archivedTarget.Description);
            if (choice == RestoreTargetChoice.Cancel)
                return;
            if (choice == RestoreTargetChoice.Archived)
            {
                var password = _secretStore.Get(archivedTarget.AccountKey);
                if (string.IsNullOrEmpty(password))
                {
                    _ = _windowService.ShowInfoAsync(Localization.Resources.Restore_NoCredentialsForTarget);
                    return;
                }
                directTarget = await _targetBuilder.BuildAsync(
                    archivedTarget.Backend, archivedTarget.ConnectionStringFor(password));
            }
        }

        try
        {
            // A restore replaces the current library (the archive's preserved keys can't merge onto existing
            // rows); combining libraries is the Import feature's job.
            var confirmReplace = await _windowService.ShowConfirmAsync(
                Localization.Resources.Restore_Replace_Title, Localization.Resources.Restore_Replace_Body);
            if (confirmReplace != true)
                return;

            var progress = _windowService.ShowProgressWindow(Localization.Resources.Restore_Header);
            RestoreResult result;
            try
            {
                var migrationProgress = new Progress<MigrationProgress>(p => progress.Report(DescribeRestoreProgress(p)));
                var target = directTarget is null ? null
                    : new RestoreTargetServices(directTarget.Factory, directTarget.Resync, directTarget.Backup);
                result = await _csvRestore.RestoreAsync(
                    backupZipPath, safetyFolder, progress: migrationProgress, target: target);
            }
            finally { progress.Close(); }

            if (result.Data.Outcome != MigrationOutcome.Completed)
            {
                _ = _windowService.ShowInfoAsync(string.Format(Localization.Resources.Restore_Failed, result.Data.ErrorMessage ?? string.Empty));
                return;
            }

            var confirm = new RestoreConfirmationViewModel(archivedConfig ?? new BootstrapConfig(), _bootstrapConfig, _restartService);
            if (directTarget is not null)
            {
                // The data was restored into the archived server — make it the active database and restart.
                await _windowService.ShowConfirmAsync(
                    Localization.Resources.Restore_Complete_Title, Localization.Resources.Restore_Complete_Body);
                confirm.ApplyCommand.Execute(null);
                return;
            }

            // Active-backend restore: apply preference keys, and offer (never auto-apply) the archive's backend.
            if (archivedConfig is not null && confirm.HasBackendChange)
            {
                var adopt = await _windowService.ShowConfirmAsync(
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
                await _windowService.ShowConfirmAsync(
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

    // A remote server the archive came from, offered as an alternative restore destination. The password is fetched
    // lazily so the connection string is only assembled once the user has chosen the archived server.
    private sealed record ArchivedRestoreTarget(
        DatabaseBackend Backend, string Description, string AccountKey, Func<string, string> ConnectionStringFor);

    // The backup can be restored into the server it came from when its config names a remote host that is not already
    // the active database.
    private ArchivedRestoreTarget? DescribeArchivedRemoteTarget(BootstrapConfig? config)
    {
        if (config is null || !Enum.TryParse(config.Backend, ignoreCase: true, out DatabaseBackend backend))
            return null;

        var current = _bootstrapConfig.Load();
        Enum.TryParse(current.Backend, ignoreCase: true, out DatabaseBackend currentBackend);

        switch (backend)
        {
            case DatabaseBackend.PostgreSql:
            {
                var options = config.Postgres;
                if (string.IsNullOrWhiteSpace(options.Host)
                    || (currentBackend == DatabaseBackend.PostgreSql && current.Postgres.AccountKey == options.AccountKey))
                    return null;
                return new ArchivedRestoreTarget(
                    DatabaseBackend.PostgreSql, $"{options.Host}/{options.Database}", options.AccountKey,
                    password => PostgresConnectionStringFactory.Build(options, password));
            }
            case DatabaseBackend.MySql:
            {
                var options = config.MySql;
                if (string.IsNullOrWhiteSpace(options.Host)
                    || (currentBackend == DatabaseBackend.MySql && current.MySql.AccountKey == options.AccountKey))
                    return null;
                return new ArchivedRestoreTarget(
                    DatabaseBackend.MySql, $"{options.Host}/{options.Database}", options.AccountKey,
                    password => MySqlConnectionStringFactory.Build(options, password));
            }
            default:
                return null;
        }
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

            var progress = _windowService.ShowProgressWindow(Localization.Resources.Export_Header);
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
                    progress: CsvExportProgressLocalizer.Localizing(progress));
            }
            finally { progress.Close(); }
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
                _ = _windowService.ShowInfoAsync(string.Format(Localization.Resources.PrintList_Failed, ex.Message));
        }
    }

    [RelayCommand]
    private void Exit()
    {
        // TryShutdown (not Shutdown) so the main window's close guard can veto — a forced
        // shutdown would silently drop unsaved edits.
        if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
            desktop.TryShutdown();
    }

    /// <summary>
    /// The aggregate shutdown confirmation: batch warning, then the inline editor's
    /// unsaved-changes prompt, then every guarded secondary window's own confirmation.
    /// Returns false if any guard refuses. On true, all secondary windows are already
    /// closed — the caller only has to complete the main window's close.
    /// </summary>
    public async Task<bool> ConfirmShutdownAsync()
    {
        if (IsBatchQueueRunning)
        {
            var result = await _windowService.ShowMainShutdownWarningAsync();
            if (result != true) return false;
        }

        if (!await BookDetail.TryNavigateAwayAsync()) return false;
        if (!await _windowService.ConfirmCloseGuardedWindowsAsync()) return false;

        // Close the satellites before the main close completes: the app exits with the main
        // window (ShutdownMode.OnMainWindowClose), so anything still open would be torn down.
        _windowService.CloseAllSecondaryWindows();
        return true;
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
