using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BookDB.Models.Enums;
namespace BookDB.Logic.Services;

public record FacetCount(int Id, string Name, int Count, string? AlternateName = null);
public record SearchCondition(SearchField Field, SearchOperator Operator, string Value);

public interface IBookSearchService
{
    Task<IReadOnlyList<int>> SearchBookIdsAsync(string rawQuery, CancellationToken ct = default);
    Task<IReadOnlyList<long>> SearchByConditionsAsync(
        IReadOnlyList<SearchCondition> conditions,
        string combinator,
        CancellationToken ct = default);
    Task<IReadOnlyList<FacetCount>> GetFacetCountsAsync(
        IReadOnlySet<int> collectionIds,
        string facetName,
        CancellationToken ct = default);
}