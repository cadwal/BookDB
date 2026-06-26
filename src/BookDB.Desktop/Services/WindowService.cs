using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using BookDB.Data.Interfaces;
using BookDB.Desktop.ViewModels;
using BookDB.Desktop.Views;
using BookDB.Help;
using BookDB.Logic.Services;
using BookDB.Models.Entities;
using BookDB.Models.Metadata;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;

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
        var dialog = Helpers.AppDialogs.BuildUnsavedChangesDialog(bookTitle);
        Window owner = _secondaryWindows.LastOrDefault(w => w.IsActive) 
                       ?? _secondaryWindows.LastOrDefault() 
                       ?? GetMainWindow();
        var result = await dialog.ShowDialog<UnsavedChangesResult?>(owner);
        return result ?? UnsavedChangesResult.KeepEditing;
    }

    public async Task<bool?> ShowDeleteConfirmationAsync(string message)
    {
        var dialog = BuildDeleteConfirmationDialog(message);
        return await dialog.ShowDialog<bool?>(GetMainWindow());
    }

    public async Task OpenFullDetailsWindowAsync(int bookId)
    {
        if (_fullDetailsWindows.TryGetValue(bookId, out var existing) && existing.IsVisible)
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
        _fullDetailsWindows[bookId] = window;
        window.Show();
        _secondaryWindows.Add(window);
        var mainWindowViewModel = _serviceProvider.GetRequiredService<MainWindowViewModel>();
        var openWindowEntry = new OpenWindowEntry(window.Title ?? "Book Details", new RelayCommand(() => window?.Activate()), WindowCategory.BookEdit);
        mainWindowViewModel.AddOpenWindow(openWindowEntry);
        window.PropertyChanged += (_, e) =>
        {
            if (e.Property == Avalonia.Controls.Window.TitleProperty)
                openWindowEntry.Title = window.Title ?? string.Empty;
        };
        window.Closed += (_, _) =>
        {
            _fullDetailsWindows.Remove(bookId);
            _secondaryWindows.Remove(window);
            mainWindowViewModel.RemoveOpenWindow(openWindowEntry);
        };
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
            windowService: this);
        dialog.DataContext = viewModel;
        var owner = ownerWindow ?? GetMainWindow();
        return await dialog.ShowDialog<bool?>(owner);
    }

    public async Task<DuplicateIsbnResult> ShowDuplicateIsbnDialogAsync(string isbn, string existingTitle)
    {
        var dialog = BuildDuplicateIsbnDialog(isbn, existingTitle);
        var result = await dialog.ShowDialog<DuplicateIsbnResult?>(GetMainWindow());
        return result ?? DuplicateIsbnResult.Cancel;
    }

    private BatchQueueWindow? _batchQueueWindow;
    private StatisticsWindow? _statisticsWindow;
    private HelpWindow? _helpWindow;
    private ManageLookupsWindow? _manageLookupsWindow;
    private ManageBorrowersWindow? _manageBorrowersWindow;
    private readonly Dictionary<int, FullDetailsWindow> _fullDetailsWindows = [];
    private readonly List<Window> _secondaryWindows = [];

    public void CloseAllSecondaryWindows()
    {
        if (_batchQueueWindow is { IsVisible: true })
        {
            _batchQueueWindow.Close();
            _batchQueueWindow = null;
        }

        if (_manageLookupsWindow is { IsVisible: true })
        {
            _manageLookupsWindow.Close();
            _manageLookupsWindow = null;
        }

        if (_manageBorrowersWindow is { IsVisible: true })
        {
            _manageBorrowersWindow.Close();
            _manageBorrowersWindow = null;
        }

        foreach (var window in _secondaryWindows.ToList())
        {
            if (window.IsVisible)
                window.Close();
        }
        _secondaryWindows.Clear();
    }

    public void OpenBatchQueueWindow()
    {
        if (_batchQueueWindow is { IsVisible: true })
        {
            _batchQueueWindow.Activate();
            return;
        }

        // If minimized (hidden), just restore
        if (_batchQueueWindow is not null)
        {
            var mainWindowVm = _serviceProvider.GetRequiredService<MainWindowViewModel>();
            mainWindowVm.IsBatchWindowMinimized = false;
            _batchQueueWindow.Show();
            _batchQueueWindow.Activate();
            return;
        }

        var viewModel = _serviceProvider.GetRequiredService<BatchQueueWindowViewModel>();
        viewModel.ResetStats();
        _batchQueueWindow = new BatchQueueWindow { DataContext = viewModel };
        viewModel.CloseWindow = () => _batchQueueWindow?.Close();

        // Wire minimize-to-status-bar
        var mainWindowViewModel = _serviceProvider.GetRequiredService<MainWindowViewModel>();
        viewModel.MinimizeWindow = () =>
        {
            mainWindowViewModel.IsBatchWindowMinimized = true;
            _batchQueueWindow?.Hide();
        };

        var batchWindowEntry = new OpenWindowEntry(_batchQueueWindow.Title ?? "Batch Queue", new RelayCommand(() => _batchQueueWindow?.Activate()), WindowCategory.Utility);
        mainWindowViewModel.AddOpenWindow(batchWindowEntry);
        _batchQueueWindow.Closed += (_, _) =>
        {
            _batchQueueWindow = null;
            mainWindowViewModel.RemoveOpenWindow(batchWindowEntry);
        };
        _batchQueueWindow.Show(GetMainWindow());
    }

    private BatchQueueWindowViewModel? GetBatchQueueWindowViewModel()
    {
        if (_batchQueueWindow?.DataContext is BatchQueueWindowViewModel viewModel)
            return viewModel;
        return null;
    }

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
        if (_manageLookupsWindow is { IsVisible: true })
        {
            _manageLookupsWindow.Activate();
            return;
        }

        var viewModel = _serviceProvider.GetRequiredService<ManageLookupsViewModel>();
        await viewModel.InitializeAsync(initialTab);
        var win = new ManageLookupsWindow { DataContext = viewModel };
        viewModel.CloseWindow = () => win.Close();
        _manageLookupsWindow = win;
        _secondaryWindows.Add(_manageLookupsWindow);
        var mainWindowViewModel = _serviceProvider.GetRequiredService<MainWindowViewModel>();
        var manageLookupsEntry = new OpenWindowEntry(win.Title ?? "Manage Lookups", new RelayCommand(() => win?.Activate()), WindowCategory.Utility);
        mainWindowViewModel.AddOpenWindow(manageLookupsEntry);
        _manageLookupsWindow.Closed += (_, _) =>
        {
            _secondaryWindows.Remove(win);
            _manageLookupsWindow = null;
            mainWindowViewModel.RemoveOpenWindow(manageLookupsEntry);
        };
        _manageLookupsWindow.Show(GetMainWindow());
    }

    public async Task ShowSettingsAsync(Window? owner = null)
    {
        var viewModel = _serviceProvider.GetRequiredService<SettingsWindowViewModel>();
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
        if (_statisticsWindow is { IsVisible: true })
        {
            _statisticsWindow.Activate();
            return;
        }

        var viewModel = _serviceProvider.GetRequiredService<StatisticsWindowViewModel>();
        // Load before showing so a failed load (e.g. the remote DB is down) never opens a blank statistics
        // window — TryRefreshAsync surfaces a connection loss on the status indicator instead.
        if (!await viewModel.TryRefreshAsync())
            return;
        var window = new StatisticsWindow { DataContext = viewModel };
        viewModel.CloseWindow = () => _statisticsWindow?.Close();
        _statisticsWindow = window;
        var mainWindowViewModel = _serviceProvider.GetRequiredService<MainWindowViewModel>();
        var statisticsEntry = new OpenWindowEntry(_statisticsWindow.Title ?? "Statistics", new RelayCommand(() => _statisticsWindow?.Activate()), WindowCategory.Utility);
        mainWindowViewModel.AddOpenWindow(statisticsEntry);
        _statisticsWindow.Closed += (_, _) =>
        {
            _statisticsWindow = null;
            mainWindowViewModel.RemoveOpenWindow(statisticsEntry);
        };
        _statisticsWindow.Show(GetMainWindow());
    }

    public void OpenHelpWindow(HelpTab tab)
    {
        if (_helpWindow is { IsVisible: true })
        {
            _helpWindow.Activate();
            (_helpWindow.DataContext as HelpWindowViewModel)!.SelectedTabIndex = (int)tab;
            return;
        }
        var viewModel = _serviceProvider.GetRequiredService<HelpWindowViewModel>();
        var window = new HelpWindow { DataContext = viewModel };
        viewModel.CloseWindow = () => _helpWindow?.Close();
        _helpWindow = window;
        _secondaryWindows.Add(window);
        var mainWindowViewModel = _serviceProvider.GetRequiredService<MainWindowViewModel>();
        var entry = new OpenWindowEntry(window.Title ?? "Help", new RelayCommand(() => window?.Activate()), WindowCategory.Utility);
        mainWindowViewModel.AddOpenWindow(entry);
        window.Closed += (_, _) =>
        {
            _helpWindow = null;
            _secondaryWindows.Remove(window);
            mainWindowViewModel.RemoveOpenWindow(entry);
        };
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

    public async Task<string?> ShowIsbnPromptDialogAsync()
    {
        var dialog = BuildIsbnPromptDialog(out var textBox);
        var result = await dialog.ShowDialog<string?>(GetMainWindow());
        return result;
    }

    public async Task<bool?> ShowBatchShutdownWarningAsync()
    {
        var dialog = Helpers.AppDialogs.BuildShutdownWarningDialog(
            Localization.Resources.Shutdown_CloseAndPause, Localization.Resources.Shutdown_KeepRunning);
        Window owner = (Window?)_batchQueueWindow ?? GetMainWindow();
        return await dialog.ShowDialog<bool?>(owner);
    }

    public async Task<bool?> ShowMainShutdownWarningAsync()
    {
        var dialog = Helpers.AppDialogs.BuildShutdownWarningDialog(
            Localization.Resources.Shutdown_CloseApplication, Localization.Resources.Shutdown_KeepRunning);
        return await dialog.ShowDialog<bool?>(GetMainWindow());
    }

    private static Window BuildIsbnPromptDialog(out TextBox textBox)
    {
        var dialog = new Window
        {
            Title = Localization.Resources.Recatalog_NoIsbn_Title,
            SizeToContent = SizeToContent.WidthAndHeight,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Padding = new Thickness(24),
            MinWidth = 340
        };

        var input = new TextBox
        {
            Watermark = Localization.Resources.Recatalog_NoIsbn_Watermark,
            Width = 280,
            Margin = new Thickness(0, 8, 0, 0)
        };
        textBox = input;

        var okBtn = new Button
        {
            Content = Localization.Resources.Recatalog_NoIsbn_LookUp,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        okBtn.Classes.Add("accent");
        okBtn.Click += (_, _) =>
        {
            var isbn = input.Text?.Trim();
            dialog.Close(string.IsNullOrEmpty(isbn) ? null : isbn);
        };

        var cancelBtn = new Button
        {
            Content = Localization.Resources.Common_Cancel,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        cancelBtn.Click += (_, _) => dialog.Close(null);

        var buttonRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(0, 16, 0, 0)
        };
        buttonRow.Children.Add(okBtn);
        buttonRow.Children.Add(cancelBtn);

        var root = new StackPanel { Spacing = 4 };
        root.Children.Add(new TextBlock
        {
            Text = Localization.Resources.Recatalog_NoIsbn_Body,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 340
        });
        root.Children.Add(input);
        root.Children.Add(buttonRow);

        dialog.Content = root;
        return dialog;
    }

    // --- Dialog builders ---

    private static Window BuildDuplicateIsbnDialog(string isbn, string existingTitle)
    {
        var dialog = new Window
        {
            Title = Localization.Resources.DuplicateIsbn_Title,
            SizeToContent = SizeToContent.WidthAndHeight,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Padding = new Thickness(24),
            MinWidth = 380
        };

        var updateBtn = new Button
        {
            Content = Localization.Resources.DuplicateIsbn_UpdateExisting,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        updateBtn.Click += (_, _) => dialog.Close(DuplicateIsbnResult.UpdateExisting);

        var addBtn = new Button
        {
            Content = Localization.Resources.DuplicateIsbn_AddAsNew,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        addBtn.Click += (_, _) => dialog.Close(DuplicateIsbnResult.AddAsNew);

        var cancelBtn = new Button
        {
            Content = Localization.Resources.Common_Cancel,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        cancelBtn.Click += (_, _) => dialog.Close(DuplicateIsbnResult.Cancel);

        var buttonRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(0, 16, 0, 0)
        };
        buttonRow.Children.Add(updateBtn);
        buttonRow.Children.Add(addBtn);
        buttonRow.Children.Add(cancelBtn);

        var root = new StackPanel { Spacing = 8 };
        root.Children.Add(new TextBlock
        {
            Text = string.Format(Localization.Resources.DuplicateIsbn_Body, isbn, existingTitle),
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 400
        });
        root.Children.Add(buttonRow);

        dialog.Content = root;
        return dialog;
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
        if (_manageBorrowersWindow is { IsVisible: true })
        {
            _manageBorrowersWindow.Activate();
            return;
        }

        var viewModel = _serviceProvider.GetRequiredService<ViewModels.ManageBorrowersViewModel>();
        await viewModel.InitializeAsync();
        var win = new Views.ManageBorrowersWindow { DataContext = viewModel };
        viewModel.CloseWindow = () => win.Close();
        _manageBorrowersWindow = win;
        _secondaryWindows.Add(_manageBorrowersWindow);
        var mainWindowViewModel = _serviceProvider.GetRequiredService<ViewModels.MainWindowViewModel>();
        var entry = new OpenWindowEntry(
            win.Title ?? "Manage Borrowers",
            new RelayCommand(() => win?.Activate()),
            WindowCategory.Utility);
        mainWindowViewModel.AddOpenWindow(entry);
        _manageBorrowersWindow.Closed += (_, _) =>
        {
            _secondaryWindows.Remove(win);
            _manageBorrowersWindow = null;
            mainWindowViewModel.RemoveOpenWindow(entry);
        };
        _manageBorrowersWindow.Show(GetMainWindow());
    }

    private static Window BuildDeleteConfirmationDialog(string message)
    {
        var dialog = new Window
        {
            Title = Localization.Resources.Delete_Dialog_Title,
            SizeToContent = SizeToContent.WidthAndHeight,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Padding = new Thickness(24),
            MinWidth = 320
        };

        var deleteBtn = new Button
        {
            Content = Localization.Resources.Delete_Confirm_Button,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Background = Helpers.Palette.Brush("BrushError", Brushes.Red),
            Foreground = Helpers.Palette.Brush("BrushBadgeText", Brushes.White)
        };
        deleteBtn.Click += (_, _) => dialog.Close(true);

        var cancelBtn = new Button
        {
            Content = Localization.Resources.Delete_Cancel_Button,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        cancelBtn.Click += (_, _) => dialog.Close(false);

        var buttonRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(0, 16, 0, 0)
        };
        buttonRow.Children.Add(deleteBtn);
        buttonRow.Children.Add(cancelBtn);

        var root = new StackPanel { Spacing = 8 };
        root.Children.Add(new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 400
        });
        root.Children.Add(buttonRow);

        dialog.Content = root;
        return dialog;
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

    public Task<WriteFailureChoice> ShowWriteFailureDialogAsync(string message) =>
        Helpers.AppDialogs.ShowWriteFailureDialogAsync(message);

    public Task<bool> ShowConnectionLostEscalationDialogAsync() =>
        Helpers.AppDialogs.ShowConnectionLostEscalationDialogAsync();
}
