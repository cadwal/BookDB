using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using BookDB.Data.Interfaces;
using BookDB.Desktop.Services;
using BookDB.Desktop.ViewModels;
using BookDB.Desktop.Views;
using BookDB.Help;
using BookDB.Logic.Services;
using BookDB.Models.Entities;
using BookDB.Models.Interfaces;
using BookDB.Models.Metadata;
using CommunityToolkit.Mvvm.Messaging;

namespace BookDB.Desktop.UITests;

/// <summary>A UI surface the smoke layer renders: its display name and how to build it from a fresh test host
/// (resolve the real VM, wire the view, run the init the app runs).</summary>
public sealed record Surface(string Name, Func<TestHost, Task<Control>> BuildAsync);

/// <summary>
/// Every top-level window, standalone pane, and dialog the smoke layer covers. Each <see cref="Surface.BuildAsync"/>
/// mirrors how <c>WindowService</c> / <c>MainWindow.axaml</c> construct it. Sub-tab panes (PersonTab, BookEditForm)
/// render in-situ within their windows; non-default panes (DatabaseSettings, ImportStep*) get standalone entries.
/// The discovery guard confirms which view types still need one.
/// </summary>
public static class SurfaceRegistry
{
    public static IReadOnlyList<Surface> Windows { get; } =
    [
        new("Splash", host => Task.FromResult<Control>(host.Resolve<SplashWindow>())),

        new("Main", async host =>
        {
            var window = host.Resolve<MainWindow>();
            await ((MainWindowViewModel)window.DataContext!).InitializeAsync();
            return window;
        }),

        new("Settings", async host =>
        {
            var vm = host.Resolve<SettingsWindowViewModel>();
            await vm.InitializeAsync();
            return new SettingsWindow { DataContext = vm };
        }),

        new("Statistics", async host =>
        {
            var vm = host.Resolve<StatisticsWindowViewModel>();
            await vm.TryRefreshAsync();
            return new StatisticsWindow { DataContext = vm };
        }),

        new("Help", async host =>
        {
            var vm = host.Resolve<HelpWindowViewModel>();
            await vm.InitializeAsync(HelpTab.KeyboardShortcuts);
            return new HelpWindow { DataContext = vm };
        }),

        new("BatchQueue", host =>
        {
            var vm = host.Resolve<BatchQueueWindowViewModel>();
            vm.ResetStats();
            return Task.FromResult<Control>(new BatchQueueWindow { DataContext = vm });
        }),

        new("ManageBorrowers", async host =>
        {
            var vm = host.Resolve<ManageBorrowersViewModel>();
            await vm.InitializeAsync();
            return new ManageBorrowersWindow { DataContext = vm };
        }),

        new("ManageLookups", async host =>
        {
            var vm = host.Resolve<ManageLookupsViewModel>();
            await vm.InitializeAsync(null);
            return new ManageLookupsWindow { DataContext = vm };
        }),

        new("ImportWizard", async host =>
        {
            var vm = host.Resolve<ImportWizardViewModel>();
            await vm.InitializeAsync();
            return new ImportWizardWindow { DataContext = vm };
        }),

        new("FullDetails", async host =>
        {
            var book = await SeedData.AddBookAsync(host, "Smoke Subject");
            var vm = host.Resolve<FullDetailsWindowViewModel>();
            await vm.LoadBookAsync(book.BookId);
            return new FullDetailsWindow { DataContext = vm };
        }),
    ];

    public static IReadOnlyList<Surface> Panes { get; } =
    [
        new("BookList", host => Pane(new BookListView(), host.Resolve<BookListViewModel>())),
        new("BookDetail", host => Pane(new BookDetailView(), host.Resolve<BookDetailViewModel>())),
        new("FilterPanel", host => Pane(new FilterPanelView(), host.Resolve<FilterPanelViewModel>())),
        new("CollectionSelector", host => Pane(new CollectionSelectorView(), host.Resolve<CollectionSelectorViewModel>())),
        new("MoveLibrary", host => Pane(new MoveLibraryView(), host.Resolve<MoveLibraryViewModel>())),
        // A non-default Settings tab, so it never realizes in-situ — covered standalone via its parent's child VM.
        new("DatabaseSettings", host => Pane(new DatabaseSettingsView(), host.Resolve<SettingsWindowViewModel>().DatabaseTab)),

        // The wizard shell realizes its step panes through the app-level ViewLocator, which the headless app
        // skips — so each step is covered standalone with its wizard-wired step VM (the wizard ctor delegates
        // the step commands, so the buttons bind to real commands here too).
        new("ImportStep1", async host =>
        {
            var vm = host.Resolve<ImportWizardViewModel>();
            await vm.InitializeAsync();
            return await Pane(new Views.ImportStepViews.ImportStep1View(), vm.Step1);
        }),
        new("ImportStep2", host => Pane(new Views.ImportStepViews.ImportStep2View(), host.Resolve<ImportWizardViewModel>().Step2)),
        new("ImportStep3", host => Pane(new Views.ImportStepViews.ImportStep3View(), host.Resolve<ImportWizardViewModel>().Step3)),
        new("ImportStep4", host => Pane(new Views.ImportStepViews.ImportStep4View(), host.Resolve<ImportWizardViewModel>().Step4)),
        new("ImportStep5", host =>
        {
            // The wizard always sets the result before showing the report; a null Errors list never renders.
            var vm = host.Resolve<ImportWizardViewModel>().Step5;
            vm.Errors = Array.Empty<string>();
            return Pane(new Views.ImportStepViews.ImportStep5View(), vm);
        }),
    ];

    public static IReadOnlyList<Surface> Dialogs { get; } =
    [
        new("AddBook", async host =>
        {
            var vm = host.Resolve<AddBookDialogViewModel>();
            vm.Reset(null);
            await vm.InitializeAsync();
            return new AddBookDialog { DataContext = vm };
        }),

        new("BulkEdit", async host =>
        {
            var a = await SeedData.AddBookAsync(host, "Bulk A");
            var b = await SeedData.AddBookAsync(host, "Bulk B");
            var vm = host.Resolve<BulkEditViewModel>();
            await vm.InitializeAsync([a.BookId, b.BookId]);
            return new BulkEditDialog { DataContext = vm };
        }),

        new("AdvancedSearch", async host =>
        {
            var vm = host.Resolve<AdvancedSearchViewModel>();
            await vm.InitializeAsync();
            return new AdvancedSearchDialog { DataContext = vm };
        }),

        new("LookupWizard", host =>
            Task.FromResult<Control>(new LookupWizardDialog { DataContext = host.Resolve<LookupWizardViewModel>() })),

        new("MergeReview", host =>
        {
            var vm = new MergeReviewViewModel(
                sources: Array.Empty<BookMetadata>(),
                currentBook: null,
                coverOptions: Array.Empty<CoverOption>(),
                bookMetadataService: host.Resolve<IBookMetadataService>(),
                messenger: host.Resolve<IMessenger>(),
                existingBookId: null,
                collectionId: null,
                closeDialog: _ => { },
                windowService: host.Resolve<IWindowService>());
            return Task.FromResult<Control>(new MergeReviewDialog { DataContext = vm });
        }),

        new("MergeTargetPicker", host =>
        {
            var vm = host.Resolve<MergeTargetPickerViewModel>();
            vm.Initialize("Source", Array.Empty<LookupEntryRow>(), sourceId: 1);
            return Task.FromResult<Control>(new MergeTargetPickerDialog { DataContext = vm });
        }),

        new("CheckOut", async host =>
        {
            var book = await SeedData.AddBookAsync(host, "Loanable");
            var vm = host.Resolve<CheckOutDialogViewModel>();
            await vm.InitializeAsync(book.BookId);
            return new CheckOutDialog { DataContext = vm };
        }),

        new("Connect", host =>
        {
            // The dialog is only ever shown when another client holds the database, so it requires a session.
            var session = new ClientSession { Hostname = "another-pc", AppVersion = "2.1.0", LastSeenAt = DateTime.UtcNow };
            var vm = new ConnectDialogViewModel([session], host.Resolve<TimeProvider>());
            return Task.FromResult<Control>(new ConnectDialog { DataContext = vm });
        }),

        new("StartupFailure", host =>
        {
            var failure = ConnectionProbeResult.Failed(ConnectionProbeStatus.ConnectionRefused, "smoke");
            var vm = new StartupFailureViewModel(failure, _ => Task.FromResult(failure));
            return Task.FromResult<Control>(new StartupFailureDialog { DataContext = vm });
        }),

        new("BackupFormat", host =>
        {
            var vm = new BackupFormatDialogViewModel(
                supportsFileBackup: true,
                configDefault: BackupFormatDialogViewModel.SqliteFormat,
                defaultFolder: System.IO.Path.GetTempPath(),
                filePicker: host.Resolve<IFilePickerService>());
            return Task.FromResult<Control>(new BackupFormatDialog { DataContext = vm });
        }),

        new("CsvColumnPicker", host =>
        {
            var vm = host.Resolve<CsvColumnPickerViewModel>();
            vm.Initialize(["Title", "ISBN", "Author"], ["Title"]);
            return Task.FromResult<Control>(new CsvColumnPickerDialog { DataContext = vm });
        }),

        new("Print", async host =>
        {
            var vm = host.Resolve<PrintDialogViewModel>();
            await vm.InitializeAsync(null, null, null, null, sortAscending: true, bookCount: 0);
            return new PrintDialog { DataContext = vm };
        }),

        new("ReaderwareDbImport", async host =>
        {
            var vm = host.Resolve<ReaderwareDbImportViewModel>();
            await vm.InitializeAsync();
            return new ReaderwareDbImportDialog { DataContext = vm };
        }),

        new("Maintenance", host =>
            Task.FromResult<Control>(new MaintenanceDialog { DataContext = host.Resolve<MaintenanceViewModel>() })),

        new("MessageDialog", _ => Task.FromResult<Control>(new MessageDialog
        {
            DataContext = new MessageDialogViewModel(new MessageDialogSpec(
                "Smoke title", "Smoke body.",
                [new DialogButton("OK", true, DialogButtonRole.Primary, IsDefault: true, IsCancel: true)],
                SafeCloseResult: null)),
        })),

        new("About", _ =>
            Task.FromResult<Control>(new AboutWindow { DataContext = new AboutWindowViewModel() })),

        new("ReleaseNotes", _ => Task.FromResult<Control>(new ReleaseNotesWindow
        {
            DataContext = new ReleaseNotesViewModel("2.3.0", "### Added\n- Smoke item"),
        })),

        new("RestoreTarget", _ => Task.FromResult<Control>(new RestoreTargetDialog
        {
            DataContext = new RestoreTargetViewModel("PostgreSQL on smoke-server"),
        })),

        new("IsbnPrompt", _ => Task.FromResult<Control>(new IsbnPromptDialog
        {
            DataContext = new IsbnPromptViewModel("Smoke Book"),
        })),

        new("DuplicateResolution", _ => Task.FromResult<Control>(new DuplicateResolutionDialog
        {
            DataContext = new DuplicateResolutionViewModel("Smoke title", "Smoke body."),
        })),

        new("Progress", _ => Task.FromResult<Control>(new ProgressWindow
        {
            DataContext = new ProgressWindowViewModel("Smoke header"),
        })),

        new("ProgressCard", _ => Task.FromResult<Control>(new ProgressWindow
        {
            DataContext = new ProgressWindowViewModel("Smoke header", isCard: true),
        })),
    ];

    public static IEnumerable<Surface> All => Windows.Concat(Panes).Concat(Dialogs);

    public static Surface ByName(string name) =>
        All.FirstOrDefault(s => s.Name == name) ?? throw new ArgumentException($"Unknown surface '{name}'.");

    private static Task<Control> Pane(Control view, object viewModel)
    {
        view.DataContext = viewModel;
        return Task.FromResult(view);
    }
}
