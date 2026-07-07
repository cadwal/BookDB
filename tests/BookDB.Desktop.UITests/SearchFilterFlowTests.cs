using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using BookDB.Desktop.ViewModels;
using BookDB.Desktop.Views;
using CommunityToolkit.Mvvm.Input;
using Xunit;

namespace BookDB.Desktop.UITests;

/// <summary>
/// Search / facet-filter / clear journey across the two coordinating panes (book list + filter panel), which share
/// the app's single messenger. Exercises the search box and both clear buttons with real input, and guards the 2.1
/// regression where Clear filter hid every book by sending an empty (rather than null) advanced-search result.
/// </summary>
public class SearchFilterFlowTests : HeadlessTest
{
    [Fact]
    public async Task CheckingAFacetThenClearingFilters_RestoresTheFullList()
    {
        var ct = TestContext.Current.CancellationToken;

        await RunUi(async () =>
        {
            using var host = TestHost.Create();
            await SeedData.AddBookAsync(host, "The Alpha", new[] { "Ann Author" }, ct);
            await SeedData.AddBookAsync(host, "The Beta", new[] { "Bob Writer" }, ct);
            await SeedData.AddBookAsync(host, "The Gamma", new[] { "Bob Writer" }, ct);

            // Both panes resolve from the same host and share the singleton messenger, so a facet change on the
            // filter pane routes to the list pane exactly as it does in the running app.
            var list = host.Resolve<BookListViewModel>();
            var filter = host.Resolve<FilterPanelViewModel>();
            await list.InitializeAsync(ct);
            await list.LoadBooksAsync(ct);
            Assert.Equal(3, list.Books.Count);

            // Check the author shared by two books via the real IsChecked toggle → filter routes to the list.
            await filter.LoadFacetsAsync();
            var authorGroup = filter.FacetGroups.Single(g => g.FacetKey == "Author");
            var sharedAuthor = AllValues(authorGroup).Single(v => v.Count == 2);
            sharedAuthor.IsChecked = true;
            Ui.Pump();

            await list.LoadBooksAsync(ct);
            Assert.Equal(new[] { "The Beta", "The Gamma" }, list.Books.Select(b => b.Title).OrderBy(t => t));
            Assert.NotNull(list.ActiveFacetFilters);

            // Clear filters via the real button on the filter pane.
            var panel = new FilterPanelView { DataContext = filter }.Host();
            var clearButton = panel.ButtonFor(filter.ClearFiltersCommand);
            clearButton.Command!.Execute(null);
            Ui.Pump();

            // Regression: pre-fix this reloaded to zero rows (empty advanced-search payload read as "matched nothing").
            await list.LoadBooksAsync(ct);
            Assert.Equal(3, list.Books.Count);
            Assert.False(list.IsAdvancedSearchActive);
            Assert.False(sharedAuthor.IsChecked);
            panel.Close();
        });
    }

    [Fact]
    public async Task TheLoanedOutFilter_NarrowsToLoanedBooks_AndUncheckingRestores()
    {
        var ct = TestContext.Current.CancellationToken;

        await RunUi(async () =>
        {
            using var host = TestHost.Create();
            var loaned = await SeedData.AddBookAsync(host, "Out With A Friend", ct);
            await SeedData.AddBookAsync(host, "Safe At Home", ct);
            var borrower = await SeedData.AddBorrowerAsync(host, "Ada", "Lovelace", ct);
            await host.Resolve<BookDB.Logic.Services.ILoanService>()
                .CheckOutAsync(loaned.BookId, borrower.BorrowerId, null, ct);

            var list = host.Resolve<BookListViewModel>();
            var filter = host.Resolve<FilterPanelViewModel>();
            await list.LoadBooksAsync(ct);
            Assert.Equal(2, list.Books.Count);

            await filter.LoadFacetsAsync();
            var panel = new FilterPanelView { DataContext = filter }.Host();

            // The pseudo-filter is a real checkbox on the pane; checking it narrows to the loaned book.
            var loanedBox = panel.Descendants<CheckBox>()
                .Single(c => Equals(c.Content, BookDB.Desktop.Localization.Resources.Filter_LoanedOut));
            loanedBox.IsChecked = true;
            Ui.Pump();
            await list.LoadBooksAsync(ct);
            Assert.Equal("Out With A Friend", Assert.Single(list.Books).Title);

            loanedBox.IsChecked = false;
            Ui.Pump();
            await list.LoadBooksAsync(ct);
            Assert.Equal(2, list.Books.Count);
            panel.Close();
        });
    }

    [Fact]
    public async Task TypingInTheSearchBoxThenClearingSearch_NarrowsThenRestores()
    {
        var ct = TestContext.Current.CancellationToken;

        await RunUi(async () =>
        {
            using var host = TestHost.Create();
            await SeedData.AddBookAsync(host, "Mistborn", ct);
            await SeedData.AddBookAsync(host, "Elantris", ct);
            await SeedData.AddBookAsync(host, "Warbreaker", ct);

            var list = host.Resolve<BookListViewModel>();
            await list.InitializeAsync(ct);
            await list.LoadBooksAsync(ct);
            Assert.Equal(3, list.Books.Count);

            var view = new BookListView { DataContext = list };
            var window = view.Host();

            // Type into the real search box; the debounce timer then runs the FTS query and reloads the list.
            var searchBox = view.Descendants<TextBox>().First();
            window.TypeInto(searchBox, "Mistborn");
            Assert.Equal("Mistborn", list.SearchText);
            await Ui.PumpUntil(() => list.Books.Count == 1, ct);
            Assert.Equal("Mistborn", list.Books.Single().Title);

            // Clear the search via its button → the full list returns.
            var clearButton = view.ButtonFor(list.ClearSearchCommand);
            await ((IAsyncRelayCommand)clearButton.Command!).ExecuteAsync(null);
            Assert.Equal(string.Empty, list.SearchText);
            Assert.Equal(3, list.Books.Count);
            window.Close();
        });
    }

    [Fact]
    public async Task EachOfTheTenFacets_FiltersTheListToItsOwnBooks()
    {
        var ct = TestContext.Current.CancellationToken;

        await RunUi(async () =>
        {
            using var host = TestHost.Create();
            await FacetSample.SeedAsync(host, ct);

            var list = host.Resolve<BookListViewModel>();
            var filter = host.Resolve<FilterPanelViewModel>();
            await list.InitializeAsync(ct);
            await list.LoadBooksAsync(ct);
            Assert.Equal(3, list.Books.Count);

            await filter.LoadFacetsAsync();
            Assert.Equal(10, filter.FacetGroups.Count);

            // Every facet has one solo value (count 1) and one shared value (count 2). Checking the shared value of
            // each facet in turn must narrow the list to exactly the two shared books; unchecking restores all three.
            foreach (var group in filter.FacetGroups)
            {
                var shared = AllValues(group).Single(v => v.Count == 2);
                shared.IsChecked = true;
                Ui.Pump();

                await list.LoadBooksAsync(ct);
                Assert.Equal(
                    new[] { FacetSample.SharedTitleOne, FacetSample.SharedTitleTwo },
                    list.Books.Select(b => b.Title).OrderBy(t => t));

                shared.IsChecked = false;
                Ui.Pump();
                await list.LoadBooksAsync(ct);
                Assert.Equal(3, list.Books.Count);
            }
        });
    }

    // Grouped facets hold their values in the letter groups; flat facets hold them directly.
    private static IEnumerable<FacetValueViewModel> AllValues(FacetGroupViewModel group) =>
        group.IsGrouped
            ? group.LetterGroups.SelectMany(lg => lg.AllValues)
            : group.Values;
}
