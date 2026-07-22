using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using BookDB.Data.Interfaces;
using BookDB.Desktop.Helpers;
using BookDB.Desktop.Services.UpdateCheck;
using BookDB.Desktop.ViewModels;
using BookDB.Desktop.Views;
using BookDB.Help;
using BookDB.Logic.Import;
using BookDB.Logic.Services;
using BookDB.Models.Entities;
using BookDB.Models.Metadata;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace BookDB.Desktop.Services;

/// <summary>
/// Concrete implementation of IWindowService. Shows modal dialogs and non-blocking windows.
/// </summary>
public sealed class WindowService : IWindowService
{
    private readonly IServiceProvider _serviceProvider;

    // Lazy MainWindow reference — resolved on first use after the visual tree is attached
    private MainWindow? _mainWindow;

    public WindowService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    private MainWindow GetMainWindow()
    {
        _mainWindow ??= _serviceProvider.GetRequiredService<MainWindow>();
        return _mainWindow;
    }

    private static Window? LiveMainWindow() =>
        Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lt
            ? lt.MainWindow
            : null;

    /// <summary>
    /// Single show path for every <see cref="MessageDialogSpec"/>-shaped dialog. The result is
    /// carried by a TaskCompletionSource rather than ShowDialog's return value so the ownerless
    /// startup path (no main window alive yet — shown non-modally) still awaits the user's answer,
    /// and any close without a button click resolves to the spec's safe-close result.
    /// </summary>
    private async Task<object?> ShowMessageDialogAsync(MessageDialogSpec spec, Window? owner = null)
    {
        var viewModel = new MessageDialogViewModel(spec);
        var dialog = new MessageDialog { DataContext = viewModel };
        var choice = new TaskCompletionSource<object?>();
        viewModel.CloseDialog = result => { choice.TrySetResult(result); dialog.Close(); };
        dialog.Closed += (_, _) => choice.TrySetResult(spec.SafeCloseResult);

        owner ??= LiveMainWindow();
        if (owner != null)
            _ = dialog.ShowDialog(owner);
        else
            dialog.Show();
        return await choice.Task;
    }

    public async Task<bool?> ShowAddBookDialogAsync(int? defaultCollectionId = null, string? prefillIsbn = null)
    {
        var viewModel = _serviceProvider.GetRequiredService<AddBookDialogViewModel>();
        viewModel.Reset(defaultCollectionId);
        if (!string.IsNullOrWhiteSpace(prefillIsbn))
            viewModel.Isbn = prefillIsbn;
        await viewModel.InitializeAsync();

        var dialog = new AddBookDialog();
        dialog.DataContext = viewModel;
        viewModel.CloseDialog = result => dialog.Close(result);
        return await dialog.ShowDialog<bool?>(GetMainWindow());
    }

    public async Task<bool?> ShowAddBookIdentifyDialogAsync(int? collectionId = null)
    {
        var viewModel = _serviceProvider.GetRequiredService<AddBookIdentifyViewModel>();
        viewModel.Initialize(collectionId);
        var dialog = new AddBookIdentifyDialog { DataContext = viewModel };
        viewModel.CloseDialog = result => dialog.Close(result);
        return await dialog.ShowDialog<bool?>(GetMainWindow());
    }

    public async Task<bool?> ShowBulkEditDialogAsync(IReadOnlyList<int> bookIds)
    {
        var viewModel = _serviceProvider.GetRequiredService<BulkEditViewModel>();
        await viewModel.InitializeAsync(bookIds);
        var dialog = new BulkEditDialog { DataContext = viewModel };
        viewModel.CloseDialog = result => dialog.Close(result);
        return await dialog.ShowDialog<bool?>(GetMainWindow());
    }

    public async Task<bool?> ShowAdvancedSearchDialogAsync(SavedSearch? searchToEdit = null)
    {
        var viewModel = _serviceProvider.GetRequiredService<AdvancedSearchViewModel>();
        await viewModel.InitializeAsync();
        if (searchToEdit != null)
            viewModel.LoadFromSavedSearch(searchToEdit);
        var dialog = new AdvancedSearchDialog { DataContext = viewModel };
        viewModel.SetCloseAction(result => dialog.Close(result));
        return await dialog.ShowDialog<bool?>(GetMainWindow());
    }

    public async Task<UnsavedChangesResult> ShowUnsavedChangesDialogAsync(string bookTitle)
    {
        var spec = new MessageDialogSpec(
            Localization.Resources.UnsavedChanges_Title,
            string.Format(Localization.Resources.UnsavedChanges_Body, bookTitle),
            [
                new DialogButton(Localization.Resources.Common_Save, UnsavedChangesResult.Save,
                    DialogButtonRole.Primary, IsDefault: true),
                new DialogButton(Localization.Resources.Common_Discard, UnsavedChangesResult.Discard),
                new DialogButton(Localization.Resources.UnsavedChanges_Cancel, UnsavedChangesResult.KeepEditing,
                    IsCancel: true),
            ],
            SafeCloseResult: UnsavedChangesResult.KeepEditing,
            MinWidth: 380);
        // Visible-only: the registry also holds the batch-queue window, which minimize-to-status-bar
        // Hide()s without unregistering — a hidden owner makes ShowDialog throw.
        Window owner = _secondaryWindows.LastOrDefault(w => w.IsActive)
                       ?? _secondaryWindows.LastOrDefault(w => w.IsVisible)
                       ?? GetMainWindow();
        return (UnsavedChangesResult)(await ShowMessageDialogAsync(spec, owner))!;
    }

    public async Task<bool?> ShowDeleteConfirmationAsync(string message)
    {
        // Delete never sits on Enter, and Esc/X decline — destructive actions take a pointed click.
        var spec = new MessageDialogSpec(
            Localization.Resources.Delete_Dialog_Title,
            message,
            [
                new DialogButton(Localization.Resources.Delete_Confirm_Button, true, DialogButtonRole.Danger),
                new DialogButton(Localization.Resources.Delete_Cancel_Button, false, IsCancel: true),
            ],
            SafeCloseResult: false);
        return (bool?)await ShowMessageDialogAsync(spec, GetMainWindow());
    }

    public async Task OpenFullDetailsWindowAsync(int bookId)
    {
        var existing = _secondaryWindows.OfType<FullDetailsWindow>()
            .FirstOrDefault(w => (w.DataContext as FullDetailsWindowViewModel)?.CurrentBook?.BookId == bookId);
        if (existing is { IsVisible: true })
        {
            existing.Activate();
            return;
        }
        var viewModel = _serviceProvider.GetRequiredService<FullDetailsWindowViewModel>();
        // Load before creating the window so a failed load (e.g. the remote DB is down) never leaves a blank
        // window on screen — LoadBookAsync surfaces a connection loss on the status indicator instead.
        if (!await viewModel.LoadBookAsync(bookId))
            return;
        var window = new FullDetailsWindow { DataContext = viewModel };
        viewModel.CloseWindow = () => window.Close();
        window.Show();
        // The first binding pass coerces every lookup ComboBox's SelectedValue (null, then back), which
        // lands in the VM as edits — the window would open already dirty and guard its close. Reset once
        // the initial realization settles; no user input can arrive before the idle callback runs.
        Dispatcher.UIThread.Post(
            () => viewModel.HasUnsavedChanges = false,
            DispatcherPriority.ApplicationIdle);
        RegisterOpenWindow(window, WindowCategory.BookEdit);
    }

    public async Task<bool?> ShowLookupWizardDialogAsync()
    {
        var viewModel = _serviceProvider.GetRequiredService<LookupWizardViewModel>();
        var dialog = new LookupWizardDialog();
        dialog.DataContext = viewModel;
        viewModel.CloseDialog = result => dialog.Close(result);
        return await dialog.ShowDialog<bool?>(GetMainWindow());
    }

    public async Task<bool?> ShowMergeReviewDialogAsync(
        IReadOnlyList<BookMetadata> sources,
        BookMetadata? currentBook,
        IReadOnlyList<CoverOption> coverOptions,
        int? existingBookId,
        int? collectionId,
        IReadOnlyList<string>? rateLimitedSources = null,
        IReadOnlyList<string>? noResultSources = null,
        IReadOnlyList<string>? erroredSources = null,
        Window? ownerWindow = null)
    {
        var dialog = new MergeReviewDialog();
        var bookMetadataService = _serviceProvider.GetRequiredService<BookDB.Logic.Services.IBookMetadataService>();
        var messenger = _serviceProvider.GetRequiredService<CommunityToolkit.Mvvm.Messaging.IMessenger>();
        var viewModel = new MergeReviewViewModel(
            sources: sources,
            currentBook: currentBook,
            coverOptions: coverOptions,
            bookMetadataService: bookMetadataService,
            messenger: messenger,
            existingBookId: existingBookId,
            collectionId: collectionId,
            closeDialog: result => dialog.Close(result),
            windowService: this,
            bookService: _serviceProvider.GetRequiredService<BookDB.Logic.Services.IBookService>(),
            lookupService: _serviceProvider.GetRequiredService<BookDB.Logic.Services.ILookupService>(),
            rateLimitedSources: rateLimitedSources,
            noResultSources: noResultSources,
            erroredSources: erroredSources);
        await viewModel.InitializeAsync();
        dialog.DataContext = viewModel;
        var owner = ownerWindow ?? GetMainWindow();
        return await dialog.ShowDialog<bool?>(owner);
    }

    public async Task<DuplicateIsbnResult> ShowDuplicateIsbnDialogAsync(string isbn, string existingTitle)
    {
        // Both non-cancel choices write to the library, so neither sits on Enter.
        var spec = new MessageDialogSpec(
            Localization.Resources.DuplicateIsbn_Title,
            string.Format(Localization.Resources.DuplicateIsbn_Body, isbn, existingTitle),
            [
                new DialogButton(Localization.Resources.DuplicateIsbn_UpdateExisting, DuplicateIsbnResult.UpdateExisting),
                new DialogButton(Localization.Resources.DuplicateIsbn_AddAsNew, DuplicateIsbnResult.AddAsNew),
                new DialogButton(Localization.Resources.Common_Cancel, DuplicateIsbnResult.Cancel, IsCancel: true),
            ],
            SafeCloseResult: DuplicateIsbnResult.Cancel,
            MinWidth: 380);
        return (DuplicateIsbnResult)(await ShowMessageDialogAsync(spec, GetMainWindow()))!;
    }

    public async Task ShowUpdateHintAsync(InstallChannel channel, string latestVersion, string currentVersion)
    {
        const string releaseUrl = "https://github.com/cadwal/BookDB/releases/latest";
        const string copyResult = "copy";
        const string openResult = "open";

        var header = string.Format(Localization.Resources.Update_Dialog_Body, latestVersion, currentVersion);
        string body;
        string? command = null;
        var buttons = new List<DialogButton>();
        switch (channel)
        {
            case InstallChannel.Winget:
                command = "winget upgrade cadwal.BookDB";
                body = header + "\n\n" + string.Format(Localization.Resources.Update_Hint_Winget, command);
                buttons.Add(new DialogButton(Localization.Resources.Update_CopyCommand, copyResult, DialogButtonRole.Primary, IsDefault: true));
                break;
            case InstallChannel.AppMan:
                command = "am -u bookdb";
                body = header + "\n\n" + string.Format(Localization.Resources.Update_Hint_AppMan, command);
                buttons.Add(new DialogButton(Localization.Resources.Update_CopyCommand, copyResult, DialogButtonRole.Primary, IsDefault: true));
                break;
            default:
                body = header + "\n\n" + Localization.Resources.Update_Hint_GitHub;
                buttons.Add(new DialogButton(Localization.Resources.Update_OpenGitHub, openResult, DialogButtonRole.Primary, IsDefault: true));
                break;
        }
        buttons.Add(new DialogButton(Localization.Resources.Common_Close, null, IsCancel: true));

        var spec = new MessageDialogSpec(
            Localization.Resources.Update_Dialog_Title, body, buttons, SafeCloseResult: null, MinWidth: 440);
        var result = await ShowMessageDialogAsync(spec, GetMainWindow());

        if (result as string == copyResult && command is not null)
            await _serviceProvider.GetRequiredService<IClipboardService>().SetTextAsync(command);
        else if (result as string == openResult)
            SystemLauncher.Open(releaseUrl);
    }

    public async Task<BackupConflictChoice> ShowBackupConflictAsync(string existingPath)
    {
        // The suffixed name shown on the default button is the first free "name-N" slot, matching
        // what the backup writes when the user lets it keep both files.
        var dir = System.IO.Path.GetDirectoryName(existingPath)!;
        var nameNoExt = System.IO.Path.GetFileNameWithoutExtension(existingPath);
        var ext = System.IO.Path.GetExtension(existingPath);
        var suffixName = $"{nameNoExt}-1{ext}";
        for (var i = 1; i < 1000; i++)
        {
            var candidate = System.IO.Path.Combine(dir, $"{nameNoExt}-{i}{ext}");
            if (!System.IO.File.Exists(candidate)) { suffixName = $"{nameNoExt}-{i}{ext}"; break; }
        }

        var spec = new MessageDialogSpec(
            Localization.Resources.AppDialog_BackupConflict_Title,
            string.Format(Localization.Resources.AppDialog_BackupConflict_Body, System.IO.Path.GetFileName(existingPath)),
            [
                new DialogButton(string.Format(Localization.Resources.AppDialog_BackupConflict_SaveAs, suffixName),
                    BackupConflictChoice.AddSuffix, DialogButtonRole.Primary, IsDefault: true),
                new DialogButton(Localization.Resources.AppDialog_BackupConflict_Overwrite, BackupConflictChoice.Overwrite),
                new DialogButton(Localization.Resources.Common_Cancel, BackupConflictChoice.Cancel, IsCancel: true),
            ],
            SafeCloseResult: BackupConflictChoice.Cancel,
            MinWidth: 360);
        return (BackupConflictChoice)(await ShowMessageDialogAsync(spec, GetMainWindow()))!;
    }

    // Every non-modal secondary window lives here — reuse/dedup, the Window menu, and shutdown all
    // derive from this one list rather than per-type fields.
    private readonly List<Window> _secondaryWindows = [];

    // Registers a menu-listed secondary window: tracks it for reuse/shutdown, adds its Window-menu
    // entry (kept in sync with the window Title), and tears both down when the window closes.
    private void RegisterOpenWindow(Window window, WindowCategory category)
    {
        _secondaryWindows.Add(window);
        var mainWindowViewModel = _serviceProvider.GetRequiredService<MainWindowViewModel>();
        var entry = new OpenWindowEntry(window.Title ?? string.Empty, new RelayCommand(window.Activate), category);
        mainWindowViewModel.AddOpenWindow(entry);
        window.PropertyChanged += (_, e) =>
        {
            if (e.Property == Window.TitleProperty)
                entry.Title = window.Title ?? string.Empty;
        };
        window.Closed += (_, _) =>
        {
            _secondaryWindows.Remove(window);
            mainWindowViewModel.RemoveOpenWindow(entry);
        };
    }

    public void CloseAllSecondaryWindows()
    {
        foreach (var window in _secondaryWindows.ToList())
        {
            if (window.IsVisible)
                window.Close();
        }
        _secondaryWindows.Clear();
    }

    /// <summary>
    /// Runs each open guarded window's close confirmation (unsaved Full-Details edits, …)
    /// before an app shutdown, activating the window so the user sees what the prompt is
    /// about. Any refusal aborts. Confirmed guards resolve their dirty state (save/discard),
    /// so the subsequent CloseAllSecondaryWindows closes them without re-prompting.
    /// </summary>
    public async Task<bool> ConfirmCloseGuardedWindowsAsync()
    {
        foreach (var window in _secondaryWindows.ToList())
        {
            if (!window.IsVisible || window.DataContext is not ICloseGuard guard || !guard.ShouldGuardClose)
                continue;
            window.Activate();
            if (!await guard.ConfirmCloseAsync())
                return false;
        }
        return true;
    }

    public void OpenBatchQueueWindow()
    {
        var existing = _secondaryWindows.OfType<BatchQueueWindow>().FirstOrDefault();
        if (existing is { IsVisible: true })
        {
            existing.Activate();
            return;
        }

        // If minimized (hidden), just restore
        if (existing is not null)
        {
            var mainWindowVm = _serviceProvider.GetRequiredService<MainWindowViewModel>();
            mainWindowVm.IsBatchWindowMinimized = false;
            existing.Show();
            existing.Activate();
            return;
        }

        var viewModel = _serviceProvider.GetRequiredService<BatchQueueWindowViewModel>();
        viewModel.ResetStats();
        var window = new BatchQueueWindow { DataContext = viewModel };
        viewModel.CloseWindow = () => window.Close();

        // Wire minimize-to-status-bar
        var mainWindowViewModel = _serviceProvider.GetRequiredService<MainWindowViewModel>();
        viewModel.MinimizeWindow = () =>
        {
            mainWindowViewModel.IsBatchWindowMinimized = true;
            window.Hide();
        };

        RegisterOpenWindow(window, WindowCategory.Utility);
        window.Show(GetMainWindow());
    }

    private BatchQueueWindowViewModel? GetBatchQueueWindowViewModel() =>
        _secondaryWindows.OfType<BatchQueueWindow>().FirstOrDefault()?.DataContext as BatchQueueWindowViewModel;

    public async Task StartBatchAsync(IReadOnlyList<string> isbns)
    {
        OpenBatchQueueWindow();
        var batchVm = GetBatchQueueWindowViewModel();
        if (batchVm is not null)
            await batchVm.StartBatchAsync(isbns);
    }

    public async Task StartBatchRecatalogAsync(IReadOnlyList<int> bookIds)
    {
        OpenBatchQueueWindow();
        var batchVm = GetBatchQueueWindowViewModel();
        if (batchVm is not null)
            await batchVm.StartRecatalogAsync(bookIds);
    }

    public async Task StartBatchRecatalogAsync(int bookId, string isbn)
    {
        OpenBatchQueueWindow();
        var batchVm = GetBatchQueueWindowViewModel();
        if (batchVm is not null)
            await batchVm.StartRecatalogAsync(bookId, isbn);
    }

    public async Task<bool?> ShowImportWizardAsync(string? initialPath = null)
    {
        var viewModel = _serviceProvider.GetRequiredService<ImportWizardViewModel>();
        await viewModel.InitializeAsync();
        if (!string.IsNullOrEmpty(initialPath))
            viewModel.Step1.FilePath = initialPath;

        var window = new ImportWizardWindow();
        window.DataContext = viewModel;
        viewModel.CloseDialog = result => window.Close(result);
        return await window.ShowDialog<bool?>(GetMainWindow());
    }

    public async Task ShowReaderwareDbImportDialogAsync()
    {
        var viewModel = _serviceProvider.GetRequiredService<ReaderwareDbImportViewModel>();
        await viewModel.InitializeAsync();

        var dialog = new ReaderwareDbImportDialog { DataContext = viewModel };
        viewModel.CloseDialog = result => dialog.Close(result);
        var outputDir = await dialog.ShowDialog<string?>(GetMainWindow());

        // On success the dialog returns the converted backup folder — hand it to the Import Wizard.
        if (!string.IsNullOrEmpty(outputDir))
            await ShowImportWizardAsync(outputDir);
    }

    public async Task ShowManageLookupsAsync(string? initialTab = null)
    {
        var existing = _secondaryWindows.OfType<ManageLookupsWindow>().FirstOrDefault();
        if (existing is { IsVisible: true })
        {
            existing.Activate();
            return;
        }

        var viewModel = _serviceProvider.GetRequiredService<ManageLookupsViewModel>();
        await viewModel.InitializeAsync(initialTab);
        var win = new ManageLookupsWindow { DataContext = viewModel };
        viewModel.CloseWindow = () => win.Close();
        RegisterOpenWindow(win, WindowCategory.Utility);
        win.Show(GetMainWindow());
    }

    public async Task ShowSettingsAsync(Window? owner = null, SettingsSection? section = null)
    {
        var viewModel = _serviceProvider.GetRequiredService<SettingsWindowViewModel>();
        if (section == SettingsSection.Database)
            viewModel.ShowDatabaseTab();
        var window = new SettingsWindow { DataContext = viewModel };
        viewModel.CloseDialog = _ => window.Close();
        _secondaryWindows.Add(window);
        window.Closed += (_, _) => _secondaryWindows.Remove(window);
        // Load tab content after the window is shown: the per-database tabs query the database, so an unreachable
        // server (the case where Settings is opened to switch backend) must not delay the window from appearing.
        _ = viewModel.InitializeAsync();
        await window.ShowDialog<object?>(owner ?? GetMainWindow());
    }

    public async Task ShowMaintenanceDialogAsync()
    {
        var viewModel = _serviceProvider.GetRequiredService<MaintenanceViewModel>();
        var window = new MaintenanceDialog { DataContext = viewModel };
        viewModel.CloseDialog = () => window.Close();
        _secondaryWindows.Add(window);
        window.Closed += (_, _) => _secondaryWindows.Remove(window);
        await window.ShowDialog<object?>(GetMainWindow());
    }

    public async Task OpenStatisticsWindowAsync()
    {
        var existing = _secondaryWindows.OfType<StatisticsWindow>().FirstOrDefault();
        if (existing is { IsVisible: true })
        {
            existing.Activate();
            return;
        }

        var viewModel = _serviceProvider.GetRequiredService<StatisticsWindowViewModel>();
        // Load before showing so a failed load (e.g. the remote DB is down) never opens a blank statistics
        // window — TryRefreshAsync surfaces a connection loss on the status indicator instead.
        if (!await viewModel.TryRefreshAsync())
            return;
        var window = new StatisticsWindow { DataContext = viewModel };
        viewModel.CloseWindow = () => window.Close();
        RegisterOpenWindow(window, WindowCategory.Utility);
        window.Show(GetMainWindow());
    }

    public void OpenHelpWindow(HelpTab tab)
    {
        var existing = _secondaryWindows.OfType<HelpWindow>().FirstOrDefault();
        if (existing is { IsVisible: true })
        {
            existing.Activate();
            (existing.DataContext as HelpWindowViewModel)!.SelectedTabIndex = (int)tab;
            return;
        }
        var viewModel = _serviceProvider.GetRequiredService<HelpWindowViewModel>();
        var window = new HelpWindow { DataContext = viewModel };
        viewModel.CloseWindow = () => window.Close();
        RegisterOpenWindow(window, WindowCategory.Utility);
        window.Show(GetMainWindow());
        _ = viewModel.InitializeAsync(tab); // fire-and-forget; content loads after window is shown
    }

    public async Task<IReadOnlyList<string>?> ShowCsvColumnPickerAsync(
        IReadOnlyList<string> allColumns,
        IReadOnlyList<string> defaultSelected)
    {
        var viewModel = _serviceProvider.GetRequiredService<CsvColumnPickerViewModel>();
        viewModel.Initialize(allColumns, defaultSelected);
        var dialog = new CsvColumnPickerDialog { DataContext = viewModel };
        viewModel.CloseDialog = result => dialog.Close(result);
        return await dialog.ShowDialog<IReadOnlyList<string>?>(GetMainWindow());
    }

    public async Task<BookDB.Logic.Services.PrintParameters?> ShowPrintDialogAsync(
        IReadOnlySet<int>? collectionIds,
        IReadOnlyList<int>? searchBookIds,
        Dictionary<string, HashSet<int>>? facetFilters,
        string? sortColumn,
        bool sortAscending,
        int bookCount = 0)
    {
        var viewModel = _serviceProvider.GetRequiredService<PrintDialogViewModel>();
        await viewModel.InitializeAsync(collectionIds, searchBookIds, facetFilters, sortColumn, sortAscending, bookCount);
        var dialog = new PrintDialog { DataContext = viewModel };
        viewModel.CloseDialog = result => dialog.Close(result);
        return await dialog.ShowDialog<BookDB.Logic.Services.PrintParameters?>(GetMainWindow());
    }

    public async Task<int?> ShowMergeTargetPickerAsync(
        string sourceName,
        int sourceId,
        IReadOnlyList<LookupEntryRow> candidates,
        Window? owner = null)
    {
        var viewModel = _serviceProvider.GetRequiredService<MergeTargetPickerViewModel>();
        viewModel.Initialize(sourceName, candidates, sourceId);
        var dialog = new MergeTargetPickerDialog { DataContext = viewModel };
        viewModel.CloseDialog = result => dialog.Close(result);
        var effectiveOwner = owner ?? _secondaryWindows.LastOrDefault(w => w.IsActive) ?? GetMainWindow();
        return await dialog.ShowDialog<int?>(effectiveOwner);
    }

    public async Task<string?> ShowIsbnPromptDialogAsync(string bookTitle)
    {
        var viewModel = new IsbnPromptViewModel(bookTitle);
        var dialog = new IsbnPromptDialog { DataContext = viewModel };
        var choice = new TaskCompletionSource<string?>();
        viewModel.CloseDialog = result => { choice.TrySetResult(result); dialog.Close(); };
        dialog.Closed += (_, _) => choice.TrySetResult(null);
        await dialog.ShowDialog(GetMainWindow());
        return await choice.Task;
    }

    // The confirming close is never the Enter default; Esc/X keep the app running.
    private static MessageDialogSpec ShutdownWarningSpec(string confirmButtonText) => new(
        Localization.Resources.BatchQueue_ShutdownWarning_Title,
        Localization.Resources.BatchQueue_ShutdownWarning_Body,
        [
            new DialogButton(confirmButtonText, true),
            new DialogButton(Localization.Resources.Shutdown_KeepRunning, false, IsCancel: true),
        ],
        SafeCloseResult: false,
        MinWidth: 360);

    public async Task<bool?> ShowBatchShutdownWarningAsync()
    {
        var spec = ShutdownWarningSpec(Localization.Resources.Shutdown_CloseAndPause);
        Window owner = _secondaryWindows.OfType<BatchQueueWindow>().FirstOrDefault() as Window ?? GetMainWindow();
        return (bool?)await ShowMessageDialogAsync(spec, owner);
    }

    public async Task<bool?> ShowMainShutdownWarningAsync()
    {
        var spec = ShutdownWarningSpec(Localization.Resources.Shutdown_CloseApplication);
        return (bool?)await ShowMessageDialogAsync(spec, GetMainWindow());
    }

    public async Task<bool?> ShowCheckOutDialogAsync(int bookId)
    {
        var viewModel = _serviceProvider.GetRequiredService<ViewModels.CheckOutDialogViewModel>();
        await viewModel.InitializeAsync(bookId);
        var dialog = new Views.CheckOutDialog { DataContext = viewModel };
        viewModel.CloseDialog = result => dialog.Close(result);
        return await dialog.ShowDialog<bool?>(GetMainWindow());
    }

    public async Task<(string Format, string Folder)?> ShowBackupFormatDialogAsync(
        bool supportsFileBackup, string configDefault, string defaultFolder)
    {
        var filePicker = _serviceProvider.GetRequiredService<BookDB.Models.Interfaces.IFilePickerService>();
        var viewModel = new ViewModels.BackupFormatDialogViewModel(supportsFileBackup, configDefault, defaultFolder, filePicker);
        var dialog = new Views.BackupFormatDialog { DataContext = viewModel };
        viewModel.CloseDialog = _ => dialog.Close();
        await dialog.ShowDialog(GetMainWindow());
        return viewModel.Result is null ? null : (viewModel.Result, viewModel.DestinationFolder);
    }

    public async Task ShowManageBorrowersAsync()
    {
        var existing = _secondaryWindows.OfType<Views.ManageBorrowersWindow>().FirstOrDefault();
        if (existing is { IsVisible: true })
        {
            existing.Activate();
            return;
        }

        var viewModel = _serviceProvider.GetRequiredService<ViewModels.ManageBorrowersViewModel>();
        await viewModel.InitializeAsync();
        var win = new Views.ManageBorrowersWindow { DataContext = viewModel };
        viewModel.CloseWindow = () => win.Close();
        RegisterOpenWindow(win, WindowCategory.Utility);
        win.Show(GetMainWindow());
    }

    public async Task<bool> ShowConnectDialogAsync(Window owner)
    {
        var heartbeat = _serviceProvider.GetRequiredService<IHeartbeatService>();
        if (!heartbeat.IsEnabled)
            return true;

        var sessions = await heartbeat.GetActiveSessionsAsync();
        if (sessions.Count == 0)
            return true;

        var viewModel = new ConnectDialogViewModel(sessions, _serviceProvider.GetRequiredService<TimeProvider>());
        var dialog = new ConnectDialog { DataContext = viewModel };
        viewModel.CloseDialog = () => dialog.Close();
        viewModel.StartCountdown();
        await dialog.ShowDialog(owner);
        return viewModel.Result == ConnectChoice.ConnectAnyway;
    }

    public async Task<StartupFailureOutcome> ShowStartupFailureDialogAsync(
        ConnectionProbeResult initialResult,
        Func<System.Threading.CancellationToken, Task<ConnectionProbeResult>> connect,
        Window owner)
    {
        var viewModel = new StartupFailureViewModel(initialResult, connect);
        var dialog = new StartupFailureDialog { DataContext = viewModel };
        viewModel.CloseDialog = () => dialog.Close();
        await dialog.ShowDialog(owner);
        // Closing the window with no explicit choice is treated as Quit — never proceed on a bad connection.
        return viewModel.Outcome ?? StartupFailureOutcome.Quit;
    }

    public async Task<WriteFailureChoice> ShowWriteFailureDialogAsync(string message)
    {
        // Retry carries Enter, Esc and the window X: dismissing this dialog by any
        // reflex path re-attempts the write — only an explicit Discard drops work.
        var spec = new MessageDialogSpec(
            Localization.Resources.WriteFailure_Title,
            message,
            [
                new DialogButton(Localization.Resources.WriteFailure_Retry_Button, WriteFailureChoice.Retry,
                    DialogButtonRole.Primary, IsDefault: true, IsCancel: true),
                new DialogButton(Localization.Resources.WriteFailure_Discard_Button, WriteFailureChoice.Discard),
            ],
            SafeCloseResult: WriteFailureChoice.Retry,
            MinWidth: 380);
        return (WriteFailureChoice)(await ShowMessageDialogAsync(spec, GetMainWindow()))!;
    }

    public async Task<bool> ShowConnectionLostEscalationDialogAsync()
    {
        // Keep-waiting is the safe answer on Enter, Esc and X; Quit (destructive) takes a pointed click.
        var spec = new MessageDialogSpec(
            Localization.Resources.ConnectionLost_Title,
            Localization.Resources.ConnectionLost_Body,
            [
                new DialogButton(Localization.Resources.ConnectionLost_KeepWaiting_Button, false,
                    DialogButtonRole.Primary, IsDefault: true, IsCancel: true),
                new DialogButton(Localization.Resources.ConnectionLost_Quit_Button, true),
            ],
            SafeCloseResult: false,
            MinWidth: 380);
        return (bool)(await ShowMessageDialogAsync(spec, GetMainWindow()))!;
    }

    public async Task ShowAboutAsync()
    {
        var viewModel = new AboutWindowViewModel();
        var dialog = new AboutWindow { DataContext = viewModel };
        viewModel.CloseWindow = () => dialog.Close();
        await dialog.ShowDialog(GetMainWindow());
    }

    public async Task ShowReleaseNotesAsync(string version, string markdown)
    {
        var viewModel = new ReleaseNotesViewModel(version, markdown);
        var window = new ReleaseNotesWindow { DataContext = viewModel };
        viewModel.CloseWindow = () => window.Close();
        await window.ShowDialog(GetMainWindow());
    }

    public async Task<ReleaseNotesChoice> ShowReleaseNotesPromptAsync(string version)
    {
        var spec = new MessageDialogSpec(
            Localization.Resources.ReleaseNotes_Prompt_Title,
            string.Format(Localization.Resources.ReleaseNotes_Prompt_Body, version),
            [
                new DialogButton(Localization.Resources.AppDialog_Yes_Button, ReleaseNotesChoice.Show,
                    DialogButtonRole.Primary, IsDefault: true),
                new DialogButton(Localization.Resources.ReleaseNotes_Prompt_Skip_Button, ReleaseNotesChoice.Skip),
                new DialogButton(Localization.Resources.Common_Cancel, ReleaseNotesChoice.Defer, IsCancel: true),
            ],
            SafeCloseResult: ReleaseNotesChoice.Defer,
            MinWidth: 380);
        return (ReleaseNotesChoice)(await ShowMessageDialogAsync(spec, GetMainWindow()))!;
    }

    public async Task<RestoreTargetChoice> ShowRestoreTargetAsync(string archivedServerDescription)
    {
        var viewModel = new RestoreTargetViewModel(archivedServerDescription);
        var dialog = new RestoreTargetDialog { DataContext = viewModel };
        var choice = new TaskCompletionSource<RestoreTargetChoice>();
        viewModel.CloseDialog = result => { choice.TrySetResult(result); dialog.Close(); };
        dialog.Closed += (_, _) => choice.TrySetResult(RestoreTargetChoice.Cancel);
        await dialog.ShowDialog(GetMainWindow());
        return await choice.Task;
    }

    public Task ShowInfoAsync(string body, string? title = null)
    {
        var spec = new MessageDialogSpec(
            title ?? Localization.Resources.AppDialog_Info_Title,
            body,
            [
                new DialogButton(Localization.Resources.Common_OK, null,
                    DialogButtonRole.Primary, IsDefault: true, IsCancel: true),
            ],
            SafeCloseResult: null);
        return ShowMessageDialogAsync(spec, GetMainWindow());
    }

    public async Task<bool?> ShowConfirmAsync(string title, string body, Window? owner = null)
    {
        // Owner deliberately unforced: restart confirmations can fire during startup outage recovery,
        // before the main window exists — resolving it from DI here would construct it prematurely.
        var spec = new MessageDialogSpec(
            title,
            body,
            [
                new DialogButton(Localization.Resources.AppDialog_Yes_Button, true,
                    DialogButtonRole.Primary, IsDefault: true),
                new DialogButton(Localization.Resources.AppDialog_No_Button, false, IsCancel: true),
            ],
            SafeCloseResult: null,
            MinWidth: 360);
        return (bool?)await ShowMessageDialogAsync(spec, owner);
    }

    public async Task<ImportDuplicateResolution> ShowDuplicateResolutionAsync(string title, string body)
    {
        var viewModel = new DuplicateResolutionViewModel(title, body);
        var dialog = new DuplicateResolutionDialog { DataContext = viewModel };
        var choice = new TaskCompletionSource<ImportDuplicateResolution>();
        viewModel.CloseDialog = result => { choice.TrySetResult(result); dialog.Close(); };
        dialog.Closed += (_, _) => choice.TrySetResult(ImportDuplicateResolution.Skip);
        await dialog.ShowDialog(GetMainWindow());
        return await choice.Task;
    }

    // The long operations run under Task.Run, so reports and Close arrive from worker threads —
    // every touch of the window/VM marshals to the UI thread.
    private sealed class ProgressWindowHandle(ProgressWindow window, ProgressWindowViewModel viewModel)
        : IProgressWindowHandle
    {
        public void Report(string value) => Dispatcher.UIThread.Post(() => viewModel.Status = value);

        // Close synchronously when already on the UI thread so a follow-up dialog (e.g. "backup saved")
        // never opens while this window's close is still queued behind it.
        public void Close()
        {
            if (Dispatcher.UIThread.CheckAccess())
                window.Close();
            else
                Dispatcher.UIThread.Post(window.Close);
        }
    }

    public IProgressWindowHandle ShowProgressWindow(string header)
    {
        var viewModel = new ProgressWindowViewModel(header);
        var window = new ProgressWindow { DataContext = viewModel };
        // Modal (ShowDialog, not awaited) so it blocks the main window while the operation runs and can
        // never fall behind it; the caller closes the handle when the work finishes.
        _ = window.ShowDialog(GetMainWindow());
        return new ProgressWindowHandle(window, viewModel);
    }

    public IProgressWindowHandle ShowBackupProgressWindow()
    {
        var viewModel = new ProgressWindowViewModel(Localization.Resources.Shutdown_BackupInProgress, isCard: true);
        var window = new ProgressWindow
        {
            DataContext = viewModel,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
        };
        try
        {
            window.Show();
        }
        catch (Exception ex)
        {
            // Best-effort: a failed status window must never abort the backup itself.
            Log.Warning("Could not show backup status window — continuing without it: {Error}", ex.Message);
        }
        return new ProgressWindowHandle(window, viewModel);
    }
}
