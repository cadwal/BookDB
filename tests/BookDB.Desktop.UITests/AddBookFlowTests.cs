using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using BookDB.Desktop.ViewModels;
using BookDB.Desktop.Views;
using CommunityToolkit.Mvvm.Input;
using Xunit;

namespace BookDB.Desktop.UITests;

/// <summary>
/// Add-a-book journey, exercising every field and both buttons of the dialog: typing each text field with real
/// key input, choosing a collection, then Save (persists everything and shows in the list) and Cancel (discards).
/// </summary>
public class AddBookFlowTests : HeadlessTest
{
    [Fact]
    public async Task FillingEveryFieldAndSaving_PersistsAllValuesAndShowsInList()
    {
        var ct = TestContext.Current.CancellationToken;

        await RunUi(async () =>
        {
            using var host = TestHost.Create();
            await SeedData.AddCollectionAsync(host, "UITest Collection A");
            var target = await SeedData.AddCollectionAsync(host, "UITest Collection B");

            var vm = host.Resolve<AddBookDialogViewModel>();
            vm.Reset(null);
            await vm.InitializeAsync();
            var dialog = new AddBookDialog { DataContext = vm };
            dialog.Show();
            Ui.Pump();

            // Every text field, typed with real key input (Title, Author, ISBN, Year — in tree order).
            var fields = dialog.Descendants<TextBox>();
            dialog.TypeInto(fields[0], "Mistborn");
            dialog.TypeInto(fields[1], "Brandon Sanderson");
            dialog.TypeInto(fields[2], "9780765311788");
            dialog.TypeInto(fields[3], "2006");
            Assert.Equal("Mistborn", vm.Title);
            Assert.Equal("Brandon Sanderson", vm.Author);
            Assert.Equal("9780765311788", vm.Isbn);
            Assert.Equal("2006", vm.Year);

            // Collection combo: pick the second collection and confirm the selection reaches the VM.
            var combo = dialog.Find<ComboBox>();
            combo.SelectedItem = vm.Collections.Single(c => c.Id == target.CollectionId);
            Ui.Pump();
            Assert.Equal(target.CollectionId, vm.SelectedCollectionId);

            // Save via its button — proves the button is wired to the command and enabled once a title is present.
            var saveButton = dialog.ButtonFor(vm.SaveCommand);
            Assert.True(saveButton.IsEnabled);
            await ((IAsyncRelayCommand)saveButton.Command!).ExecuteAsync(null);

            var list = host.Resolve<BookListViewModel>();
            await list.LoadBooksAsync(ct);
            var row = list.Books.Single(b => b.Title == "Mistborn");
            Assert.Equal("9780765311788", row.Isbn);
            Assert.Equal("2006", row.Year);
            Assert.Equal(target.CollectionId, row.CollectionId);
            Assert.Contains("Sanderson", row.AuthorDisplay ?? "");
            dialog.Close();
        });
    }

    [Fact]
    public async Task TypingThenCancelling_DiscardsWithoutSaving()
    {
        var ct = TestContext.Current.CancellationToken;

        await RunUi(async () =>
        {
            using var host = TestHost.Create();

            var vm = host.Resolve<AddBookDialogViewModel>();
            vm.Reset(null);
            await vm.InitializeAsync();
            bool? closedResult = null;
            vm.CloseDialog = r => closedResult = r;
            var dialog = new AddBookDialog { DataContext = vm };
            dialog.Show();
            Ui.Pump();

            dialog.TypeInto(dialog.Find<TextBox>(), "Discarded Title");

            var cancelButton = dialog.ButtonFor(vm.CancelCommand);
            cancelButton.Command!.Execute(null);
            Ui.Pump();

            Assert.False(closedResult); // Cancel closes the dialog reporting "not saved".
            var list = host.Resolve<BookListViewModel>();
            await list.LoadBooksAsync(ct);
            Assert.DoesNotContain(list.Books, b => b.Title == "Discarded Title");
            dialog.Close();
        });
    }
}
