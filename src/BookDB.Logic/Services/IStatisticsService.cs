using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BookDB.Logic.Services;

public record BreakdownRow(string Label, int Count, double Percentage);

public interface IStatisticsService
{
    Task<IReadOnlyList<(int Year, int Count)>> GetBooksPerYearAsync(CancellationToken ct = default);
    Task<IReadOnlyList<BreakdownRow>> GetBreakdownByFormatAsync(CancellationToken ct = default);
    Task<IReadOnlyList<BreakdownRow>> GetBreakdownByCollectionAsync(CancellationToken ct = default);
    Task<IReadOnlyList<BreakdownRow>> GetBreakdownByLanguageAsync(CancellationToken ct = default);
    Task<IReadOnlyList<BreakdownRow>> GetBreakdownByPublishedYearAsync(CancellationToken ct = default);
    Task<int> GetTotalBookCountAsync(CancellationToken ct = default);
}
