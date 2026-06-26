using System;
using System.Linq.Expressions;
using BookDB.Data.Interfaces;
using BookDB.Models.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace BookDB.Data.Sqlite;

/// <summary>
/// SQLite lookup name matching via symmetric <c>NOCASE</c> collation — the same fold the lookup tables
/// use, so duplicate detection stays Unicode-correct (see <see cref="ILookupNameMatcher"/>).
/// </summary>
public sealed class SqliteLookupNameMatcher : ILookupNameMatcher
{
    public Expression<Func<T, bool>> NameEquals<T>(string value) where T : class, INamedLookup =>
        e => EF.Functions.Collate(e.Name, "NOCASE") == EF.Functions.Collate(value, "NOCASE");
}
