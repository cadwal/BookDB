using System;
using System.Linq.Expressions;
using BookDB.Data.Interfaces;
using BookDB.Models.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace BookDB.Data.MySql;

/// <summary>
/// MySQL/MariaDB lookup name matching via <c>LIKE</c> against the wildcard-escaped value (see
/// <see cref="ILookupNameMatcher"/>). The schema's <c>utf8mb4_unicode_ci</c> collation makes <c>LIKE</c>
/// case-insensitive and folds non-ASCII case (e.g. <c>ÅSA</c>/<c>Åsa</c>), so no <c>ILIKE</c> / <c>lower()</c>
/// plumbing is needed — the same native-collation story as the search provider's exact-match path.
/// </summary>
public sealed class MySqlLookupNameMatcher : ILookupNameMatcher
{
    public Expression<Func<T, bool>> NameEquals<T>(string value) where T : class, INamedLookup
    {
        var pattern = LikeEscaping.Escape(value);
        return e => EF.Functions.Like(e.Name, pattern, LikeEscaping.EscapeChar);
    }
}
