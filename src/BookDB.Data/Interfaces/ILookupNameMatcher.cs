using System;
using System.Linq.Expressions;
using BookDB.Models.Interfaces;

namespace BookDB.Data.Interfaces;

/// <summary>
/// Provider-specific, case-insensitive and Unicode-aware name equality for lookup duplicate checks
/// (SQLite <c>NOCASE</c> collation vs Postgres <c>ILIKE</c>). Kept off the shared LINQ path because the
/// naive cross-provider form — <c>x.ToLower() == value.ToLower()</c> — is wrong on SQLite, whose
/// <c>lower()</c> is ASCII-only while the C# constant is culture-aware, so non-ASCII case duplicates
/// (e.g. <c>ÅSA</c>/<c>Åsa</c>) slip through (issue #42).
/// </summary>
public interface ILookupNameMatcher
{
    /// <summary>Predicate matching lookup rows whose <see cref="INamedLookup.Name"/> equals <paramref name="value"/>
    /// ignoring case.</summary>
    Expression<Func<T, bool>> NameEquals<T>(string value) where T : class, INamedLookup;
}
