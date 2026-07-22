using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using BookDB.Desktop.ViewModels;
using BookDB.Desktop.Views;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
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

            // Text fields typed with real key input. The author row's AutoCompleteBox brings
            // template-internal TextBoxes into the visual tree, so keep only the dialog's own
            // (non-templated) boxes: Title, ISBN, Year in tree order — the author goes through
            // the row VM instead (the type-ahead flow has its own coverage).
            var fields = dialog.Descendants<TextBox>().Where(t => t.TemplatedParent is null).ToList();
            Assert.Equal(3, fields.Count);
            dialog.TypeInto(fields[0], "Mistborn");
            dialog.TypeInto(fields[1], "9780765311788");
            dialog.TypeInto(fields[2], "2006");
            var authorRow = Assert.Single(vm.AuthorRows);
            authorRow.SearchText = "Brandon Sanderson";
            Assert.Equal("Mistborn", vm.Title);
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
    public async Task ManualSave_ReusesAnExistingPersonCaseInsensitively_AndCreatesTheUnknownOne()
    {
        var ct = TestContext.Current.CancellationToken;

        await RunUi(async () =>
        {
            using var host = TestHost.Create();
            await SeedData.AddBookAsync(host, "Seed Book", ["Orig Author"], ct);

            var vm = host.Resolve<AddBookDialogViewModel>();
            vm.Reset(null);
            await vm.InitializeAsync();
            vm.CloseDialog = _ => { };
            vm.Title = "Manual Book";

            // Case-different spelling must reuse the seeded person; the second row is new.
            vm.AuthorRows[0].SearchText = "orig author";
            Assert.True(vm.AuthorRows[0].IsExistingPerson);
            vm.AddAuthorRowCommand.Execute(null);
            vm.AuthorRows[1].SearchText = "Brand New Person";
            Assert.True(vm.AuthorRows[1].IsNewPerson);

            await vm.SaveCommand.ExecuteAsync(null);

            var books = host.Resolve<BookDB.Logic.Services.IBookService>();
            var people = await books.GetPeopleAsync(ct);
            Assert.Equal(2, people.Count);

            var saved = await books.GetBookByIdAsync(await FindBookIdAsync(host, "Manual Book", ct), ct);
            Assert.NotNull(saved);
            Assert.Equal(2, saved.Contributors.Count);
            var seededPerson = people.Single(p => p.DisplayName == "Orig Author");
            Assert.Contains(saved.Contributors, c => c.PersonId == seededPerson.PersonId);
        });
    }

    [Fact]
    public async Task SaveAndOpenEditor_SavesTheBook_ThenHandsItToTheFullEditor()
    {
        var ct = TestContext.Current.CancellationToken;

        await RunUi(async () =>
        {
            var windowService = Substitute.For<BookDB.Desktop.Services.IWindowService>();
            using var host = TestHost.Create(s => s.AddSingleton(windowService));

            var vm = host.Resolve<AddBookDialogViewModel>();
            vm.Reset(null);
            await vm.InitializeAsync();
            bool? closed = null;
            vm.CloseDialog = r => closed = r;
            vm.Title = "Editor Bound";

            await vm.SaveAndOpenEditorCommand.ExecuteAsync(null);

            Assert.True(closed);
            var bookId = await FindBookIdAsync(host, "Editor Bound", ct);
            await windowService.Received(1).OpenFullDetailsWindowAsync(bookId);
        });
    }

    private static async Task<int> FindBookIdAsync(TestHost host, string title, System.Threading.CancellationToken ct)
    {
        var list = host.Resolve<BookListViewModel>();
        await list.LoadBooksAsync(ct);
        return list.Books.Single(b => b.Title == title).BookId;
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
