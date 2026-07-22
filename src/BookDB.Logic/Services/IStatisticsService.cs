using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BookDB.Logic.Services;

/// <summary>
/// One category slice of a breakdown chart. A null <paramref name="Label"/> marks the uncategorised
/// bucket (books with no format/collection/language assigned); the display layer localises it.
/// </summary>
public record BreakdownRow(string? Label, int Count, double Percentage);

/// <summary>One month on the cumulative library-growth line: the running total of books at month's end.</summary>
public record LibraryGrowthPoint(int Year, int Month, int CumulativeCount);

public interface IStatisticsService
{
    Task<IReadOnlyList<(int Year, int Count)>> GetBooksPerYearAsync(CancellationToken ct = default);
    Task<IReadOnlyList<LibraryGrowthPoint>> GetLibraryGrowthAsync(CancellationToken ct = default);
    Task<IReadOnlyList<BreakdownRow>> GetBreakdownByFormatAsync(CancellationToken ct = default);
    Task<IReadOnlyList<BreakdownRow>> GetBreakdownByCollectionAsync(CancellationToken ct = default);
    Task<IReadOnlyList<BreakdownRow>> GetBreakdownByLanguageAsync(CancellationToken ct = default);
    Task<IReadOnlyList<BreakdownRow>> GetBreakdownByPublishedYearAsync(CancellationToken ct = default);
    Task<IReadOnlyList<BreakdownRow>> GetTopAuthorsAsync(int limit, CancellationToken ct = default);
    Task<int> GetTotalBookCountAsync(CancellationToken ct = default);
}
