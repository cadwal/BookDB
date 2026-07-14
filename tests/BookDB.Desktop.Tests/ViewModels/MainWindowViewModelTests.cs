using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using BookDB.Desktop.Tests.Helpers;
using BookDB.Desktop.ViewModels;
using BookDB.Logic.Services;
using CommunityToolkit.Mvvm.Messaging;
using Xunit;

namespace BookDB.Desktop.Tests.ViewModels;

public class MainWindowViewModelTests
{
    private static (MainWindowViewModel vm, TestLookupServiceFactory factory) CreateTestViewModel()
    {
        var factory = new TestLookupServiceFactory();
        var messenger = new WeakReferenceMessenger();
        var windowService = new TestLookupServiceFactory.NullWindowService();
        var filePickerService = new TestLookupServiceFactory.NullFilePickerService();
        var settingsService = (ISettingsService)factory.LookupService;
        var filterPanel = new FilterPanelViewModel(messenger, factory.BookService, factory.BookSearchService, settingsService, windowService);
        var loanService = NSubstitute.Substitute.For<ILoanService>();
        var recatalogFlow = NSubstitute.Substitute.For<BookDB.Desktop.Services.IRecatalogFlowService>();
        var bookList = new BookListViewModel(messenger, factory.BookService, factory.BookSearchService, factory.BookImageService, windowService, settingsService, factory.LookupService, new TestLookupServiceFactory.NullClipboardService(), loanService, NSubstitute.Substitute.For<BookDB.Logic.Services.IConnectionHealthMonitor>(), NSubstitute.Substitute.For<BookDB.Data.Interfaces.IConnectionFailureClassifier>(), recatalogFlow);
        var bookDetail = new BookDetailViewModel(messenger, factory.BookService, factory.BookImageService, factory.LookupService, windowService, filePickerService, new Helpers.PassThroughWriteGuard(), NSubstitute.Substitute.For<System.Net.Http.IHttpClientFactory>(), loanService, NSubstitute.Substitute.For<BookDB.Logic.Services.IConnectionHealthMonitor>(), NSubstitute.Substitute.For<BookDB.Data.Interfaces.IConnectionFailureClassifier>(), recatalogFlow);
        var collectionSelector = new CollectionSelectorViewModel(messenger);
        var vm = new MainWindowViewModel(filterPanel, bookList, bookDetail, collectionSelector, factory.LookupService, windowService, messenger,
            filePickerService, new TestLookupServiceFactory.NullBackupService(), new TestLookupServiceFactory.NullCsvExportService(), settingsService, new TestLookupServiceFactory.NullPrintService(),
            NSubstitute.Substitute.For<BookDB.Desktop.Services.IApplicationRestartService>(),
            NSubstitute.Substitute.For<BookDB.Logic.Services.IConnectionHealthMonitor>(),
            NSubstitute.Substitute.For<BookDB.Data.Interfaces.IConnectionFailureClassifier>(),
            NSubstitute.Substitute.For<BookDB.Logic.Services.ICsvArchiveRestoreService>(),
            NSubstitute.Substitute.For<BookDB.Desktop.Services.IBootstrapConfigService>(),
            new BookDB.Models.AppSettings(),
            NSubstitute.Substitute.For<BookDB.Desktop.Services.IMigrationTargetBuilder>(),
            NSubstitute.Substitute.For<BookDB.Data.Interfaces.ISecretStore>(),
            NSubstitute.Substitute.For<BookDB.Desktop.Services.IReleaseNotesService>());
        return (vm, factory);
    }

    [Fact]
    public void DefaultPaneWidths_AppliedBeforeInit()
    {
        var (vm, factory) = CreateTestViewModel();
        using (factory)
        {
            Assert.Equal(200.0, vm.FilterPanelWidth);
            Assert.Equal(400.0, vm.DetailPanelWidth);
            Assert.True(vm.DetailPanelVisible);
        }
    }

    [Fact]
    public async Task InitializeAsync_LoadsWidthsFromSettings()
    {
        var (vm, factory) = CreateTestViewModel();
        using (factory)
        {
            await factory.SeedCollectionsAsync((1, "Library", 0), (2, "Wishlist", 1), (3, "Favorites", 2));
            await ((ISettingsService)factory.LookupService).SetAsync("FilterPanelWidth", "300", TestContext.Current.CancellationToken);
            await ((ISettingsService)factory.LookupService).SetAsync("DetailPanelWidth", "550", TestContext.Current.CancellationToken);
            await ((ISettingsService)factory.LookupService).SetAsync("DetailPanelVisible", "False", TestContext.Current.CancellationToken);

            await vm.InitializeAsync(TestContext.Current.CancellationToken);

            Assert.Equal(300.0, vm.FilterPanelWidth);
            Assert.Equal(550.0, vm.DetailPanelWidth);
            Assert.False(vm.DetailPanelVisible);
        }
    }

    [Fact]
    public async Task InitializeAsync_FirstRun_UsesDefaults_WhenNoSettings()
    {
        var (vm, factory) = CreateTestViewModel();
        using (factory)
        {
            await factory.SeedCollectionsAsync((1, "Library", 0), (2, "Wishlist", 1), (3, "Favorites", 2));

            await vm.InitializeAsync(TestContext.Current.CancellationToken);

            Assert.Equal(200.0, vm.FilterPanelWidth);
            Assert.Equal(400.0, vm.DetailPanelWidth);
            Assert.True(vm.DetailPanelVisible);
        }
    }

    [Fact]
    public async Task InitializeAsync_FirstRun_SelectsAllCollections()
    {
        var (vm, factory) = CreateTestViewModel();
        using (factory)
        {
            await factory.SeedCollectionsAsync((1, "Library", 0), (2, "Wishlist", 1), (3, "Favorites", 2));

            await vm.InitializeAsync(TestContext.Current.CancellationToken);

            Assert.Equal(3, vm.CollectionSelector.CollectionItems.Count);
            Assert.All(vm.CollectionSelector.CollectionItems, item => Assert.True(item.IsSelected));
        }
    }

    [Fact]
    public async Task InitializeAsync_RestoresCollectionSelection()
    {
        var (vm, factory) = CreateTestViewModel();
        using (factory)
        {
            await factory.SeedCollectionsAsync((1, "Library", 0), (2, "Wishlist", 1), (3, "Favorites", 2));
            await ((ISettingsService)factory.LookupService).SetAsync("LastSelectedCollectionIds", "1,3", TestContext.Current.CancellationToken);

            await vm.InitializeAsync(TestContext.Current.CancellationToken);

            var items = vm.CollectionSelector.CollectionItems;
            Assert.True(items.First(c => c.Id == 1).IsSelected);
            Assert.False(items.First(c => c.Id == 2).IsSelected);
            Assert.True(items.First(c => c.Id == 3).IsSelected);
        }
    }

    [Fact]
    public async Task PersistSettingsAsync_WritesAllSettings()
    {
        var (vm, factory) = CreateTestViewModel();
        using (factory)
        {
            await factory.SeedCollectionsAsync((1, "Library", 0), (2, "Wishlist", 1), (3, "Favorites", 2));

            await vm.InitializeAsync(TestContext.Current.CancellationToken);

            vm.FilterPanelWidth = 500.0;
            vm.DetailPanelWidth = 400.0;
            vm.DetailPanelVisible = false;

            await vm.PersistSettingsAsync(TestContext.Current.CancellationToken);

            var filterWidth = await ((ISettingsService)factory.LookupService).GetAsync("FilterPanelWidth", TestContext.Current.CancellationToken);
            var detailWidth = await ((ISettingsService)factory.LookupService).GetAsync("DetailPanelWidth", TestContext.Current.CancellationToken);
            var detailVisible = await ((ISettingsService)factory.LookupService).GetAsync("DetailPanelVisible", TestContext.Current.CancellationToken);

            Assert.Equal("500", filterWidth);
            Assert.Equal("400", detailWidth);
            Assert.Equal("False", detailVisible);
        }
    }

    [Fact]
    public async Task PersistSettingsAsync_RoundTrip_PreservesInvariantCulture()
    {
        var (vm, factory) = CreateTestViewModel();
        using (factory)
        {
            await factory.SeedCollectionsAsync((1, "Library", 0));

            await vm.InitializeAsync(TestContext.Current.CancellationToken);

            vm.FilterPanelWidth = 123.456;

            await vm.PersistSettingsAsync(TestContext.Current.CancellationToken);

            var raw = await ((ISettingsService)factory.LookupService).GetAsync("FilterPanelWidth", TestContext.Current.CancellationToken);

            // Dot must be used as decimal separator (not comma — critical for Swedish locale)
            Assert.Equal("123.456", raw);

            // Verify round-trip
            var parsed = double.Parse(raw!, NumberStyles.Float, CultureInfo.InvariantCulture);
            Assert.Equal(123.456, parsed, precision: 6);
        }
    }

    [Fact]
    public void DetailPanelToggleChevron_ReturnsCorrectCharacter()
    {
        var (vm, factory) = CreateTestViewModel();
        using (factory)
        {
            // Default: DetailPanelVisible == true, left-pointing
            Assert.Equal("\u00AB", vm.DetailPanelToggleChevron);

            vm.DetailPanelVisible = false;

            // When hidden: right-pointing
            Assert.Equal("\u00BB", vm.DetailPanelToggleChevron);
        }
    }

    [Fact]
    public void ChevronPropertyChanged_Fires_WhenDetailPanelVisibleChanges()
    {
        var (vm, factory) = CreateTestViewModel();
        using (factory)
        {
            var changedProperties = new System.Collections.Generic.List<string?>();
            vm.PropertyChanged += (_, args) => changedProperties.Add(args.PropertyName);

            vm.DetailPanelVisible = false;

            Assert.Contains(nameof(vm.DetailPanelToggleChevron), changedProperties);
        }
    }

    [Fact]
    public void WindowMenu_SeparatorInserted_WhenBothCategoriesPresent()
    {
        var (vm, factory) = CreateTestViewModel();
        using (factory)
        {
            var bookEditEntry = new OpenWindowEntry("Edit Book 1",
                new CommunityToolkit.Mvvm.Input.RelayCommand(() => { }),
                WindowCategory.BookEdit);
            var utilityEntry = new OpenWindowEntry("Statistics",
                new CommunityToolkit.Mvvm.Input.RelayCommand(() => { }),
                WindowCategory.Utility);

            vm.AddOpenWindow(bookEditEntry);
            vm.AddOpenWindow(utilityEntry);

            Assert.Contains(vm.OpenWindowEntries, e => e.IsSeparator);
        }
    }

    [Fact]
    public void WindowMenu_SeparatorRemoved_WhenOnlySingleCategoryRemains()
    {
        var (vm, factory) = CreateTestViewModel();
        using (factory)
        {
            var bookEditEntry = new OpenWindowEntry("Edit Book 1",
                new CommunityToolkit.Mvvm.Input.RelayCommand(() => { }),
                WindowCategory.BookEdit);
            var utilityEntry = new OpenWindowEntry("Statistics",
                new CommunityToolkit.Mvvm.Input.RelayCommand(() => { }),
                WindowCategory.Utility);

            vm.AddOpenWindow(bookEditEntry);
            vm.AddOpenWindow(utilityEntry);
            vm.RemoveOpenWindow(utilityEntry);

            Assert.DoesNotContain(vm.OpenWindowEntries, e => e.IsSeparator);
        }
    }

    [Fact]
    public void WindowMenu_GroupsBookEditBeforeUtility_WhicheverOpensFirst()
    {
        var (vm, factory) = CreateTestViewModel();
        using (factory)
        {
            var utility = new OpenWindowEntry("Statistics",
                new CommunityToolkit.Mvvm.Input.RelayCommand(() => { }), WindowCategory.Utility);
            var bookEdit = new OpenWindowEntry("Edit Book",
                new CommunityToolkit.Mvvm.Input.RelayCommand(() => { }), WindowCategory.BookEdit);

            // Utility opens first — the menu must still list the BookEdit group above the separator above Utility.
            vm.AddOpenWindow(utility);
            vm.AddOpenWindow(bookEdit);

            var entries = vm.OpenWindowEntries.ToList();
            Assert.Single(entries, e => e.IsSeparator);
            Assert.True(entries.IndexOf(bookEdit) < entries.FindIndex(e => e.IsSeparator));
            Assert.True(entries.FindIndex(e => e.IsSeparator) < entries.IndexOf(utility));
        }
    }

    [Fact]
    public void WindowMenu_KeepsInsertionOrderWithinACategory_AndEntryActivationRunsItsCommand()
    {
        var (vm, factory) = CreateTestViewModel();
        using (factory)
        {
            var activated = 0;
            var first = new OpenWindowEntry("Statistics",
                new CommunityToolkit.Mvvm.Input.RelayCommand(() => { }), WindowCategory.Utility);
            var second = new OpenWindowEntry("Help",
                new CommunityToolkit.Mvvm.Input.RelayCommand(() => activated++), WindowCategory.Utility);

            vm.AddOpenWindow(first);
            vm.AddOpenWindow(second);

            Assert.Equal(new[] { first, second }, vm.OpenWindowEntries.Where(e => !e.IsSeparator));
            second.ActivateCommand.Execute(null);
            Assert.Equal(1, activated);
        }
    }

    [Fact]
    public void WindowMenu_ShowsLocalizedSentinel_OnlyWhenNoWindowsAreOpen()
    {
        var (vm, factory) = CreateTestViewModel();
        using (factory)
        {
            var sentinel = BookDB.Desktop.Localization.Resources.Menu_Window_NoOpenWindows;
            Assert.Equal(sentinel, Assert.Single(vm.OpenWindowEntries).Title);

            var entry = new OpenWindowEntry("Statistics",
                new CommunityToolkit.Mvvm.Input.RelayCommand(() => { }), WindowCategory.Utility);
            vm.AddOpenWindow(entry);
            Assert.DoesNotContain(vm.OpenWindowEntries, e => e.Title == sentinel);

            vm.RemoveOpenWindow(entry);
            Assert.Equal(sentinel, Assert.Single(vm.OpenWindowEntries).Title);
        }
    }
}
