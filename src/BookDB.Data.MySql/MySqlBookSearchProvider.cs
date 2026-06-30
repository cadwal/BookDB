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

namespace BookDB.Data.MySql;

/// <summary>
/// MySQL/MariaDB search: ranked full-text over the InnoDB <c>FULLTEXT</c> index (replacing SQLite FTS5 /
/// Postgres tsvector) and case-insensitive text/relation predicates via <c>LIKE</c>. Case-insensitivity is
/// native here — the schema's <c>utf8mb4_unicode_ci</c> collation makes <c>LIKE</c> ignore case, so no ILIKE
/// plumbing is needed (the Postgres analog's only structural difference). User input is wildcard-escaped so it
/// can never inject LIKE metacharacters, and full-text tokens are stripped to alphanumerics before being handed
/// to <c>AGAINST … IN BOOLEAN MODE</c> so no boolean operator from raw input survives.
/// </summary>
public sealed class MySqlBookSearchProvider : IBookSearchProvider
{
    private const string EscapeChar = LikeEscaping.EscapeChar;

    // InnoDB FULLTEXT silently ignores tokens shorter than innodb_ft_min_token_size (default 3), so a query with
    // any shorter token is routed to a LIKE fallback instead — short terms still match (parity with SQLite FTS5 /
    // Postgres tsquery, which both index short tokens). Assuming the default is safe: a token at/above this length
    // that still doesn't match is a genuine miss, not a silently-dropped short token.
    private const int MinFullTextTokenLength = 3;

    private static readonly MethodInfo LikeMethod = typeof(DbFunctionsExtensions).GetMethod(
        nameof(DbFunctionsExtensions.Like),
        new[] { typeof(DbFunctions), typeof(string), typeof(string), typeof(string) })!;

    private readonly IDbContextFactory<BookDbContext> _factory;

    private record FtsResult(int BookId);

    public MySqlBookSearchProvider(IDbContextFactory<BookDbContext> factory)
    {
        _factory = factory;
    }

    public async Task<IReadOnlyList<int>> SearchBookIdsAsync(string rawQuery, CancellationToken ct)
    {
        var tokens = Tokenize(rawQuery);
        if (tokens.Count == 0) return Array.Empty<int>();

        await using var dbContext = await _factory.CreateDbContextAsync(ct);

        // Any token below the FULLTEXT floor would make MATCH … AGAINST drop it and the AND-combined query return
        // nothing, so fall back to a LIKE scan over the same columns when a short token is present.
        if (tokens.Any(token => token.Length < MinFullTextTokenLength))
            return await SearchByLikeAsync(dbContext, tokens, ct);

        // Prefix-matching, AND-combined boolean query (e.g. "+foo* +bar*"); the trailing '*' is the boolean-mode
        // prefix wildcard. The MATCH column list is a literal (never user input) and must match
        // V001_CreateSchema.sql's IX_Book_SearchVector index exactly, or MySQL raises "Can't find FULLTEXT index
        // matching the column list". Only booleanQuery is parameterized; SqlQuery turns every interpolation into a
        // parameter.
        var booleanQuery = string.Join(" ", tokens.Select(token => "+" + token + "*"));
        var results = await dbContext.Database
            .SqlQuery<FtsResult>($"""
                SELECT `BookId` FROM `Book`
                WHERE MATCH(`Title`, `Subtitle`, `Keywords`, `Comments`, `BookInfo`, `ExternalId`)
                      AGAINST({booleanQuery} IN BOOLEAN MODE)
                ORDER BY MATCH(`Title`, `Subtitle`, `Keywords`, `Comments`, `BookInfo`, `ExternalId`)
                      AGAINST({booleanQuery} IN BOOLEAN MODE) DESC
                """)
            .ToListAsync(ct);
        return results.Select(r => r.BookId).ToList();
    }

    // Each whitespace token is reduced to its letters/digits so no boolean operator (+ - * " ( ) ~ < >) survives
    // — every token is then safe to hand to AGAINST or to wildcard-escape for LIKE.
    private static List<string> Tokenize(string rawQuery) =>
        rawQuery
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(SanitizeToken)
            .Where(token => token.Length > 0)
            .ToList();

    // LIKE fallback for queries the FULLTEXT index can't serve (short tokens). Each term must appear in at least
    // one of the same columns MATCH searches; terms are AND-combined. No FULLTEXT ranking here — an unranked id
    // set is acceptable for the rare short-token query.
    private static async Task<IReadOnlyList<int>> SearchByLikeAsync(
        BookDbContext dbContext, IReadOnlyList<string> tokens, CancellationToken ct)
    {
        IQueryable<Book> query = dbContext.Books;
        foreach (var token in tokens)
        {
            var pattern = ContainsPattern(token);
            query = query.Where(b =>
                EF.Functions.Like(b.Title, pattern, EscapeChar)
                || (b.Subtitle != null && EF.Functions.Like(b.Subtitle, pattern, EscapeChar))
                || (b.Keywords != null && EF.Functions.Like(b.Keywords, pattern, EscapeChar))
                || (b.Comments != null && EF.Functions.Like(b.Comments, pattern, EscapeChar))
                || (b.BookInfo != null && EF.Functions.Like(b.BookInfo, pattern, EscapeChar))
                || (b.ExternalId != null && EF.Functions.Like(b.ExternalId, pattern, EscapeChar)));
        }
        return await query.Select(b => b.BookId).ToListAsync(ct);
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

        Expression Like(string pattern) => Expression.Call(
            LikeMethod,
            Expression.Constant(EF.Functions, typeof(DbFunctions)),
            prop,
            Expression.Constant(pattern, typeof(string)),
            Expression.Constant(EscapeChar, typeof(string)));

        Expression body = op switch
        {
            SearchOperator.Contains    => Like(ContainsPattern(value)),
            SearchOperator.StartsWith  => Like(StartsWithPattern(value)),
            SearchOperator.EndsWith    => Like(EndsWithPattern(value)),
            SearchOperator.Equals      => Like(ExactPattern(value)),
            // Negative operators only match books that actually have a value — empty/null fields are excluded.
            SearchOperator.NotContains => Expression.AndAlso(HasTextValue(prop), Expression.Not(Like(ContainsPattern(value)))),
            SearchOperator.NotEquals   => Expression.AndAlso(HasTextValue(prop), Expression.Not(Like(ExactPattern(value)))),
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
                                                     && c.Person != null && EF.Functions.Like(c.Person.DisplayName, contains, EscapeChar)),
                SearchOperator.NotContains => b => b.Contributors.Any(c => c.ContributorRole != null && c.ContributorRole.Code == "Author")
                                                     && !b.Contributors.Any(c => c.ContributorRole != null && c.ContributorRole.Code == "Author"
                                                     && c.Person != null && EF.Functions.Like(c.Person.DisplayName, contains, EscapeChar)),
                SearchOperator.NotEquals   => b => b.Contributors.Any(c => c.ContributorRole != null && c.ContributorRole.Code == "Author")
                                                     && !b.Contributors.Any(c => c.ContributorRole != null && c.ContributorRole.Code == "Author"
                                                     && c.Person != null && EF.Functions.Like(c.Person.DisplayName, exact, EscapeChar)),
                SearchOperator.Equals      => b => b.Contributors.Any(c => c.ContributorRole != null && c.ContributorRole.Code == "Author"
                                                     && c.Person != null && EF.Functions.Like(c.Person.DisplayName, exact, EscapeChar)),
                SearchOperator.StartsWith  => b => b.Contributors.Any(c => c.ContributorRole != null && c.ContributorRole.Code == "Author"
                                                     && c.Person != null && EF.Functions.Like(c.Person.DisplayName, starts, EscapeChar)),
                SearchOperator.EndsWith    => b => b.Contributors.Any(c => c.ContributorRole != null && c.ContributorRole.Code == "Author"
                                                     && c.Person != null && EF.Functions.Like(c.Person.DisplayName, ends, EscapeChar)),
                _                          => null
            },

            SearchField.Publisher => op switch
            {
                SearchOperator.IsEmpty     => b => b.Publisher == null,
                SearchOperator.IsNotEmpty  => b => b.Publisher != null,
                SearchOperator.Contains    => b => b.Publisher != null && EF.Functions.Like(b.Publisher.Name, contains, EscapeChar),
                SearchOperator.NotContains => b => b.Publisher != null && !EF.Functions.Like(b.Publisher.Name, contains, EscapeChar),
                SearchOperator.NotEquals   => b => b.Publisher != null && !EF.Functions.Like(b.Publisher.Name, exact, EscapeChar),
                SearchOperator.Equals      => b => b.Publisher != null && EF.Functions.Like(b.Publisher.Name, exact, EscapeChar),
                SearchOperator.StartsWith  => b => b.Publisher != null && EF.Functions.Like(b.Publisher.Name, starts, EscapeChar),
                SearchOperator.EndsWith    => b => b.Publisher != null && EF.Functions.Like(b.Publisher.Name, ends, EscapeChar),
                _                          => null
            },

            SearchField.Series => op switch
            {
                SearchOperator.IsEmpty     => b => b.Series == null,
                SearchOperator.IsNotEmpty  => b => b.Series != null,
                SearchOperator.Contains    => b => b.Series != null && EF.Functions.Like(b.Series.Name, contains, EscapeChar),
                SearchOperator.NotContains => b => b.Series != null && !EF.Functions.Like(b.Series.Name, contains, EscapeChar),
                SearchOperator.NotEquals   => b => b.Series != null && !EF.Functions.Like(b.Series.Name, exact, EscapeChar),
                SearchOperator.Equals      => b => b.Series != null && EF.Functions.Like(b.Series.Name, exact, EscapeChar),
                SearchOperator.StartsWith  => b => b.Series != null && EF.Functions.Like(b.Series.Name, starts, EscapeChar),
                SearchOperator.EndsWith    => b => b.Series != null && EF.Functions.Like(b.Series.Name, ends, EscapeChar),
                _                          => null
            },

            SearchField.Category => op switch
            {
                SearchOperator.IsEmpty     => b => !b.Categories.Any(),
                SearchOperator.IsNotEmpty  => b => b.Categories.Any(),
                SearchOperator.Contains    => b => b.Categories.Any(bc => bc.Category != null && EF.Functions.Like(bc.Category.Name, contains, EscapeChar)),
                SearchOperator.NotContains => b => b.Categories.Any() && !b.Categories.Any(bc => bc.Category != null && EF.Functions.Like(bc.Category.Name, contains, EscapeChar)),
                SearchOperator.NotEquals   => b => b.Categories.Any() && !b.Categories.Any(bc => bc.Category != null && EF.Functions.Like(bc.Category.Name, exact, EscapeChar)),
                SearchOperator.Equals      => b => b.Categories.Any(bc => bc.Category != null && EF.Functions.Like(bc.Category.Name, exact, EscapeChar)),
                SearchOperator.StartsWith  => b => b.Categories.Any(bc => bc.Category != null && EF.Functions.Like(bc.Category.Name, starts, EscapeChar)),
                SearchOperator.EndsWith    => b => b.Categories.Any(bc => bc.Category != null && EF.Functions.Like(bc.Category.Name, ends, EscapeChar)),
                _                          => null
            },

            SearchField.Format => op switch
            {
                SearchOperator.IsEmpty     => b => b.Format == null,
                SearchOperator.IsNotEmpty  => b => b.Format != null,
                SearchOperator.Contains    => b => b.Format != null && EF.Functions.Like(b.Format.Name, contains, EscapeChar),
                SearchOperator.NotContains => b => b.Format != null && !EF.Functions.Like(b.Format.Name, contains, EscapeChar),
                SearchOperator.NotEquals   => b => b.Format != null && !EF.Functions.Like(b.Format.Name, exact, EscapeChar),
                SearchOperator.Equals      => b => b.Format != null && EF.Functions.Like(b.Format.Name, exact, EscapeChar),
                SearchOperator.StartsWith  => b => b.Format != null && EF.Functions.Like(b.Format.Name, starts, EscapeChar),
                SearchOperator.EndsWith    => b => b.Format != null && EF.Functions.Like(b.Format.Name, ends, EscapeChar),
                _                          => null
            },

            SearchField.Language => op switch
            {
                SearchOperator.IsEmpty     => b => b.Language == null,
                SearchOperator.IsNotEmpty  => b => b.Language != null,
                SearchOperator.Contains    => b => b.Language != null && EF.Functions.Like(b.Language.Name, contains, EscapeChar),
                SearchOperator.NotContains => b => b.Language != null && !EF.Functions.Like(b.Language.Name, contains, EscapeChar),
                SearchOperator.NotEquals   => b => b.Language != null && !EF.Functions.Like(b.Language.Name, exact, EscapeChar),
                SearchOperator.Equals      => b => b.Language != null && EF.Functions.Like(b.Language.Name, exact, EscapeChar),
                SearchOperator.StartsWith  => b => b.Language != null && EF.Functions.Like(b.Language.Name, starts, EscapeChar),
                SearchOperator.EndsWith    => b => b.Language != null && EF.Functions.Like(b.Language.Name, ends, EscapeChar),
                _                          => null
            },

            SearchField.Rating => op switch
            {
                SearchOperator.IsEmpty     => b => b.Rating == null,
                SearchOperator.IsNotEmpty  => b => b.Rating != null,
                SearchOperator.Contains    => b => b.Rating != null && EF.Functions.Like(b.Rating.Name, contains, EscapeChar),
                SearchOperator.NotContains => b => b.Rating != null && !EF.Functions.Like(b.Rating.Name, contains, EscapeChar),
                SearchOperator.NotEquals   => b => b.Rating != null && !EF.Functions.Like(b.Rating.Name, exact, EscapeChar),
                SearchOperator.Equals      => b => b.Rating != null && EF.Functions.Like(b.Rating.Name, exact, EscapeChar),
                SearchOperator.StartsWith  => b => b.Rating != null && EF.Functions.Like(b.Rating.Name, starts, EscapeChar),
                SearchOperator.EndsWith    => b => b.Rating != null && EF.Functions.Like(b.Rating.Name, ends, EscapeChar),
                _                          => null
            },

            SearchField.Status => op switch
            {
                SearchOperator.IsEmpty     => b => b.Status == null,
                SearchOperator.IsNotEmpty  => b => b.Status != null,
                SearchOperator.Contains    => b => b.Status != null && EF.Functions.Like(b.Status.Name, contains, EscapeChar),
                SearchOperator.NotContains => b => b.Status != null && !EF.Functions.Like(b.Status.Name, contains, EscapeChar),
                SearchOperator.NotEquals   => b => b.Status != null && !EF.Functions.Like(b.Status.Name, exact, EscapeChar),
                SearchOperator.Equals      => b => b.Status != null && EF.Functions.Like(b.Status.Name, exact, EscapeChar),
                SearchOperator.StartsWith  => b => b.Status != null && EF.Functions.Like(b.Status.Name, starts, EscapeChar),
                SearchOperator.EndsWith    => b => b.Status != null && EF.Functions.Like(b.Status.Name, ends, EscapeChar),
                _                          => null
            },

            SearchField.Location => op switch
            {
                SearchOperator.IsEmpty     => b => b.Location == null,
                SearchOperator.IsNotEmpty  => b => b.Location != null,
                SearchOperator.Contains    => b => b.Location != null && EF.Functions.Like(b.Location.Name, contains, EscapeChar),
                SearchOperator.NotContains => b => b.Location != null && !EF.Functions.Like(b.Location.Name, contains, EscapeChar),
                SearchOperator.NotEquals   => b => b.Location != null && !EF.Functions.Like(b.Location.Name, exact, EscapeChar),
                SearchOperator.Equals      => b => b.Location != null && EF.Functions.Like(b.Location.Name, exact, EscapeChar),
                SearchOperator.StartsWith  => b => b.Location != null && EF.Functions.Like(b.Location.Name, starts, EscapeChar),
                SearchOperator.EndsWith    => b => b.Location != null && EF.Functions.Like(b.Location.Name, ends, EscapeChar),
                _                          => null
            },

            SearchField.Owner => op switch
            {
                SearchOperator.IsEmpty     => b => b.Owner == null,
                SearchOperator.IsNotEmpty  => b => b.Owner != null,
                SearchOperator.Contains    => b => b.Owner != null && EF.Functions.Like(b.Owner.Name, contains, EscapeChar),
                SearchOperator.NotContains => b => b.Owner != null && !EF.Functions.Like(b.Owner.Name, contains, EscapeChar),
                SearchOperator.NotEquals   => b => b.Owner != null && !EF.Functions.Like(b.Owner.Name, exact, EscapeChar),
                SearchOperator.Equals      => b => b.Owner != null && EF.Functions.Like(b.Owner.Name, exact, EscapeChar),
                SearchOperator.StartsWith  => b => b.Owner != null && EF.Functions.Like(b.Owner.Name, starts, EscapeChar),
                SearchOperator.EndsWith    => b => b.Owner != null && EF.Functions.Like(b.Owner.Name, ends, EscapeChar),
                _                          => null
            },

            _ => null
        };
    }
}
