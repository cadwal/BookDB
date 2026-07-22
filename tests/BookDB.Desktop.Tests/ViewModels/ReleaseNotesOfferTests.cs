using System.Threading.Tasks;
using BookDB.Desktop.Services;
using BookDB.Desktop.Tests.Helpers;
using BookDB.Desktop.ViewModels;
using BookDB.Logic.Services;
using CommunityToolkit.Mvvm.Messaging;
using NSubstitute;
using Xunit;

namespace BookDB.Desktop.Tests.ViewModels;

/// <summary>
/// The once-per-version release-notes offer: a fresh install seeds the version silently, Yes and
/// Skip record it as seen, deferring keeps the prompt returning, and a version without notes
/// never prompts.
/// </summary>
public class ReleaseNotesOfferTests
{
    private const string LastSeenKey = "ReleaseNotes.LastSeenVersion";

    private static (MainWindowViewModel Vm, TestLookupServiceFactory Factory,
        IWindowService WindowService, IReleaseNotesService ReleaseNotes) CreateViewModel()
    {
        var factory = new TestLookupServiceFactory();
        var messenger = new WeakReferenceMessenger();
        var nullWindowService = new TestLookupServiceFactory.NullWindowService();
        var windowService = Substitute.For<IWindowService>();
        var releaseNotes = Substitute.For<IReleaseNotesService>();
        var filePickerService = new TestLookupServiceFactory.NullFilePickerService();
        var settingsService = (ISettingsService)factory.LookupService;
        var filterPanel = new FilterPanelViewModel(messenger, factory.BookService, factory.BookSearchService, settingsService, nullWindowService);
        var loanService = Substitute.For<ILoanService>();
        var recatalogFlow = Substitute.For<IRecatalogFlowService>();
        var bookList = new BookListViewModel(messenger, factory.BookService, factory.BookSearchService, factory.BookImageService, nullWindowService, settingsService, factory.LookupService, new TestLookupServiceFactory.NullClipboardService(), loanService, Substitute.For<IConnectionHealthMonitor>(), Substitute.For<BookDB.Data.Interfaces.IConnectionFailureClassifier>(), recatalogFlow);
        var bookDetail = new BookDetailViewModel(messenger, factory.BookService, factory.BookImageService, factory.LookupService, nullWindowService, filePickerService, new Helpers.PassThroughWriteGuard(), Substitute.For<System.Net.Http.IHttpClientFactory>(), loanService, Substitute.For<IConnectionHealthMonitor>(), Substitute.For<BookDB.Data.Interfaces.IConnectionFailureClassifier>(), recatalogFlow);
        var collectionSelector = new CollectionSelectorViewModel(messenger);
        var vm = new MainWindowViewModel(filterPanel, bookList, bookDetail, collectionSelector, factory.LookupService, windowService, messenger,
            filePickerService, new TestLookupServiceFactory.NullBackupService(), new TestLookupServiceFactory.NullCsvExportService(), settingsService, new TestLookupServiceFactory.NullPrintService(),
            Substitute.For<IApplicationRestartService>(),
            Substitute.For<IConnectionHealthMonitor>(),
            Substitute.For<BookDB.Data.Interfaces.IConnectionFailureClassifier>(),
            Substitute.For<ICsvArchiveRestoreService>(),
            Substitute.For<IBootstrapConfigService>(),
            new BookDB.Models.AppSettings(),
            Substitute.For<IMigrationTargetBuilder>(),
            Substitute.For<BookDB.Data.Interfaces.ISecretStore>(),
            releaseNotes,
            Substitute.For<BookDB.Desktop.Services.UpdateCheck.IUpdateCheckService>());
        return (vm, factory, windowService, releaseNotes);
    }

    [Fact]
    public async Task FreshInstall_SeedsTheVersionSilently_WithoutPrompting()
    {
        var (vm, factory, windowService, releaseNotes) = CreateViewModel();
        using (factory)
        {
            releaseNotes.CurrentVersion.Returns("2.4.0");

            await vm.OfferReleaseNotesCommand.ExecuteAsync(null);

            Assert.Equal("2.4.0", await ((ISettingsService)factory.LookupService).GetAsync(LastSeenKey, TestContext.Current.CancellationToken));
            await windowService.DidNotReceive().ShowReleaseNotesPromptAsync(Arg.Any<string>());
            await windowService.DidNotReceive().ShowReleaseNotesAsync(Arg.Any<string>(), Arg.Any<string>());
        }
    }

    [Fact]
    public async Task SameVersion_NeitherPromptsNorRewrites()
    {
        var (vm, factory, windowService, releaseNotes) = CreateViewModel();
        using (factory)
        {
            releaseNotes.CurrentVersion.Returns("2.4.0");
            await ((ISettingsService)factory.LookupService).SetAsync(LastSeenKey, "2.4.0", TestContext.Current.CancellationToken);

            await vm.OfferReleaseNotesCommand.ExecuteAsync(null);

            await windowService.DidNotReceive().ShowReleaseNotesPromptAsync(Arg.Any<string>());
            releaseNotes.DidNotReceive().GetNotes(Arg.Any<string>());
        }
    }

    [Fact]
    public async Task NewVersionWithoutNotes_NeitherPromptsNorRecords()
    {
        var (vm, factory, windowService, releaseNotes) = CreateViewModel();
        using (factory)
        {
            releaseNotes.CurrentVersion.Returns("2.4.0");
            releaseNotes.GetNotes("2.4.0").Returns((string?)null);
            await ((ISettingsService)factory.LookupService).SetAsync(LastSeenKey, "2.3.0", TestContext.Current.CancellationToken);

            await vm.OfferReleaseNotesCommand.ExecuteAsync(null);

            await windowService.DidNotReceive().ShowReleaseNotesPromptAsync(Arg.Any<string>());
            Assert.Equal("2.3.0", await ((ISettingsService)factory.LookupService).GetAsync(LastSeenKey, TestContext.Current.CancellationToken));
        }
    }

    [Fact]
    public async Task ChoosingShow_RecordsTheVersion_AndOpensTheViewer()
    {
        var (vm, factory, windowService, releaseNotes) = CreateViewModel();
        using (factory)
        {
            releaseNotes.CurrentVersion.Returns("2.4.0");
            releaseNotes.GetNotes("2.4.0").Returns("New things.");
            windowService.ShowReleaseNotesPromptAsync("2.4.0").Returns(ReleaseNotesChoice.Show);
            await ((ISettingsService)factory.LookupService).SetAsync(LastSeenKey, "2.3.0", TestContext.Current.CancellationToken);

            await vm.OfferReleaseNotesCommand.ExecuteAsync(null);

            Assert.Equal("2.4.0", await ((ISettingsService)factory.LookupService).GetAsync(LastSeenKey, TestContext.Current.CancellationToken));
            await windowService.Received(1).ShowReleaseNotesAsync("2.4.0", "New things.");
        }
    }

    [Fact]
    public async Task ChoosingSkip_RecordsTheVersion_WithoutTheViewer()
    {
        var (vm, factory, windowService, releaseNotes) = CreateViewModel();
        using (factory)
        {
            releaseNotes.CurrentVersion.Returns("2.4.0");
            releaseNotes.GetNotes("2.4.0").Returns("New things.");
            windowService.ShowReleaseNotesPromptAsync("2.4.0").Returns(ReleaseNotesChoice.Skip);
            await ((ISettingsService)factory.LookupService).SetAsync(LastSeenKey, "2.3.0", TestContext.Current.CancellationToken);

            await vm.OfferReleaseNotesCommand.ExecuteAsync(null);

            Assert.Equal("2.4.0", await ((ISettingsService)factory.LookupService).GetAsync(LastSeenKey, TestContext.Current.CancellationToken));
            await windowService.DidNotReceive().ShowReleaseNotesAsync(Arg.Any<string>(), Arg.Any<string>());
        }
    }

    [Fact]
    public async Task Deferring_LeavesTheVersionUnrecorded_SoTheNextStartAsksAgain()
    {
        var (vm, factory, windowService, releaseNotes) = CreateViewModel();
        using (factory)
        {
            releaseNotes.CurrentVersion.Returns("2.4.0");
            releaseNotes.GetNotes("2.4.0").Returns("New things.");
            windowService.ShowReleaseNotesPromptAsync("2.4.0").Returns(ReleaseNotesChoice.Defer);
            await ((ISettingsService)factory.LookupService).SetAsync(LastSeenKey, "2.3.0", TestContext.Current.CancellationToken);

            await vm.OfferReleaseNotesCommand.ExecuteAsync(null);
            Assert.Equal("2.3.0", await ((ISettingsService)factory.LookupService).GetAsync(LastSeenKey, TestContext.Current.CancellationToken));
            await windowService.DidNotReceive().ShowReleaseNotesAsync(Arg.Any<string>(), Arg.Any<string>());

            await vm.OfferReleaseNotesCommand.ExecuteAsync(null);
            await windowService.Received(2).ShowReleaseNotesPromptAsync("2.4.0");
        }
    }
}
