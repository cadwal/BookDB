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
            // DoubleTapped gesture the behavior listens for. Re-clicked until the pane reacts: the
            // double-tap window is wall-clock, so a stalled runner can split one attempt's clicks past it.
            var row = listWindow.Descendants<DataGridRow>().First();
            await Ui.PumpUntil(() =>
            {
                if (!detail.IsEditMode)
                    listWindow.DoubleClick(row);
                return detail.IsEditMode && detail.CurrentBook?.BookId == book.BookId;
            }, ct);
            Assert.Equal("Tap Subject", detail.EditTitle);

            listWindow.Close();
            detailWindow.Close();
        });
    }

    [Fact]
    public async Task RecatalogFromThePane_PromptsForAnIsbnOnlyWhenTheBookHasNone()
    {
        var ct = TestContext.Current.CancellationToken;
        var windowService = Substitute.For<IWindowService>();
        windowService.ShowIsbnPromptDialogAsync("No Isbn Yet").Returns("9780451526538");

        await RunUi(async () =>
        {
            using var host = TestHost.Create(s => s.AddSingleton(windowService));
            var books = host.Resolve<IBookService>();
            var noIsbn = await books.AddBookAsync(new Book { Title = "No Isbn Yet" }, ct);

            var detail = host.Resolve<BookDetailViewModel>();
            var detailView = new BookDetailView { DataContext = detail };
            var detailWindow = detailView.Host();
            await detail.LoadBookAsync(noIsbn.BookId);
            Ui.Pump();

            // No ISBN on the record: the pane's Re-catalog button prompts (naming the book), offers to
            // save the entered ISBN, and enqueues it for this book instead of discarding it. The
            // unanswered save offer (substitute returns null) must still enqueue.
            await Ui.ClickAsync(detailView.ButtonFor(detail.RecatalogCommand));
            await windowService.Received(1).ShowIsbnPromptDialogAsync("No Isbn Yet");
            await windowService.Received(1).ShowConfirmAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Avalonia.Controls.Window?>());
            await windowService.Received(1).StartBatchRecatalogAsync(noIsbn.BookId, "9780451526538");

            // With an ISBN, re-catalog goes straight to the queue — no prompt.
            var withIsbn = await books.AddBookAsync(new Book { Title = "Has Isbn", Isbn = "9780062315007" }, ct);
            await detail.LoadBookAsync(withIsbn.BookId);
            Ui.Pump();
            await Ui.ClickAsync(detailView.ButtonFor(detail.RecatalogCommand));
            await windowService.Received(1).ShowIsbnPromptDialogAsync(Arg.Any<string>());
            await windowService.Received(1).StartBatchRecatalogAsync(
                Arg.Is<System.Collections.Generic.IReadOnlyList<int>>(ids => ids.Single() == withIsbn.BookId));

            detailWindow.Close();
        });
    }

}
