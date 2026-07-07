using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using BookDB.Desktop.ViewModels;
using BookDB.Desktop.Views;
using BookDB.Logic.Services;
using BookDB.Models.Entities;
using Xunit;

namespace BookDB.Desktop.UITests;

/// <summary>
/// Collection scoping: the selector's checked set drives which books the list shows, an empty selection is
/// vetoed (the last valid selection is restored), and the context menu's Move-to-Collection reassigns the
/// selected books — moving a book out of the current scope makes it leave the visible list.
/// </summary>
public class CollectionScopeFlowTests : HeadlessTest
{
    [Fact]
    public async Task SelectorScopesTheList_MoveReassigns_EmptySelectionIsVetoed()
    {
        var ct = TestContext.Current.CancellationToken;

        await RunUi(async () =>
        {
            using var host = TestHost.Create();
            var shelfA = await SeedData.AddCollectionAsync(host, "Shelf A", ct);
            var shelfB = await SeedData.AddCollectionAsync(host, "Shelf B", ct);
            var bookService = host.Resolve<IBookService>();
            var inA = await bookService.AddBookAsync(new Book { Title = "In A", CollectionId = shelfA.CollectionId }, ct);
            await bookService.AddBookAsync(new Book { Title = "In B", CollectionId = shelfB.CollectionId }, ct);

            var list = host.Resolve<BookListViewModel>();
            await list.InitializeAsync(ct); // caches the collections for the Move-to submenu

            // First-run selector state: every collection checked (as MainWindow initializes it).
            var selector = host.Resolve<CollectionSelectorViewModel>();
            var view = new CollectionSelectorView { DataContext = selector };
            var window = view.Host();
            var collections = await host.Resolve<ILookupService>().GetCollectionsAsync(ct);
            selector.Initialize(collections, collections.Select(c => c.CollectionId).ToHashSet());
            await list.LoadBooksAsync(ct);
            Assert.Equal(2, list.Books.Count);

            // Unchecking Shelf B narrows the list to Shelf A's book; the summary names only checked shelves.
            selector.CollectionItems.Single(i => i.Id == shelfB.CollectionId).IsSelected = false;
            Ui.Pump();
            await list.LoadBooksAsync(ct);
            Assert.Equal("In A", Assert.Single(list.Books).Title);
            Assert.Contains("Shelf A", selector.SelectionSummary);
            Assert.DoesNotContain("Shelf B", selector.SelectionSummary);

            // Move-to-Collection: the submenu marks the book's current collection; moving it to the
            // (unchecked) Shelf B reassigns it and it drops out of the current scope.
            list.UpdateSelectedBooks(new[] { list.Books.Single() });
            var entries = list.CollectionMenuEntries;
            Assert.True(entries.Single(e => e.CollectionId == shelfA.CollectionId).IsCurrentCollection);
            Assert.False(entries.Single(e => e.CollectionId == shelfB.CollectionId).IsCurrentCollection);
            Assert.True(list.MoveToCollectionCommand.CanExecute(shelfB.CollectionId));
            await list.MoveToCollectionCommand.ExecuteAsync(shelfB.CollectionId);

            var moved = await bookService.GetBookByIdAsync(inA.BookId, ct);
            Assert.Equal(shelfB.CollectionId, moved!.CollectionId);
            await list.LoadBooksAsync(ct);
            Assert.Empty(list.Books); // moved outside the checked scope

            // Unchecking everything is vetoed: the last valid selection comes back (the revert is posted).
            foreach (var item in selector.CollectionItems.Where(i => i.IsSelected).ToList())
                item.IsSelected = false;
            await Ui.PumpUntil(() => selector.CollectionItems.Any(i => i.IsSelected), ct);
            window.Close();
        });
    }
}
