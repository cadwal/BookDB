using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BookDB.Data.DbContexts;
using BookDB.Data.Interfaces;
using BookDB.Models.Entities;
using BookDB.Models.Enums;
using Microsoft.EntityFrameworkCore;

namespace BookDB.Data.PostgreSQL;

/// <summary>
/// PostgreSQL search: ranked full-text over the generated <c>SearchVector</c> tsvector column (replacing
/// SQLite FTS5) and case-insensitive text/relation predicates via <c>ILIKE</c> (replacing SQLite's
/// <c>NOCASE</c> collation). User input is wildcard-escaped so it can never inject LIKE metacharacters,
/// and full-text tokens are stripped to lexeme-safe characters before being handed to <c>to_tsquery</c>.
/// </summary>
public sealed class PostgresBookSearchProvider : IBookSearchProvider
{
    private const string EscapeChar = LikeEscaping.EscapeChar;

    private static readonly MethodInfo ILikeMethod = typeof(NpgsqlDbFunctionsExtensions).GetMethod(
        nameof(NpgsqlDbFunctionsExtensions.ILike),
        new[] { typeof(DbFunctions), typeof(string), typeof(string), typeof(string) })!;

    private readonly IDbContextFactory<BookDbContext> _factory;

    private record FtsResult(int BookId);

    public PostgresBookSearchProvider(IDbContextFactory<BookDbContext> factory)
    {
        _factory = factory;
    }

    public async Task<IReadOnlyList<int>> SearchBookIdsAsync(string rawQuery, CancellationToken ct)
    {
        var tsQuery = BuildTsQuery(rawQuery);
        if (tsQuery is null) return Array.Empty<int>();

        await using var dbContext = await _factory.CreateDbContextAsync(ct);
        var results = await dbContext.Database
            .SqlQuery<FtsResult>($"""
                SELECT "BookId" FROM "Book"
                WHERE "SearchVector" @@ to_tsquery('simple', {tsQuery})
                ORDER BY ts_rank("SearchVector", to_tsquery('simple', {tsQuery})) DESC
                """)
            .ToListAsync(ct);
        return results.Select(r => r.BookId).ToList();
    }

    // Builds a prefix-matching, AND-combined tsquery (e.g. "foo:* & bar:*"). Each whitespace token is
    // reduced to its letters/digits so no tsquery operator (&, |, !, parentheses, quotes, :) survives —
    // the value is then safe to pass as a parameter to to_tsquery. Returns null when nothing is searchable.
    private static string? BuildTsQuery(string rawQuery)
    {
        var lexemes = rawQuery
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(SanitizeToken)
            .Where(token => token.Length > 0)
            .Select(token => token + ":*");

        var joined = string.Join(" & ", lexemes);
        return joined.Length == 0 ? null : joined;
    }

    private static string SanitizeToken(string token)
    {
        var sb = new StringBuilder(token.Length);
        foreach (var ch in token)
        {
            if (char.IsLetterOrDigit(ch))
                sb.Append(ch);
        }
        return sb.ToString();
    }

    public Expression<Func<Book, bool>>? BuildTextPredicate(string field, SearchOperator op, string value)
    {
        var param = Expression.Parameter(typeof(Book), "b");
        var prop = Expression.Property(param, field);

        Expression ILike(string pattern) => Expression.Call(
            ILikeMethod,
            Expression.Constant(EF.Functions, typeof(DbFunctions)),
            prop,
            Expression.Constant(pattern, typeof(string)),
            Expression.Constant(EscapeChar, typeof(string)));

        Expression body = op switch
        {
            SearchOperator.Contains    => ILike(ContainsPattern(value)),
            SearchOperator.StartsWith  => ILike(StartsWithPattern(value)),
            SearchOperator.EndsWith    => ILike(EndsWithPattern(value)),
            SearchOperator.Equals      => ILike(ExactPattern(value)),
            // Negative operators only match books that actually have a value — empty/null fields are excluded.
            SearchOperator.NotContains => Expression.AndAlso(HasTextValue(prop), Expression.Not(ILike(ContainsPattern(value)))),
            SearchOperator.NotEquals   => Expression.AndAlso(HasTextValue(prop), Expression.Not(ILike(ExactPattern(value)))),
            SearchOperator.IsEmpty     => Expression.OrElse(IsNull(prop), IsEmptyString(prop)),
            SearchOperator.IsNotEmpty  => HasTextValue(prop),
            _                          => Expression.Constant(true),
        };

        return Expression.Lambda<Func<Book, bool>>(body, param);
    }

    private static Expression IsNull(Expression prop) =>
        Expression.Equal(prop, Expression.Constant(null, typeof(string)));

    private static Expression IsEmptyString(Expression prop) =>
        Expression.Equal(prop, Expression.Constant(string.Empty, typeof(string)));

    // True when a text property is neither null nor empty.
    private static Expression HasTextValue(Expression prop) =>
        Expression.AndAlso(
            Expression.NotEqual(prop, Expression.Constant(null, typeof(string))),
            Expression.NotEqual(prop, Expression.Constant(string.Empty, typeof(string))));

    private static string ContainsPattern(string value) => $"%{LikeEscaping.Escape(value)}%";
    private static string StartsWithPattern(string value) => $"{LikeEscaping.Escape(value)}%";
    private static string EndsWithPattern(string value) => $"%{LikeEscaping.Escape(value)}";
    private static string ExactPattern(string value) => LikeEscaping.Escape(value);

    public Expression<Func<Book, bool>>? BuildRelationPredicate(SearchField field, SearchOperator op, string value)
    {
        var contains = ContainsPattern(value);
        var starts = StartsWithPattern(value);
        var ends = EndsWithPattern(value);
        var exact = ExactPattern(value);

        return field switch
        {
            // Author — navigate through the Contributors join table.
            SearchField.Author => op switch
            {
                SearchOperator.IsEmpty     => b => !b.Contributors.Any(c => c.ContributorRole != null && c.ContributorRole.Code == "Author"),
                SearchOperator.IsNotEmpty  => b => b.Contributors.Any(c => c.ContributorRole != null && c.ContributorRole.Code == "Author"),
                SearchOperator.Contains    => b => b.Contributors.Any(c => c.ContributorRole != null && c.ContributorRole.Code == "Author"
                                                     && c.Person != null && EF.Functions.ILike(c.Person.DisplayName, contains, EscapeChar)),
                SearchOperator.NotContains => b => b.Contributors.Any(c => c.ContributorRole != null && c.ContributorRole.Code == "Author")
                                                     && !b.Contributors.Any(c => c.ContributorRole != null && c.ContributorRole.Code == "Author"
                                                     && c.Person != null && EF.Functions.ILike(c.Person.DisplayName, contains, EscapeChar)),
                SearchOperator.NotEquals   => b => b.Contributors.Any(c => c.ContributorRole != null && c.ContributorRole.Code == "Author")
                                                     && !b.Contributors.Any(c => c.ContributorRole != null && c.ContributorRole.Code == "Author"
                                                     && c.Person != null && EF.Functions.ILike(c.Person.DisplayName, exact, EscapeChar)),
                SearchOperator.Equals      => b => b.Contributors.Any(c => c.ContributorRole != null && c.ContributorRole.Code == "Author"
                                                     && c.Person != null && EF.Functions.ILike(c.Person.DisplayName, exact, EscapeChar)),
                SearchOperator.StartsWith  => b => b.Contributors.Any(c => c.ContributorRole != null && c.ContributorRole.Code == "Author"
                                                     && c.Person != null && EF.Functions.ILike(c.Person.DisplayName, starts, EscapeChar)),
                SearchOperator.EndsWith    => b => b.Contributors.Any(c => c.ContributorRole != null && c.ContributorRole.Code == "Author"
                                                     && c.Person != null && EF.Functions.ILike(c.Person.DisplayName, ends, EscapeChar)),
                _                          => null
            },

            SearchField.Publisher => op switch
            {
                SearchOperator.IsEmpty     => b => b.Publisher == null,
                SearchOperator.IsNotEmpty  => b => b.Publisher != null,
                SearchOperator.Contains    => b => b.Publisher != null && EF.Functions.ILike(b.Publisher.Name, contains, EscapeChar),
                SearchOperator.NotContains => b => b.Publisher != null && !EF.Functions.ILike(b.Publisher.Name, contains, EscapeChar),
                SearchOperator.NotEquals   => b => b.Publisher != null && !EF.Functions.ILike(b.Publisher.Name, exact, EscapeChar),
                SearchOperator.Equals      => b => b.Publisher != null && EF.Functions.ILike(b.Publisher.Name, exact, EscapeChar),
                SearchOperator.StartsWith  => b => b.Publisher != null && EF.Functions.ILike(b.Publisher.Name, starts, EscapeChar),
                SearchOperator.EndsWith    => b => b.Publisher != null && EF.Functions.ILike(b.Publisher.Name, ends, EscapeChar),
                _                          => null
            },

            SearchField.Series => op switch
            {
                SearchOperator.IsEmpty     => b => b.Series == null,
                SearchOperator.IsNotEmpty  => b => b.Series != null,
                SearchOperator.Contains    => b => b.Series != null && EF.Functions.ILike(b.Series.Name, contains, EscapeChar),
                SearchOperator.NotContains => b => b.Series != null && !EF.Functions.ILike(b.Series.Name, contains, EscapeChar),
                SearchOperator.NotEquals   => b => b.Series != null && !EF.Functions.ILike(b.Series.Name, exact, EscapeChar),
                SearchOperator.Equals      => b => b.Series != null && EF.Functions.ILike(b.Series.Name, exact, EscapeChar),
                SearchOperator.StartsWith  => b => b.Series != null && EF.Functions.ILike(b.Series.Name, starts, EscapeChar),
                SearchOperator.EndsWith    => b => b.Series != null && EF.Functions.ILike(b.Series.Name, ends, EscapeChar),
                _                          => null
            },

            SearchField.Category => op switch
            {
                SearchOperator.IsEmpty     => b => !b.Categories.Any(),
                SearchOperator.IsNotEmpty  => b => b.Categories.Any(),
                SearchOperator.Contains    => b => b.Categories.Any(bc => bc.Category != null && EF.Functions.ILike(bc.Category.Name, contains, EscapeChar)),
                SearchOperator.NotContains => b => b.Categories.Any() && !b.Categories.Any(bc => bc.Category != null && EF.Functions.ILike(bc.Category.Name, contains, EscapeChar)),
                SearchOperator.NotEquals   => b => b.Categories.Any() && !b.Categories.Any(bc => bc.Category != null && EF.Functions.ILike(bc.Category.Name, exact, EscapeChar)),
                SearchOperator.Equals      => b => b.Categories.Any(bc => bc.Category != null && EF.Functions.ILike(bc.Category.Name, exact, EscapeChar)),
                SearchOperator.StartsWith  => b => b.Categories.Any(bc => bc.Category != null && EF.Functions.ILike(bc.Category.Name, starts, EscapeChar)),
                SearchOperator.EndsWith    => b => b.Categories.Any(bc => bc.Category != null && EF.Functions.ILike(bc.Category.Name, ends, EscapeChar)),
                _                          => null
            },

            SearchField.Format => op switch
            {
                SearchOperator.IsEmpty     => b => b.Format == null,
                SearchOperator.IsNotEmpty  => b => b.Format != null,
                SearchOperator.Contains    => b => b.Format != null && EF.Functions.ILike(b.Format.Name, contains, EscapeChar),
                SearchOperator.NotContains => b => b.Format != null && !EF.Functions.ILike(b.Format.Name, contains, EscapeChar),
                SearchOperator.NotEquals   => b => b.Format != null && !EF.Functions.ILike(b.Format.Name, exact, EscapeChar),
                SearchOperator.Equals      => b => b.Format != null && EF.Functions.ILike(b.Format.Name, exact, EscapeChar),
                SearchOperator.StartsWith  => b => b.Format != null && EF.Functions.ILike(b.Format.Name, starts, EscapeChar),
                SearchOperator.EndsWith    => b => b.Format != null && EF.Functions.ILike(b.Format.Name, ends, EscapeChar),
                _                          => null
            },

            SearchField.Language => op switch
            {
                SearchOperator.IsEmpty     => b => b.Language == null,
                SearchOperator.IsNotEmpty  => b => b.Language != null,
                SearchOperator.Contains    => b => b.Language != null && EF.Functions.ILike(b.Language.Name, contains, EscapeChar),
                SearchOperator.NotContains => b => b.Language != null && !EF.Functions.ILike(b.Language.Name, contains, EscapeChar),
                SearchOperator.NotEquals   => b => b.Language != null && !EF.Functions.ILike(b.Language.Name, exact, EscapeChar),
                SearchOperator.Equals      => b => b.Language != null && EF.Functions.ILike(b.Language.Name, exact, EscapeChar),
                SearchOperator.StartsWith  => b => b.Language != null && EF.Functions.ILike(b.Language.Name, starts, EscapeChar),
                SearchOperator.EndsWith    => b => b.Language != null && EF.Functions.ILike(b.Language.Name, ends, EscapeChar),
                _                          => null
            },

            SearchField.Rating => op switch
            {
                SearchOperator.IsEmpty     => b => b.Rating == null,
                SearchOperator.IsNotEmpty  => b => b.Rating != null,
                SearchOperator.Contains    => b => b.Rating != null && EF.Functions.ILike(b.Rating.Name, contains, EscapeChar),
                SearchOperator.NotContains => b => b.Rating != null && !EF.Functions.ILike(b.Rating.Name, contains, EscapeChar),
                SearchOperator.NotEquals   => b => b.Rating != null && !EF.Functions.ILike(b.Rating.Name, exact, EscapeChar),
                SearchOperator.Equals      => b => b.Rating != null && EF.Functions.ILike(b.Rating.Name, exact, EscapeChar),
                SearchOperator.StartsWith  => b => b.Rating != null && EF.Functions.ILike(b.Rating.Name, starts, EscapeChar),
                SearchOperator.EndsWith    => b => b.Rating != null && EF.Functions.ILike(b.Rating.Name, ends, EscapeChar),
                _                          => null
            },

            SearchField.Status => op switch
            {
                SearchOperator.IsEmpty     => b => b.Status == null,
                SearchOperator.IsNotEmpty  => b => b.Status != null,
                SearchOperator.Contains    => b => b.Status != null && EF.Functions.ILike(b.Status.Name, contains, EscapeChar),
                SearchOperator.NotContains => b => b.Status != null && !EF.Functions.ILike(b.Status.Name, contains, EscapeChar),
                SearchOperator.NotEquals   => b => b.Status != null && !EF.Functions.ILike(b.Status.Name, exact, EscapeChar),
                SearchOperator.Equals      => b => b.Status != null && EF.Functions.ILike(b.Status.Name, exact, EscapeChar),
                SearchOperator.StartsWith  => b => b.Status != null && EF.Functions.ILike(b.Status.Name, starts, EscapeChar),
                SearchOperator.EndsWith    => b => b.Status != null && EF.Functions.ILike(b.Status.Name, ends, EscapeChar),
                _                          => null
            },

            SearchField.Location => op switch
            {
                SearchOperator.IsEmpty     => b => b.Location == null,
                SearchOperator.IsNotEmpty  => b => b.Location != null,
                SearchOperator.Contains    => b => b.Location != null && EF.Functions.ILike(b.Location.Name, contains, EscapeChar),
                SearchOperator.NotContains => b => b.Location != null && !EF.Functions.ILike(b.Location.Name, contains, EscapeChar),
                SearchOperator.NotEquals   => b => b.Location != null && !EF.Functions.ILike(b.Location.Name, exact, EscapeChar),
                SearchOperator.Equals      => b => b.Location != null && EF.Functions.ILike(b.Location.Name, exact, EscapeChar),
                SearchOperator.StartsWith  => b => b.Location != null && EF.Functions.ILike(b.Location.Name, starts, EscapeChar),
                SearchOperator.EndsWith    => b => b.Location != null && EF.Functions.ILike(b.Location.Name, ends, EscapeChar),
                _                          => null
            },

            SearchField.Owner => op switch
            {
                SearchOperator.IsEmpty     => b => b.Owner == null,
                SearchOperator.IsNotEmpty  => b => b.Owner != null,
                SearchOperator.Contains    => b => b.Owner != null && EF.Functions.ILike(b.Owner.Name, contains, EscapeChar),
                SearchOperator.NotContains => b => b.Owner != null && !EF.Functions.ILike(b.Owner.Name, contains, EscapeChar),
                SearchOperator.NotEquals   => b => b.Owner != null && !EF.Functions.ILike(b.Owner.Name, exact, EscapeChar),
                SearchOperator.Equals      => b => b.Owner != null && EF.Functions.ILike(b.Owner.Name, exact, EscapeChar),
                SearchOperator.StartsWith  => b => b.Owner != null && EF.Functions.ILike(b.Owner.Name, starts, EscapeChar),
                SearchOperator.EndsWith    => b => b.Owner != null && EF.Functions.ILike(b.Owner.Name, ends, EscapeChar),
                _                          => null
            },

            _ => null
        };
    }
}
