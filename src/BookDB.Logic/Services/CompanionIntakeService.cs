using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BookDB.Data.DbContexts;
using BookDB.Isbn;
using BookDB.Models;
using BookDB.Models.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BookDB.Logic.Services;

/// <summary>
/// Turns a scanned item into library content: a new ISBN becomes a placeholder book queued for
/// cataloguing, and a known ISBN gets the photos added to the book that is already there. Photos are
/// appended, never replacing what the user already had.
/// </summary>
public sealed class CompanionIntakeService : ICompanionIntakeService
{
    private readonly IDbContextFactory<BookDbContext> _factory;
    private readonly IBookMetadataService _metadata;
    private readonly BatchQueueService _queue;
    private readonly IBatchQueueProcessor _processor;
    private readonly ILogger<CompanionIntakeService> _logger;

    public CompanionIntakeService(
        IDbContextFactory<BookDbContext> factory,
        IBookMetadataService metadata,
        BatchQueueService queue,
        IBatchQueueProcessor processor,
        ILogger<CompanionIntakeService> logger)
    {
        _factory = factory;
        _metadata = metadata;
        _queue = queue;
        _processor = processor;
        _logger = logger;
    }

    public async Task<CompanionIntakeResult> IntakeAsync(CompanionScanItem item, CancellationToken ct = default)
    {
        string isbn = IsbnNormalizer.Normalize(item.Isbn ?? "");
        if (isbn.Length == 0)
        {
            return Failure(item, CompanionIntakeFailure.InvalidIsbn);
        }

        // A wrong check digit is not a reason to refuse: misprinted ISBNs exist on real books, and the
        // desktop treats the check digit as a warning everywhere else too.
        var existing = await _metadata.FindBookByIsbnAsync(isbn, ct).ConfigureAwait(false);

        try
        {
            return existing is null
                ? await CreateAsync(item, isbn, ct).ConfigureAwait(false)
                : await AttachAsync(item, existing.BookId, ct).ConfigureAwait(false);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Companion intake failed to store the scanned item.");
            return Failure(item, CompanionIntakeFailure.SaveFailed);
        }
    }

    private async Task<CompanionIntakeResult> CreateAsync(CompanionScanItem item, string isbn, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var book = new Book
        {
            // The ISBN stands in as the title until cataloguing replaces it; a blank title would be
            // indistinguishable from a broken row in the desktop list.
            Title = isbn,
            Isbn = isbn,
            Added = now,
            Updated = now,
        };

        await using (var db = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false))
        {
            db.Books.Add(book);

            int order = 0;
            bool hasPrimary = false;
            foreach (var image in item.Images)
            {
                bool primary = !hasPrimary && image.ImageTypeId == BookImageTypeId.FrontCover;
                hasPrimary |= primary;
                book.Images.Add(NewImage(image, primary, order++, now));
            }

            // One save for the book and all its photos: a failure anywhere leaves no half-written book.
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        var queued = await _queue.EnqueueRecatalogAsync([book.BookId], ct).ConfigureAwait(false);
        if (queued.Count > 0)
        {
            await _processor.EnqueueAsync(queued).ConfigureAwait(false);
        }

        return new CompanionIntakeResult(
            item.ClientItemId,
            CompanionIntakeOutcome.Created,
            book.BookId,
            queued.FirstOrDefault()?.BatchQueueItemId,
            CompanionIntakeFailure.None);
    }

    private async Task<CompanionIntakeResult> AttachAsync(CompanionScanItem item, int bookId, CancellationToken ct)
    {
        if (item.Images.Count == 0)
        {
            // An ISBN the library already has, with nothing to add: the phone says "already in your
            // library" and no row is touched.
            return new CompanionIntakeResult(
                item.ClientItemId, CompanionIntakeOutcome.AlreadyOwned, bookId, null, CompanionIntakeFailure.None);
        }

        await using var db = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);

        var current = await db.BookImages
            .Where(i => i.BookId == bookId)
            .Select(i => new { i.IsPrimary, i.DisplayOrder })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        bool hasPrimary = current.Any(i => i.IsPrimary);
        int order = current.Count == 0 ? 0 : current.Max(i => i.DisplayOrder) + 1;
        var now = DateTime.UtcNow;

        foreach (var image in item.Images)
        {
            bool primary = !hasPrimary && image.ImageTypeId == BookImageTypeId.FrontCover;
            hasPrimary |= primary;

            var row = NewImage(image, primary, order++, now);
            row.BookId = bookId;
            db.BookImages.Add(row);
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        // No recatalog: the book is already catalogued, and photos say nothing about its metadata.
        return new CompanionIntakeResult(
            item.ClientItemId, CompanionIntakeOutcome.AddedToExisting, bookId, null, CompanionIntakeFailure.None);
    }

    private static BookImage NewImage(CompanionScanImage image, bool isPrimary, int displayOrder, DateTime now) => new()
    {
        ImageData = image.Jpeg,
        MimeType = "image/jpeg",
        BookImageTypeId = image.ImageTypeId,
        IsPrimary = isPrimary,
        DisplayOrder = displayOrder,
        Added = now,
    };

    private static CompanionIntakeResult Failure(CompanionScanItem item, CompanionIntakeFailure failure)
        => new(item.ClientItemId, CompanionIntakeOutcome.Failed, null, null, failure);
}
