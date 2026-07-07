using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using BookDB.Desktop.ViewModels;
using BookDB.Desktop.Views;
using BookDB.Logic.Services;
using CommunityToolkit.Mvvm.Input;
using Xunit;

namespace BookDB.Desktop.UITests;

/// <summary>
/// Check-out / return journey through the real dialog, with the borrower autocomplete driven key-by-key. The
/// motivating regression: a per-keystroke async populator used to drop characters on a slow backend, so every
/// keystroke must accumulate in the bound text. Exercises the borrower field, the due-date field, and both buttons.
/// </summary>
public class CheckOutFlowTests : HeadlessTest
{
    [Fact]
    public async Task TypingBorrowerKeyByKey_RetainsEveryCharacterThenChecksOutAndReturns()
    {
        var ct = TestContext.Current.CancellationToken;

        await RunUi(async () =>
        {
            using var host = TestHost.Create();
            var book = await SeedData.AddBookAsync(host, "Loanable Book", ct);
            await SeedData.AddBorrowerAsync(host, "Ada", "Lovelace", ct);

            var vm = host.Resolve<CheckOutDialogViewModel>();
            await vm.InitializeAsync(book.BookId);
            bool? closed = null;
            vm.CloseDialog = r => closed = r;
            var dialog = new CheckOutDialog { DataContext = vm };
            dialog.Show();
            Ui.Pump();

            // Type the borrower name one character at a time; the bound text must retain every character so far.
            var input = dialog.Find<AutoCompleteBox>().Descendants<TextBox>().First();
            input.Focus();
            Ui.Pump();

            const string name = "Ada Lovelace";
            for (var i = 0; i < name.Length; i++)
            {
                dialog.KeyTextInput(name[i].ToString());
                Ui.Pump();
                Assert.Equal(name[..(i + 1)], vm.SearchText);
            }

            // Due-date field, then confirm via the real button (enabled once a borrower is typed).
            vm.DueDate = new DateTimeOffset(new DateTime(2030, 1, 15));
            var confirm = dialog.ButtonFor(vm.ConfirmCommand);
            Assert.True(confirm.IsEnabled);
            await ((IAsyncRelayCommand)confirm.Command!).ExecuteAsync(null);
            Assert.True(closed);

            var loanService = host.Resolve<ILoanService>();
            var active = await loanService.GetActiveLoanAsync(book.BookId, ct);
            Assert.NotNull(active);
            Assert.Equal("Ada Lovelace", active!.Value.DisplayName);

            // Return the book through the list's check-in command; loan history then shows the completed loan.
            var list = host.Resolve<BookListViewModel>();
            await list.LoadBooksAsync(ct);
            var row = list.Books.Single(b => b.BookId == book.BookId);
            Assert.True(row.IsLoaned);
            list.UpdateSelectedBooks(new[] { row });
            await ((IAsyncRelayCommand)list.CheckInCommand).ExecuteAsync(null);

            Assert.Null(await loanService.GetActiveLoanAsync(book.BookId, ct));
            var entry = Assert.Single(await loanService.GetLoanHistoryAsync(book.BookId, ct));
            Assert.Equal("Ada Lovelace", entry.BorrowerDisplayName);
            Assert.NotNull(entry.ReturnedDate);
            dialog.Close();
        });
    }

    [Fact]
    public async Task CancellingTheDialog_RecordsNoLoan()
    {
        var ct = TestContext.Current.CancellationToken;

        await RunUi(async () =>
        {
            using var host = TestHost.Create();
            var book = await SeedData.AddBookAsync(host, "Loanable Book", ct);

            var vm = host.Resolve<CheckOutDialogViewModel>();
            await vm.InitializeAsync(book.BookId);
            bool? closed = null;
            vm.CloseDialog = r => closed = r;
            var dialog = new CheckOutDialog { DataContext = vm };
            dialog.Show();
            Ui.Pump();

            var input = dialog.Find<AutoCompleteBox>().Descendants<TextBox>().First();
            input.Focus();
            Ui.Pump();
            dialog.KeyTextInput("Grace Hopper");
            Ui.Pump();
            Assert.Equal("Grace Hopper", vm.SearchText);

            var cancel = dialog.ButtonFor(vm.CancelCommand);
            cancel.Command!.Execute(null);
            Ui.Pump();

            Assert.False(closed);
            Assert.Null(await host.Resolve<ILoanService>().GetActiveLoanAsync(book.BookId, ct));
            dialog.Close();
        });
    }
}
