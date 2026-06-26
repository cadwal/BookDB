using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BookDB.Data.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace BookDB.Logic.Services;

public sealed class StatisticsService : IStatisticsService
{
    private readonly IDbContextFactory<BookDbContext> _factory;

    public StatisticsService(IDbContextFactory<BookDbContext> factory)
    {
        _factory = factory;
    }

    public async Task<IReadOnlyList<(int Year, int Count)>> GetBooksPerYearAsync(
        CancellationToken ct = default)
    {
        await using var dbContext = await _factory.CreateDbContextAsync(ct);
        // Materialise the grouping as an anonymous row, then map to the tuple in memory. Projecting the tuple
        // inside the query makes EF emit a SQL composite (ROW/record) that Npgsql cannot read back on Postgres.
        var rows = await dbContext.Books
            .GroupBy(b => b.Added.Year)
            .Select(g => new { Year = g.Key, Count = g.Count() })
            .OrderBy(x => x.Year)
            .ToListAsync(ct);
        return rows.Select(x => (x.Year, x.Count)).ToList();
    }

    public async Task<IReadOnlyList<BreakdownRow>> GetBreakdownByFormatAsync(
        CancellationToken ct = default)
    {
        await using var dbContext = await _factory.CreateDbContextAsync(ct);

        var total = await dbContext.Books.CountAsync(ct);
        if (total == 0)
            return [];

        // Join-based approach — EF Core 10 SQLite cannot translate GroupBy with navigation .Name inline
        var counts = await dbContext.Books
            .GroupBy(b => b.FormatId)
            .Select(g => new { FormatId = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var formats = await dbContext.Formats
            .Select(f => new { f.FormatId, f.Name })
            .ToListAsync(ct);

        var formatMap = formats.ToDictionary(f => f.FormatId, f => f.Name);

        return counts
            .OrderByDescending(x => x.Count)
            .Select(x => new BreakdownRow(
                x.FormatId.HasValue && formatMap.TryGetValue(x.FormatId.Value, out var name) ? name : "Unknown",
                x.Count,
                Math.Round((double)x.Count / total * 100, 1)))
            .ToList();
    }

    public async Task<IReadOnlyList<BreakdownRow>> GetBreakdownByCollectionAsync(
        CancellationToken ct = default)
    {
        await using var dbContext = await _factory.CreateDbContextAsync(ct);

        var total = await dbContext.Books.CountAsync(ct);
        if (total == 0)
            return [];

        var counts = await dbContext.Books
            .GroupBy(b => b.CollectionId)
            .Select(g => new { CollectionId = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var collections = await dbContext.Collections
            .Select(c => new { c.CollectionId, c.Name })
            .ToListAsync(ct);

        var collectionMap = collections.ToDictionary(c => c.CollectionId, c => c.Name);

        return counts
            .OrderByDescending(x => x.Count)
            .Select(x => new BreakdownRow(
                x.CollectionId.HasValue && collectionMap.TryGetValue(x.CollectionId.Value, out var name) ? name : "Unknown",
                x.Count,
                Math.Round((double)x.Count / total * 100, 1)))
            .ToList();
    }

    public async Task<IReadOnlyList<BreakdownRow>> GetBreakdownByLanguageAsync(
        CancellationToken ct = default)
    {
        await using var dbContext = await _factory.CreateDbContextAsync(ct);

        var total = await dbContext.Books.CountAsync(ct);
        if (total == 0)
            return [];

        var counts = await dbContext.Books
            .GroupBy(b => b.LanguageId)
            .Select(g => new { LanguageId = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var languages = await dbContext.Languages
            .Select(l => new { l.LanguageId, l.Name })
            .ToListAsync(ct);

        var languageMap = languages.ToDictionary(l => l.LanguageId, l => l.Name);

        return counts
            .OrderByDescending(x => x.Count)
            .Select(x => new BreakdownRow(
                x.LanguageId.HasValue && languageMap.TryGetValue(x.LanguageId.Value, out var name) ? name : "Unknown",
                x.Count,
                Math.Round((double)x.Count / total * 100, 1)))
            .ToList();
    }

    public async Task<IReadOnlyList<BreakdownRow>> GetBreakdownByPublishedYearAsync(
        CancellationToken ct = default)
    {
        await using var dbContext = await _factory.CreateDbContextAsync(ct);

        var total = await dbContext.Books.CountAsync(ct);
        if (total == 0)
            return [];

        // PubDate is stored as string — filter out nulls and group by raw string value
        var counts = await dbContext.Books
            .Where(b => b.PubDate != null && b.PubDate != "")
            .GroupBy(b => b.PubDate)
            .Select(g => new { PubDate = g.Key, Count = g.Count() })
            .OrderBy(x => x.PubDate)
            .ToListAsync(ct);

        return counts
            .Select(x => new BreakdownRow(
                x.PubDate ?? "Unknown",
                x.Count,
                Math.Round((double)x.Count / total * 100, 1)))
            .ToList();
    }

    public async Task<int> GetTotalBookCountAsync(CancellationToken ct = default)
    {
        await using var dbContext = await _factory.CreateDbContextAsync(ct);
        return await dbContext.Books.CountAsync(ct);
    }
}
