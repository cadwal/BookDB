using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using BookDB.Desktop.Localization;
using BookDB.Desktop.ViewModels;
using BookDB.Desktop.Views;
using BookDB.Logic.Services;
using CommunityToolkit.Mvvm.Input;
using Xunit;

namespace BookDB.Desktop.UITests;

/// <summary>
/// Manage-borrowers journeys: add a borrower and fill the editor's fields, then rename and delete — asserting every
/// step round-trips through the borrower service; a borrower with loan history is protected from deletion and the
/// window says why.
/// </summary>
public class ManageBorrowersFlowTests : HeadlessTest
{
    [Fact]
    public async Task AddEditAndDeleteABorrower_RoundTripsEveryField()
    {
        var ct = TestContext.Current.CancellationToken;

        await RunUi(async () =>
        {
            using var host = TestHost.Create();
            var vm = host.Resolve<ManageBorrowersViewModel>();
            await vm.InitializeAsync();
            var window = new ManageBorrowersWindow { DataContext = vm };
            window.Show();
            Ui.Pump();

            // Add creates a stub and loads it into the editor; fill several fields and save.
            vm.AddCommand.Execute(null);
            var editor = vm.Editor;
            editor.FirstName = "Grace";
            editor.LastName = "Hopper";
            editor.Organization = "US Navy";
            editor.Email = "grace@example.test";
            editor.Phone1 = "555-0001";
            editor.City = "Arlington";
            editor.Country = "USA";
            Assert.True(editor.SaveCommand.CanExecute(null));
            await ((IAsyncRelayCommand)editor.SaveCommand).ExecuteAsync(null);

            var borrowerService = host.Resolve<IBorrowerService>();
            var saved = Assert.Single(await borrowerService.GetAllAsync(ct));
            Assert.Equal("Grace", saved.FirstName);
            Assert.Equal("Hopper", saved.LastName);
            Assert.Equal("US Navy", saved.Organization);
            Assert.Equal("grace@example.test", saved.Email);
            Assert.Equal("555-0001", saved.Phone1);
            Assert.Equal("Arlington", saved.City);
            Assert.Equal("USA", saved.Country);

            // Edit an existing borrower: reselect, rename, save.
            vm.SelectedBorrower = vm.AllBorrowers.First(b => b.BorrowerId == saved.BorrowerId);
            editor.LastName = "Hopper-Murray";
            await ((IAsyncRelayCommand)editor.SaveCommand).ExecuteAsync(null);
            var afterEdit = Assert.Single(await borrowerService.GetAllAsync(ct));
            Assert.Equal("Hopper-Murray", afterEdit.LastName);

            // Delete removes it.
            vm.SelectedBorrower = vm.AllBorrowers.First(b => b.BorrowerId == saved.BorrowerId);
            await ((IAsyncRelayCommand)vm.DeleteCommand).ExecuteAsync(null);
            Assert.Empty(await borrowerService.GetAllAsync(ct));
            window.Close();
        });
    }

    [Fact]
    public async Task ABorrowerWithLoanHistory_CannotBeDeleted_AndTheWindowSaysWhy()
    {
        var ct = TestContext.Current.CancellationToken;

        await RunUi(async () =>
        {
            using var host = TestHost.Create();
            var borrower = await SeedData.AddBorrowerAsync(host, "Busy", "Borrower", ct);
            var book = await SeedData.AddBookAsync(host, "Out On Loan", ct);
            await host.Resolve<ILoanService>().CheckOutAsync(book.BookId, borrower.BorrowerId, null, ct);

            var vm = host.Resolve<ManageBorrowersViewModel>();
            await vm.InitializeAsync();
            var window = new ManageBorrowersWindow { DataContext = vm };
            window.Show();
            Ui.Pump();

            vm.SelectedBorrower = vm.AllBorrowers.First(b => b.BorrowerId == borrower.BorrowerId);
            Ui.Pump();
            Assert.DoesNotContain(window.Descendants<TextBlock>(), // no warning before the attempt
                t => t.IsEffectivelyVisible && t.Text == Resources.ManageBorrowers_DeleteBlocked);

            await ((IAsyncRelayCommand)vm.DeleteCommand).ExecuteAsync(null);
            Ui.Pump();

            // The guard blocks the delete and the window tells the user why.
            Assert.Equal(Resources.ManageBorrowers_DeleteBlocked, vm.StatusMessage);
            Assert.Contains(window.Descendants<TextBlock>(),
                t => t.IsEffectivelyVisible && t.Text == Resources.ManageBorrowers_DeleteBlocked);
            Assert.Contains(vm.AllBorrowers, b => b.BorrowerId == borrower.BorrowerId);
            var stillThere = Assert.Single(await host.Resolve<IBorrowerService>().GetAllAsync(ct));
            Assert.Equal(borrower.BorrowerId, stillThere.BorrowerId);
            window.Close();
        });
    }
}
