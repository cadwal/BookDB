using System.Linq;
using System.Threading.Tasks;
using Avalonia.Input;
using BookDB.Desktop.Localization;
using BookDB.Desktop.Services;
using BookDB.Desktop.ViewModels;
using BookDB.Desktop.Views;
using BookDB.Logic.Services;
using BookDB.Models.Entities;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace BookDB.Desktop.UITests;

/// <summary>
/// Destructive list actions. Delete asks for confirmation — naming the single book, or the count for a
/// multi-selection — and removes rows only on accept, whether triggered by the toolbar button or the DataGrid's
/// Delete key binding. Duplicate creates a copy that keeps the fields and contributors but drops the ISBN
/// (the partial unique index forbids a second book with the same one).
/// </summary>
public class DeleteDuplicateFlowTests : HeadlessTest
{
    [Fact]
    public async Task DeleteBooks_ConfirmsFirst_DeclineKeepsAcceptRemoves()
    {
        var ct = TestContext.Current.CancellationToken;
        var windowService = Substitute.For<IWindowService>();

        await RunUi(async () =>
        {
            using var host = TestHost.Create(s => s.AddSingleton(windowService));
            var one = await SeedData.AddBookAsync(host, "Doomed One", ct);
            var two = await SeedData.AddBookAsync(host, "Doomed Two", ct);
            await SeedData.AddBookAsync(host, "Survivor", ct);

            var list = host.Resolve<BookListViewModel>();
            var view = new BookListView { DataContext = list };
            var window = view.Host();
            await list.LoadBooksAsync(ct);
            Ui.Pump();

            var deleteButton = view.ButtonFor(list.DeleteBooksCommand);
            Assert.False(deleteButton.IsEffectivelyEnabled); // nothing selected

            // Single selection: the confirmation names the book; declining keeps everything.
            list.UpdateSelectedBooks(new[] { list.Books.Single(b => b.BookId == one.BookId) });
            Assert.True(deleteButton.IsEffectivelyEnabled);
            windowService.ShowDeleteConfirmationAsync(Arg.Any<string>()).Returns(false);
            await ((IAsyncRelayCommand)deleteButton.Command!).ExecuteAsync(null);
            await windowService.Received(1).ShowDeleteConfirmationAsync(
                string.Format(Resources.Delete_SingleBook_Message, "Doomed One"));
            Assert.Equal(3, list.Books.Count);

            // Multi selection: the confirmation states the count; accepting removes exactly those rows.
            list.UpdateSelectedBooks(list.Books.Where(b => b.BookId == one.BookId || b.BookId == two.BookId).ToList());
            windowService.ShowDeleteConfirmationAsync(Arg.Any<string>()).Returns(true);
            await ((IAsyncRelayCommand)deleteButton.Command!).ExecuteAsync(null);
            await windowService.Received(1).ShowDeleteConfirmationAsync(
                string.Format(Resources.Delete_MultipleBooks_Message, 2));

            await Ui.PumpUntil(() => list.Books.Count == 1, ct); // row removal is posted
            Assert.Equal("Survivor", list.Books.Single().Title);
            await list.LoadBooksAsync(ct); // re-read from the database — the rows are really gone
            Assert.Equal("Survivor", Assert.Single(list.Books).Title);
            window.Close();
        });
    }

    [Fact]
    public async Task DeleteBook_OnLoan_WarnsAboutLoanHistory_AndRemovesIt()
    {
        var ct = TestContext.Current.CancellationToken;
        var windowService = Substitute.For<IWindowService>();
        windowService.ShowDeleteConfirmationAsync(Arg.Any<string>()).Returns(true);

        await RunUi(async () =>
        {
            using var host = TestHost.Create(s => s.AddSingleton(windowService));
            var book = await SeedData.AddBookAsync(host, "Loaned Book", ct);
            var borrower = await SeedData.AddBorrowerAsync(host, "Ada", "Lovelace", ct);
            await host.Resolve<ILoanService>().CheckOutAsync(book.BookId, borrower.BorrowerId, null, ct);

            var list = host.Resolve<BookListViewModel>();
            var view = new BookListView { DataContext = list };
            var window = view.Host();
            await list.LoadBooksAsync(ct);
            Ui.Pump();

            var row = list.Books.Single(b => b.BookId == book.BookId);
            Assert.False(string.IsNullOrEmpty(row.LoanedToName)); // sanity: the row shows it as on loan

            list.UpdateSelectedBooks(new[] { row });
            await list.DeleteBooksCommand.ExecuteAsync(null);

            // The confirmation uses the loaned-out wording that names the borrower...
            await windowService.Received(1).ShowDeleteConfirmationAsync(
                string.Format(Resources.Delete_LoanedOut_Single_Message, "Loaned Book", row.LoanedToName));

            // ...and the delete goes through despite the Loan FK — its loan rows are removed first.
            await Ui.PumpUntil(() => list.Books.Count == 0, ct);
            await list.LoadBooksAsync(ct);
            Assert.Empty(list.Books);
            window.Close();
        });
    }

    [Fact]
    public async Task DeleteKey_OnTheGrid_RunsTheSameConfirmedDelete()
    {
        var ct = TestContext.Current.CancellationToken;
        var windowService = Substitute.For<IWindowService>();
        windowService.ShowDeleteConfirmationAsync(Arg.Any<string>()).Returns(true);

        await RunUi(async () =>
        {
            using var host = TestHost.Create(s => s.AddSingleton(windowService));
            var doomed = await SeedData.AddBookAsync(host, "Keyed Away", ct);
            await SeedData.AddBookAsync(host, "Keeper", ct);

            var list = host.Resolve<BookListViewModel>();
            var view = new BookListView { DataContext = list };
            var window = view.Host();
            await list.LoadBooksAsync(ct);
            Ui.Pump();

            list.UpdateSelectedBooks(new[] { list.Books.Single(b => b.BookId == doomed.BookId) });
            var grid = view.Find<Avalonia.Controls.DataGrid>();
            grid.Focus();
            Ui.Pump();
            window.Press(PhysicalKey.Delete);

            await Ui.PumpUntil(() => list.Books.Count == 1, ct);
            Assert.Equal("Keeper", list.Books.Single().Title);
            window.Close();
        });
    }

    [Fact]
    public async Task DuplicateBook_CopiesFieldsAndContributors_ButDropsTheIsbn()
    {
        var ct = TestContext.Current.CancellationToken;

        await RunUi(async () =>
        {
            using var host = TestHost.Create();
            var bookService = host.Resolve<IBookService>();
            var original = await bookService.AddBookWithContributorsAsync(
                new Book { Title = "Sole Copy", Isbn = "9781111111111", Pages = 321, Keywords = "space opera" },
                new[] { "Jane Writer" }, ct);
            await SeedData.AddBookAsync(host, "Bystander", ct);

            var list = host.Resolve<BookListViewModel>();
            await list.LoadBooksAsync(ct);

            // Duplicate is a single-selection action (context menu); it stays disabled for zero or many rows.
            Assert.False(list.DuplicateBookCommand.CanExecute(null));
            list.UpdateSelectedBooks(list.Books.ToList());
            Assert.False(list.DuplicateBookCommand.CanExecute(null));
            list.UpdateSelectedBooks(new[] { list.Books.Single(b => b.BookId == original.BookId) });
            Assert.True(list.DuplicateBookCommand.CanExecute(null));
            await list.DuplicateBookCommand.ExecuteAsync(null);

            await list.LoadBooksAsync(ct);
            var copyRow = list.Books.Single(b => b.Title == Resources.DuplicateBook_TitlePrefix + "Sole Copy");
            var copy = await bookService.GetBookByIdAsync(copyRow.BookId, ct);
            Assert.NotNull(copy);
            Assert.Null(copy!.Isbn); // the unique ISBN never travels to the copy
            Assert.Equal(321, copy.Pages);
            Assert.Equal("space opera", copy.Keywords);
            Assert.Equal(0, copy.ReadCount);
            Assert.Equal(1, copy.Copies);
            var originalAuthor = Assert.Single((await bookService.GetBookByIdAsync(original.BookId, ct))!.Contributors);
            Assert.Contains(copy.Contributors, c => c.PersonId == originalAuthor.PersonId);

            var kept = await bookService.GetBookByIdAsync(original.BookId, ct);
            Assert.Equal("9781111111111", kept!.Isbn); // the original is untouched
        });
    }
}
