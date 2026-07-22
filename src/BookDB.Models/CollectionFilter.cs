using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using BookDB.Models.Entities;

namespace BookDB.Models;

/// <summary>
/// Collection sidebar filtering. Real <see cref="Collection.CollectionId"/>s are positive;
/// <see cref="Uncategorized"/> is a sentinel that, when present in the selected set, includes books with no
/// collection (<see cref="Book.CollectionId"/> null). Every query that filters the library by collection —
/// list, facet counts, export, print — goes through <see cref="Predicate"/> so the null-inclusion rule can
/// never drift between them.
/// </summary>
public static class CollectionFilter
{
    /// <summary>Sentinel id for the "Uncategorized" (no-collection) filter entry. Never a real collection.</summary>
    public const int Uncategorized = -1;

    /// <summary>
    /// Selects books belonging to any of the given collections. A positive id matches that collection; the
    /// <see cref="Uncategorized"/> sentinel additionally matches books with no collection. Books with no
    /// collection appear ONLY when the sentinel is selected — otherwise they are filtered out.
    /// </summary>
    public static Expression<Func<Book, bool>> Predicate(IReadOnlySet<int> collectionIds)
    {
        var includeUncategorized = collectionIds.Contains(Uncategorized);
        var realIds = collectionIds.Where(id => id > 0).ToHashSet();
        return b => (includeUncategorized && b.CollectionId == null)
                 || (b.CollectionId != null && realIds.Contains(b.CollectionId.Value));
    }
}
