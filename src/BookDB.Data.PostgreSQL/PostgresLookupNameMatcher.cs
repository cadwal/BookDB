using System;
using System.Linq.Expressions;
using BookDB.Data.Interfaces;
using BookDB.Models.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace BookDB.Data.PostgreSQL;

/// <summary>
/// PostgreSQL lookup name matching via <c>ILIKE</c> against the wildcard-escaped value, mirroring the
/// search provider's exact-match path (see <see cref="ILookupNameMatcher"/>). Postgres <c>lower()</c> and
/// <c>ILIKE</c> are Unicode-aware, so this folds case correctly for non-ASCII names.
/// </summary>
public sealed class PostgresLookupNameMatcher : ILookupNameMatcher
{
    private const string EscapeChar = "\\";

    public Expression<Func<T, bool>> NameEquals<T>(string value) where T : class, INamedLookup
    {
        var pattern = Escape(value);
        return e => EF.Functions.ILike(e.Name, pattern, EscapeChar);
    }

    private static string Escape(string value) =>
        value.Replace(EscapeChar, EscapeChar + EscapeChar).Replace("%", EscapeChar + "%").Replace("_", EscapeChar + "_");
}
