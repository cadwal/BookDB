using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using BookDB.Desktop.Services;
using BookDB.Desktop.Tests.Helpers;
using BookDB.Models.Entities;
using NSubstitute;
using Xunit;

namespace BookDB.Desktop.Tests.Services;

/// <summary>
/// The shared re-catalog flow: ISBN-bearing books enqueue directly; an ISBN-less book gets a prompt
/// and an offer to save the entered ISBN — accepting persists it on the record before enqueueing,
/// declining still enqueues, and cancelling the prompt skips just that book.
/// </summary>
public class RecatalogFlowServiceTests
{
    private static (RecatalogFlowService Flow, TestLookupServiceFactory Factory, IWindowService WindowService) CreateFlow()
    {
        var factory = new TestLookupServiceFactory();
        var windowService = Substitute.For<IWindowService>();
        return (new RecatalogFlowService(windowService, factory.BookService), factory, windowService);
    }

    [Fact]
    public async Task IsbnBearingBooks_EnqueueAsOneBatch_WithoutPrompting()
    {
        var (flow, factory, windowService) = CreateFlow();
        using (factory)
        {
            var first = await factory.BookService.AddBookAsync(new Book { Title = "First", Isbn = "9780441013593" }, TestContext.Current.CancellationToken);
            var second = await factory.BookService.AddBookAsync(new Book { Title = "Second", Isbn = "9780141439587" }, TestContext.Current.CancellationToken);

            await flow.RecatalogAsync([
                new RecatalogCandidate(first.BookId, first.Title, first.Isbn),
                new RecatalogCandidate(second.BookId, second.Title, second.Isbn)]);

            await windowService.Received(1).StartBatchRecatalogAsync(
                Arg.Is<IReadOnlyList<int>>(ids => ids.Count == 2 && ids[0] == first.BookId && ids[1] == second.BookId));
            await windowService.DidNotReceive().ShowIsbnPromptDialogAsync(Arg.Any<string>());
        }
    }

    [Fact]
    public async Task AcceptingTheSaveOffer_PersistsTheIsbn_ThenEnqueues()
    {
        var (flow, factory, windowService) = CreateFlow();
        using (factory)
        {
            var book = await factory.BookService.AddBookAsync(new Book { Title = "Nameless" }, TestContext.Current.CancellationToken);
            windowService.ShowIsbnPromptDialogAsync("Nameless").Returns("9780451526538");
            windowService.ShowConfirmAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Window?>())
                .Returns(true);

            await flow.RecatalogAsync([new RecatalogCandidate(book.BookId, book.Title, null)]);

            var persisted = await factory.BookService.GetBookByIdAsync(book.BookId, TestContext.Current.CancellationToken);
            Assert.Equal("9780451526538", persisted!.Isbn);
            await windowService.Received(1).StartBatchRecatalogAsync(book.BookId, "9780451526538");
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(null)]
    public async Task DecliningOrDismissingTheSaveOffer_EnqueuesWithoutPersisting(bool? saveAnswer)
    {
        var (flow, factory, windowService) = CreateFlow();
        using (factory)
        {
            var book = await factory.BookService.AddBookAsync(new Book { Title = "Nameless" }, TestContext.Current.CancellationToken);
            windowService.ShowIsbnPromptDialogAsync("Nameless").Returns("9780451526538");
            windowService.ShowConfirmAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Window?>())
                .Returns(saveAnswer);

            await flow.RecatalogAsync([new RecatalogCandidate(book.BookId, book.Title, null)]);

            var record = await factory.BookService.GetBookByIdAsync(book.BookId, TestContext.Current.CancellationToken);
            Assert.Null(record!.Isbn);
            await windowService.Received(1).StartBatchRecatalogAsync(book.BookId, "9780451526538");
        }
    }

    [Fact]
    public async Task CancellingThePrompt_SkipsJustThatBook()
    {
        var (flow, factory, windowService) = CreateFlow();
        using (factory)
        {
            var ct = TestContext.Current.CancellationToken;
            var withIsbn = await factory.BookService.AddBookAsync(new Book { Title = "Has Isbn", Isbn = "9780441013593" }, ct);
            var skipped = await factory.BookService.AddBookAsync(new Book { Title = "Skipped" }, ct);
            var answered = await factory.BookService.AddBookAsync(new Book { Title = "Answered" }, ct);
            windowService.ShowIsbnPromptDialogAsync("Skipped").Returns((string?)null);
            windowService.ShowIsbnPromptDialogAsync("Answered").Returns("9780451526538");

            await flow.RecatalogAsync([
                new RecatalogCandidate(withIsbn.BookId, withIsbn.Title, withIsbn.Isbn),
                new RecatalogCandidate(skipped.BookId, skipped.Title, null),
                new RecatalogCandidate(answered.BookId, answered.Title, null)]);

            // The ISBN-bearing book enqueues as a batch; each ISBN-less book gets its own prompt, and
            // the cancelled one is skipped without derailing the one answered after it.
            await windowService.Received(1).StartBatchRecatalogAsync(
                Arg.Is<IReadOnlyList<int>>(ids => ids.Count == 1 && ids[0] == withIsbn.BookId));
            await windowService.Received(1).ShowIsbnPromptDialogAsync("Skipped");
            await windowService.DidNotReceive().StartBatchRecatalogAsync(skipped.BookId, Arg.Any<string>());
            await windowService.Received(1).StartBatchRecatalogAsync(answered.BookId, "9780451526538");
        }
    }
}
