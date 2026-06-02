using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BookDB.Data.DbContexts;
using BookDB.Logic.Helpers;
using BookDB.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace BookDB.Logic.Services;

public sealed class BookImageService : IBookImageService
{
    private readonly IDbContextFactory<BookDbContext> _factory;

    public BookImageService(IDbContextFactory<BookDbContext> factory)
    {
        _factory = factory;
    }

    public async Task<byte[]?> GetBookPrimaryCoverBytesAsync(int bookId, CancellationToken ct = default)
    {
        await using var dbContext = await _factory.CreateDbContextAsync(ct);
        var image = await dbContext.BookImages
            .Where(i => i.BookId == bookId && i.IsPrimary)
            .Select(i => new { i.ImageData })
            .FirstOrDefaultAsync(ct);
        return image?.ImageData;
    }

    public async Task SavePrimaryBookImageAsync(int bookId, byte[] imageData, CancellationToken ct = default)
    {
        await using var dbContext = await _factory.CreateDbContextAsync(ct);

        var existingPrimary = await dbContext.BookImages
            .FirstOrDefaultAsync(i => i.BookId == bookId && i.IsPrimary, ct);
        if (existingPrimary != null)
            dbContext.BookImages.Remove(existingPrimary);

        dbContext.BookImages.Add(new BookImage
        {
            BookId = bookId,
            ImageData = imageData,
            MimeType = ImageHelpers.DetectMimeType(imageData),
            IsPrimary = true,
            DisplayOrder = 0,
            Added = DateTime.UtcNow
        });
        await dbContext.SaveChangesAsync(ct);
    }

    public async Task RemovePrimaryBookImageAsync(int bookId, CancellationToken ct = default)
    {
        await using var dbContext = await _factory.CreateDbContextAsync(ct);
        var primary = await dbContext.BookImages
            .FirstOrDefaultAsync(i => i.BookId == bookId && i.IsPrimary, ct);
        if (primary is not null)
        {
            dbContext.BookImages.Remove(primary);
            await dbContext.SaveChangesAsync(ct);
        }
    }

    public async Task<IReadOnlyList<BookImage>> GetBookImagesAsync(int bookId, CancellationToken ct = default)
    {
        await using var dbContext = await _factory.CreateDbContextAsync(ct);
        return await dbContext.BookImages
            .AsNoTracking()
            .Where(bi => bi.BookId == bookId)
            .Include(bi => bi.BookImageType)
            .OrderBy(bi => bi.DisplayOrder)
            .ToListAsync(ct);
    }

    public async Task SaveBookImageByTypeAsync(int bookId, int imageTypeId, byte[] data, CancellationToken ct = default)
    {
        await using var dbContext = await _factory.CreateDbContextAsync(ct);

        // IsPrimary = true only for type 0 (Cover) — preserves IsPrimary semantics for thumbnail-loading fallback
        bool isPrimary = imageTypeId == 0;

        var existing = await dbContext.BookImages
            .FirstOrDefaultAsync(i => i.BookId == bookId && i.BookImageTypeId == imageTypeId, ct);
        if (existing != null)
            dbContext.BookImages.Remove(existing);

        dbContext.BookImages.Add(new BookImage
        {
            BookId = bookId,
            ImageData = data,
            MimeType = ImageHelpers.DetectMimeType(data),
            IsPrimary = isPrimary,
            DisplayOrder = imageTypeId,
            BookImageTypeId = imageTypeId,
            Added = DateTime.UtcNow
        });
        await dbContext.SaveChangesAsync(ct);
    }

    public async Task RemoveBookImageByTypeAsync(int bookId, int imageTypeId, CancellationToken ct = default)
    {
        await using var dbContext = await _factory.CreateDbContextAsync(ct);
        await dbContext.BookImages
            .Where(i => i.BookId == bookId && i.BookImageTypeId == imageTypeId)
            .ExecuteDeleteAsync(ct);
    }

    public async Task<byte[]?> GetBookThumbnailBytesAsync(int bookId, CancellationToken ct = default)
    {
        await using var dbContext = await _factory.CreateDbContextAsync(ct);

        var thumbnail = await dbContext.BookImages
            .Where(i => i.BookId == bookId && i.BookImageTypeId == 1)
            .Select(i => i.ImageData)
            .FirstOrDefaultAsync(ct);

        if (thumbnail == null)
        {
            thumbnail = await dbContext.BookImages
                .Where(i => i.BookId == bookId && i.IsPrimary)
                .Select(i => i.ImageData)
                .FirstOrDefaultAsync(ct);
        }

        return thumbnail;
    }

    public async Task ReorderBookImageAsync(int bookId, int imageId, int newDisplayOrder, CancellationToken ct = default)
    {
        await using var dbContext = await _factory.CreateDbContextAsync(ct);
        await dbContext.BookImages
            .Where(i => i.BookId == bookId && i.BookImageId == imageId)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.DisplayOrder, newDisplayOrder), ct);
    }

    public async Task ReassignBookImageTypeAsync(int bookId, int imageId, int newTypeId, CancellationToken ct = default)
    {
        await using var dbContext = await _factory.CreateDbContextAsync(ct);
        await dbContext.BookImages
            .Where(i => i.BookId == bookId && i.BookImageId == imageId)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.BookImageTypeId, newTypeId), ct);
    }

    public async Task<byte[]?> GetBookImageBytesAsync(int bookId, int imageId, CancellationToken ct = default)
    {
        await using var dbContext = await _factory.CreateDbContextAsync(ct);
        return await dbContext.BookImages
            .Where(i => i.BookId == bookId && i.BookImageId == imageId)
            .Select(i => i.ImageData)
            .SingleOrDefaultAsync(ct);
    }

    public async Task RemoveBookImageByIdAsync(int bookId, int imageId, CancellationToken ct = default)
    {
        await using var dbContext = await _factory.CreateDbContextAsync(ct);
        await dbContext.BookImages
            .Where(i => i.BookId == bookId && i.BookImageId == imageId)
            .ExecuteDeleteAsync(ct);
    }
}
