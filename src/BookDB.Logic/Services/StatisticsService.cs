using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
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

    public async Task<IReadOnlyList<LibraryGrowthPoint>> GetLibraryGrowthAsync(
        CancellationToken ct = default)
    {
        await using var dbContext = await _factory.CreateDbContextAsync(ct);
        // Group added dates by year+month as an anonymous row (a tuple projection breaks Npgsql composite reads,
        // see GetBooksPerYearAsync), then accumulate the running total in memory to get the cumulative line.
        var monthly = await dbContext.Books
            .GroupBy(b => new { b.Added.Year, b.Added.Month })
            .Select(g => new { g.Key.Year, g.Key.Month, Count = g.Count() })
            .OrderBy(x => x.Year).ThenBy(x => x.Month)
            .ToListAsync(ct);

        var running = 0;
        return monthly
            .Select(x => new LibraryGrowthPoint(x.Year, x.Month, running += x.Count))
            .ToList();
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
                x.FormatId.HasValue && formatMap.TryGetValue(x.FormatId.Value, out var name) ? name : null,
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
                x.CollectionId.HasValue && collectionMap.TryGetValue(x.CollectionId.Value, out var name) ? name : null,
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
                x.LanguageId.HasValue && languageMap.TryGetValue(x.LanguageId.Value, out var name) ? name : null,
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

        // PubDate is a free-form string ("2003", "2003-05-01", "May 2003"); pull the raw values and reduce
        // each to its 4-digit year in memory, since SQL can't parse the varied formats. Books with no
        // recognisable year drop out, so percentages are over the whole library (matching the other breakdowns).
        var raw = await dbContext.Books
            .Where(b => b.PubDate != null && b.PubDate != "")
            .Select(b => b.PubDate!)
            .ToListAsync(ct);

        return raw
            .Select(ExtractYear)
            .Where(year => year != null)
            .GroupBy(year => year!.Value)
            .Select(g => new { Year = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .Select(x => new BreakdownRow(
                x.Year.ToString(CultureInfo.InvariantCulture),
                x.Count,
                Math.Round((double)x.Count / total * 100, 1)))
            .ToList();
    }

    private static int? ExtractYear(string pubDate)
    {
        var match = Regex.Match(pubDate, @"\d{4}");
        return match.Success && int.TryParse(match.Value, out var year) ? year : null;
    }

    public async Task<IReadOnlyList<BreakdownRow>> GetTopAuthorsAsync(
        int limit, CancellationToken ct = default)
    {
        await using var dbContext = await _factory.CreateDbContextAsync(ct);

        var total = await dbContext.Books.CountAsync(ct);
        if (total == 0)
            return [];

        // Two-query shape mirroring the breakdowns: aggregate contributor counts by PersonId (Author role only),
        // then map display names from People — EF Core SQLite cannot translate a GroupBy projecting the nav name.
        var counts = await dbContext.BookContributors
            .Where(bc => bc.ContributorRole!.Code == "Author")
            .GroupBy(bc => bc.PersonId)
            .Select(g => new { PersonId = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .Take(limit)
            .ToListAsync(ct);

        if (counts.Count == 0)
            return [];

        var personIds = counts.Select(x => x.PersonId).ToList();
        var names = await dbContext.People
            .Where(p => personIds.Contains(p.PersonId))
            .Select(p => new { p.PersonId, p.DisplayName })
            .ToListAsync(ct);

        var nameMap = names.ToDictionary(p => p.PersonId, p => p.DisplayName);

        return counts
            .Select(x => new BreakdownRow(
                nameMap.TryGetValue(x.PersonId, out var name) ? name : null,
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
