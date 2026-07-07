using System.Linq;
using System.Threading.Tasks;
using BookDB.Desktop.Services;
using BookDB.Desktop.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace BookDB.Desktop.UITests;

/// <summary>
/// The list's Copy ISBN / Copy Title context-menu commands against a faked clipboard: both are gated on a
/// selection, multi-selections join one value per line, books without an ISBN are skipped, and a selection
/// with nothing to copy never touches the clipboard.
/// </summary>
public class ClipboardCopyFlowTests : HeadlessTest
{
    [Fact]
    public async Task CopyIsbnAndTitle_FollowTheSelection()
    {
        var ct = TestContext.Current.CancellationToken;
        var clipboard = Substitute.For<IClipboardService>();
        string? copied = null;
        clipboard.SetTextAsync(Arg.Do<string>(t => copied = t)).Returns(Task.CompletedTask);

        await RunUi(async () =>
        {
            using var host = TestHost.Create(s => s.AddSingleton(clipboard));
            await SeedData.AddBookAsync(host, "Dune", "9780441013593", ct);
            await SeedData.AddBookAsync(host, "Emma", "9780141439587", ct);
            await SeedData.AddBookAsync(host, "No Isbn Book", ct);

            var list = host.Resolve<BookListViewModel>();
            await list.LoadBooksAsync(ct);

            // No selection: both commands are gated.
            list.UpdateSelectedBooks(System.Array.Empty<BookRowViewModel>());
            Assert.False(list.CopyIsbnCommand.CanExecute(null));
            Assert.False(list.CopyTitleCommand.CanExecute(null));

            // Single selection copies its ISBN, then its title.
            var dune = list.Books.Single(b => b.Title == "Dune");
            list.UpdateSelectedBooks(new[] { dune });
            Assert.True(list.CopyIsbnCommand.CanExecute(null));
            await list.CopyIsbnCommand.ExecuteAsync(null);
            Assert.Equal("9780441013593", copied);
            await list.CopyTitleCommand.ExecuteAsync(null);
            Assert.Equal("Dune", copied);

            // A multi-selection joins one value per line; the ISBN copy skips the book without one.
            var emma = list.Books.Single(b => b.Title == "Emma");
            var noIsbn = list.Books.Single(b => b.Title == "No Isbn Book");
            list.UpdateSelectedBooks(new[] { dune, emma, noIsbn });
            await list.CopyIsbnCommand.ExecuteAsync(null);
            Assert.Equal("9780441013593\n9780141439587", copied);
            await list.CopyTitleCommand.ExecuteAsync(null);
            Assert.Equal("Dune\nEmma\nNo Isbn Book", copied);

            // A selection with no ISBNs at all leaves the clipboard untouched.
            clipboard.ClearReceivedCalls();
            copied = null;
            list.UpdateSelectedBooks(new[] { noIsbn });
            await list.CopyIsbnCommand.ExecuteAsync(null);
            await clipboard.DidNotReceiveWithAnyArgs().SetTextAsync(default!);
            Assert.Null(copied);
        });
    }
}
