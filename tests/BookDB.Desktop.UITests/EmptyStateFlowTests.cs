using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using BookDB.Desktop.Localization;
using BookDB.Desktop.Messages;
using BookDB.Desktop.Services;
using BookDB.Desktop.ViewModels;
using BookDB.Desktop.Views;
using BookDB.Logic.Services;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace BookDB.Desktop.UITests;

/// <summary>
/// The book list distinguishes a truly empty library (guided panel: add / import / connect, plus a help pointer)
/// from a filter or search that matched nothing (plain "no matches" text). Both states track the live book count
/// with no restart or manual refresh, and the guided panel's actions drive the same window-service seams the menu
/// and toolbar use.
/// </summary>
public class EmptyStateFlowTests : HeadlessTest
{
    [Fact]
    public async Task EmptyLibrary_ShowsGuidedPanel_AndActionsDriveTheirSeams()
    {
        var ct = TestContext.Current.CancellationToken;
        var windowService = Substitute.For<IWindowService>();

        await RunUi(async () =>
        {
            using var host = TestHost.Create(s => s.AddSingleton(windowService));

            var list = host.Resolve<BookListViewModel>();
            var view = new BookListView { DataContext = list };
            var window = view.Host();
            await list.LoadBooksAsync(ct);
            Ui.Pump();

            // A fresh library: guided panel up, plain "no matches" text down.
            Assert.True(list.IsLibraryEmpty);
            Assert.False(list.IsFilteredEmpty);
            Assert.True(VisibleTextBlock(view, Resources.BookList_Empty_Title));
            Assert.False(VisibleTextBlock(view, Resources.BookList_EmptyState));

            // Each action routes to the same seam the menu/toolbar drive.
            await Ui.ClickAsync(view.ButtonFor(list.AddBookCommand));
            await windowService.Received(1).ShowAddBookIdentifyDialogAsync(Arg.Any<int?>());

            await Ui.ClickAsync(view.ButtonFor(list.ImportBooksCommand));
            await windowService.Received(1).ShowImportWizardAsync(Arg.Any<string?>());

            await Ui.ClickAsync(view.ButtonFor(list.ConnectDatabaseCommand));
            await windowService.Received(1).ShowSettingsAsync(Arg.Any<Window?>(), SettingsSection.Database);

            await Ui.ClickAsync(view.ButtonFor(list.OpenGettingStartedHelpCommand));
            windowService.Received(1).OpenHelpWindow(BookDB.Help.HelpTab.GettingStarted);
        });
    }

    [Fact]
    public async Task SearchMatchingNothing_ShowsPlainMessage_NotGuidedPanel()
    {
        var ct = TestContext.Current.CancellationToken;

        await RunUi(async () =>
        {
            using var host = TestHost.Create();
            await SeedData.AddBookAsync(host, "The Only Book", ct);

            var list = host.Resolve<BookListViewModel>();
            var view = new BookListView { DataContext = list };
            var window = view.Host();
            await list.LoadBooksAsync(ct);
            Assert.Single(list.Books);

            // An advanced search that ran and matched nothing (empty, non-null result) filters every row away.
            list.Receive(new AdvancedSearchResultMessage(new List<long>()));
            await list.LoadBooksAsync(ct);
            Ui.Pump();

            Assert.Empty(list.Books);
            Assert.True(list.IsFilteredEmpty);
            Assert.False(list.IsLibraryEmpty);
            Assert.True(VisibleTextBlock(view, Resources.BookList_EmptyState));
            Assert.False(VisibleTextBlock(view, Resources.BookList_Empty_Title));
        });
    }

    [Fact]
    public async Task GuidedPanel_FlipsLive_AsTheLastBookIsAddedAndRemoved()
    {
        var ct = TestContext.Current.CancellationToken;

        await RunUi(async () =>
        {
            using var host = TestHost.Create();

            var list = host.Resolve<BookListViewModel>();
            var view = new BookListView { DataContext = list };
            var window = view.Host();
            await list.LoadBooksAsync(ct);
            Assert.True(list.IsLibraryEmpty);
            Assert.True(VisibleTextBlock(view, Resources.BookList_Empty_Title));

            // Add the first book → the panel gives way to the grid.
            var book = await SeedData.AddBookAsync(host, "First Arrival", ct);
            await list.LoadBooksAsync(ct);
            Ui.Pump();
            Assert.False(list.IsLibraryEmpty);
            Assert.False(VisibleTextBlock(view, Resources.BookList_Empty_Title));

            // Remove it via the real delete path — the service delete plus the BooksDeletedMessage the delete
            // command sends. The handler reconciles the totals from the database, so the panel returns without
            // the caller reloading (deleting the last book used to leave the empty state hidden).
            await host.Resolve<IBookService>().DeleteBooksAsync(new[] { book.BookId }, ct);
            list.Receive(new BooksDeletedMessage(new[] { book.BookId }));
            await Ui.PumpUntil(() => list.IsLibraryEmpty, ct);
            Assert.True(VisibleTextBlock(view, Resources.BookList_Empty_Title));
        });
    }

    private static bool VisibleTextBlock(Visual root, string text) =>
        root.Descendants<TextBlock>().Any(t => t.IsEffectivelyVisible && t.Text == text);
}
