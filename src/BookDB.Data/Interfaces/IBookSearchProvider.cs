using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using BookDB.Models.Entities;
using BookDB.Models.Enums;

namespace BookDB.Data.Interfaces;

/// <summary>
/// Provider-specific search: full-text ranking plus the case-insensitive text/relation predicates
/// (SQLite <c>NOCASE</c> collation vs Postgres <c>ILIKE</c>). Facet-count queries stay in
/// BookSearchService — they are provider-agnostic EF LINQ.
/// </summary>
public interface IBookSearchProvider
{
    /// <summary>Full-text path — returns matching BookIds ranked by relevance.</summary>
    Task<IReadOnlyList<int>> SearchBookIdsAsync(string rawQuery, CancellationToken ct);

    Expression<Func<Book, bool>>? BuildTextPredicate(string field, SearchOperator op, string value);

    Expression<Func<Book, bool>>? BuildRelationPredicate(SearchField field, SearchOperator op, string value);
}
