using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BookDB.Logic.Services;

namespace BookDB.Desktop.Services;

/// <summary>A book offered for re-cataloging; Isbn is null or blank when the record has none.</summary>
public sealed record RecatalogCandidate(int BookId, string Title, string? Isbn);

public interface IRecatalogFlowService
{
    /// <summary>
    /// Re-catalogs the given books. ISBN-bearing books enqueue directly; each ISBN-less book gets an
    /// ISBN prompt naming it (Esc skips just that book) followed by an offer to save the entered ISBN
    /// on the record before enqueueing (declining still enqueues, matching the pre-save behaviour).
    /// </summary>
    Task RecatalogAsync(IReadOnlyList<RecatalogCandidate> books);
}

public sealed class RecatalogFlowService : IRecatalogFlowService
{
    private readonly IWindowService _windowService;
    private readonly IBookService _bookService;

    public RecatalogFlowService(IWindowService windowService, IBookService bookService)
    {
        _windowService = windowService;
        _bookService = bookService;
    }

    public async Task RecatalogAsync(IReadOnlyList<RecatalogCandidate> books)
    {
        var bookIdsWithIsbn = books
            .Where(b => !string.IsNullOrWhiteSpace(b.Isbn))
            .Select(b => b.BookId)
            .ToList();
        if (bookIdsWithIsbn.Count > 0)
            await _windowService.StartBatchRecatalogAsync(bookIdsWithIsbn);

        foreach (var book in books.Where(b => string.IsNullOrWhiteSpace(b.Isbn)))
            await RecatalogWithPromptAsync(book);
    }

    private async Task RecatalogWithPromptAsync(RecatalogCandidate book)
    {
        var isbn = await _windowService.ShowIsbnPromptDialogAsync(book.Title);
        if (string.IsNullOrWhiteSpace(isbn)) return;

        // Offering to persist keeps a failed lookup from leaving the record ISBN-less again; declining
        // (or closing the confirm) enqueues anyway — the lookup result is what saves the book either way.
        var save = await _windowService.ShowConfirmAsync(
            Localization.Resources.Recatalog_SaveIsbn_Title,
            string.Format(Localization.Resources.Recatalog_SaveIsbn_Body, isbn, book.Title));
        if (save == true)
        {
            var record = await _bookService.GetBookByIdAsync(book.BookId);
            if (record is not null)
            {
                record.Isbn = isbn;
                await _bookService.UpdateBookAsync(record);
            }
        }

        await _windowService.StartBatchRecatalogAsync(book.BookId, isbn);
    }
}
