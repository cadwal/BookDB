using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
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
/// Detail-pane editing — the second entry point into the shared edit form (FullDetails is the first). Selecting a
/// grid row loads the pane through the selection behavior's message; the pane's Edit button opens the inline form,
/// Save persists and returns to read mode (and the list hears about it), the list's Edit button re-opens the pane
/// straight into edit mode, and Cancel with unsaved changes routes through the discard dialog and reverts.
/// </summary>
public class DetailPaneEditFlowTests : HeadlessTest
{
    [Fact]
    public async Task GridSelectionLoadsThePane_EditSaves_ThenCancelDiscards()
    {
        var ct = TestContext.Current.CancellationToken;
        var windowService = Substitute.For<IWindowService>();
        windowService.ShowUnsavedChangesDialogAsync(Arg.Any<string>()).Returns(UnsavedChangesResult.Discard);

        await RunUi(async () =>
        {
            using var host = TestHost.Create(s => s.AddSingleton(windowService));
            var book = await host.Resolve<IBookService>()
                .AddBookAsync(new Book { Title = "Pane Subject", Subtitle = "First Edition" }, ct);

            var list = host.Resolve<BookListViewModel>();
            var listView = new BookListView { DataContext = list };
            var listWindow = listView.Host();
            await list.LoadBooksAsync(ct);
            Ui.Pump();

            var detail = host.Resolve<BookDetailViewModel>();
            var detailView = new BookDetailView { DataContext = detail };
            var detailWindow = detailView.Host();

            // Selecting the row in the real grid routes through the selection behavior to the pane.
            var grid = listView.Find<DataGrid>();
            grid.SelectedItem = list.Books.Single(b => b.BookId == book.BookId);
            await Ui.PumpUntil(() => detail.CurrentBook?.BookId == book.BookId, ct);

            // Read mode offers Edit; the edit form's Save/Cancel are not on screen yet.
            var editButton = detailView.ButtonFor(detail.EnterEditModeCommand);
            var saveButton = detailView.ButtonFor(detail.SaveCommand);
            Assert.True(editButton.IsEffectivelyVisible);
            Assert.False(saveButton.IsEffectivelyVisible);

            // Enter edit mode and retype the title through the embedded edit form.
            await Ui.ClickAsync(editButton);
            Assert.True(detail.IsEditMode);
            Assert.True(saveButton.IsEffectivelyVisible);
            var titleBox = detailView.Descendants<TextBox>().First(t => t.Text == "Pane Subject");
            detailWindow.RetypeInto(titleBox, "Pane Subject II");
            Assert.Equal("Pane Subject II", detail.EditTitle);

            // Save persists, returns to read mode, and the list shows the new title after its reload.
            await Ui.ClickAsync(saveButton);
            Assert.False(detail.IsEditMode);
            var saved = await host.Resolve<IBookService>().GetBookByIdAsync(book.BookId, ct);
            Assert.Equal("Pane Subject II", saved!.Title);
            await Ui.PumpUntil(() => list.Books.Any(b => b.Title == "Pane Subject II"), ct);

            // The list's Edit button re-opens the pane straight into edit mode.
            list.UpdateSelectedBooks(new[] { list.Books.Single(b => b.BookId == book.BookId) });
            await Ui.ClickAsync(listView.ButtonFor(list.EditBookCommand));
            await Ui.PumpUntil(() => detail.IsEditMode, ct);

            // Cancel with unsaved changes goes through the discard dialog and reverts everything.
            detail.EditSubtitle = "Second Edition";
            Assert.True(detail.HasUnsavedChanges);
            await Ui.ClickAsync(detailView.ButtonFor(detail.CancelEditCommand));
            await windowService.Received(1).ShowUnsavedChangesDialogAsync(Arg.Any<string>());
            Assert.False(detail.IsEditMode);
            var untouched = await host.Resolve<IBookService>().GetBookByIdAsync(book.BookId, ct);
            Assert.Equal("First Edition", untouched!.Subtitle);

            listWindow.Close();
            detailWindow.Close();
        });
    }

    [Fact]
    public async Task DoubleTappingARow_OpensThePaneInEditMode()
    {
        var ct = TestContext.Current.CancellationToken;

        await RunUi(async () =>
        {
            using var host = TestHost.Create();
            var book = await SeedData.AddBookAsync(host, "Tap Subject", ct);

            var list = host.Resolve<BookListViewModel>();
            var listView = new BookListView { DataContext = list };
            var listWindow = listView.Host();
            await list.LoadBooksAsync(ct);
            Ui.Pump();

            var detail = host.Resolve<BookDetailViewModel>();
            var detailView = new BookDetailView { DataContext = detail };
            var detailWindow = detailView.Host();

            // A real double-click on the row: the first click selects it, the second raises the
            // DoubleTapped gesture the behavior listens for.
            var row = listWindow.Descendants<DataGridRow>().First();
            var center = row.TranslatePoint(new Avalonia.Point(row.Bounds.Width / 2, row.Bounds.Height / 2), listWindow)!.Value;
            listWindow.MouseDown(center, MouseButton.Left);
            listWindow.MouseUp(center, MouseButton.Left);
            listWindow.MouseDown(center, MouseButton.Left);
            listWindow.MouseUp(center, MouseButton.Left);
            Ui.Pump();

            await Ui.PumpUntil(() => detail.IsEditMode && detail.CurrentBook?.BookId == book.BookId, ct);
            Assert.Equal("Tap Subject", detail.EditTitle);

            listWindow.Close();
            detailWindow.Close();
        });
    }

}
