using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using BookDB.Data.DbContexts;
using BookDB.Data.Interfaces;
using BookDB.Models.Entities;
using BookDB.Models.Enums;
using Microsoft.EntityFrameworkCore;

namespace BookDB.Data.Sqlite;

/// <summary>
/// SQLite search: FTS5 ranked full-text and the case-insensitive <c>NOCASE</c> relation predicates.
/// Text-field predicates are case-sensitive (binary), matching the column collation; relation
/// predicates fold case via <c>NOCASE</c> — both preserved from the original BookSearchService.
/// </summary>
public sealed class SqliteBookSearchProvider : IBookSearchProvider
{
    private readonly IDbContextFactory<BookDbContext> _factory;

    private record FtsResult(int BookId);

    public SqliteBookSearchProvider(IDbContextFactory<BookDbContext> factory)
    {
        _factory = factory;
    }

    public async Task<IReadOnlyList<int>> SearchBookIdsAsync(string rawQuery, CancellationToken ct)
    {
        var ftsQuery = BuildFtsQuery(rawQuery);
        await using var dbContext = await _factory.CreateDbContextAsync(ct);
        var results = await dbContext.Database
            .SqlQuery<FtsResult>($"SELECT rowid AS BookId FROM fts_books WHERE fts_books MATCH {ftsQuery} ORDER BY rank")
            .ToListAsync(ct);
        return results.Select(r => r.BookId).ToList();
    }

    private static string BuildFtsQuery(string rawQuery)
    {
        var trimmed = rawQuery.Trim();
        if (string.IsNullOrEmpty(trimmed)) return "\"\"";

        // Split into tokens and create prefix queries for each
        var tokens = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 1)
        {
            var escaped = tokens[0].Replace("\"", "\"\"");
            return $"\"{escaped}\"*";
        }

        // Multi-word: each token becomes a prefix match joined with AND
        var parts = tokens.Select(t => $"\"{t.Replace("\"", "\"\"")}\"*");
        return string.Join(" AND ", parts);
    }

    public Expression<Func<Book, bool>>? BuildTextPredicate(string field, SearchOperator op, string value)
    {
        var param = Expression.Parameter(typeof(Book), "b");
        var prop = Expression.Property(param, field);

        Expression body;
        switch (op)
        {
            case SearchOperator.Contains:
                var containsMethod = typeof(string).GetMethod("Contains", new[] { typeof(string) })!;
                body = Expression.AndAlso(
                    Expression.NotEqual(prop, Expression.Constant(null, typeof(string))),
                    Expression.Call(prop, containsMethod, Expression.Constant(value)));
                break;

            case SearchOperator.NotContains:
                var notContainsMethod = typeof(string).GetMethod("Contains", new[] { typeof(string) })!;
                // Negative operators only match books that have a value — empty/null fields are excluded.
                body = Expression.AndAlso(
                    HasTextValue(prop),
                    Expression.Not(Expression.Call(prop, notContainsMethod, Expression.Constant(value))));
                break;

            case SearchOperator.Equals:
                body = Expression.Equal(prop, Expression.Constant(value, typeof(string)));
                break;

            case SearchOperator.NotEquals:
                // Only books that have a value and whose value differs from the search value.
                body = Expression.AndAlso(
                    HasTextValue(prop),
                    Expression.NotEqual(prop, Expression.Constant(value, typeof(string))));
                break;

            case SearchOperator.StartsWith:
                var swMethod = typeof(string).GetMethod("StartsWith", new[] { typeof(string) })!;
                body = Expression.AndAlso(
                    Expression.NotEqual(prop, Expression.Constant(null, typeof(string))),
                    Expression.Call(prop, swMethod, Expression.Constant(value)));
                break;

            case SearchOperator.EndsWith:
                var ewMethod = typeof(string).GetMethod("EndsWith", new[] { typeof(string) })!;
                body = Expression.AndAlso(
                    Expression.NotEqual(prop, Expression.Constant(null, typeof(string))),
                    Expression.Call(prop, ewMethod, Expression.Constant(value)));
                break;

            case SearchOperator.IsEmpty:
                // EF Core can translate: b.Field == null || b.Field == ""
                body = Expression.OrElse(
                    Expression.Equal(prop, Expression.Constant(null, typeof(string))),
                    Expression.Equal(prop, Expression.Constant(string.Empty, typeof(string))));
                break;

            case SearchOperator.IsNotEmpty:
                // EF Core can translate: b.Field != null && b.Field != ""
                body = Expression.AndAlso(
                    Expression.NotEqual(prop, Expression.Constant(null, typeof(string))),
                    Expression.NotEqual(prop, Expression.Constant(string.Empty, typeof(string))));
                break;

            default:
                body = Expression.Constant(true);
                break;
        }

        return Expression.Lambda<Func<Book, bool>>(body, param);
    }

    // True when a text property is neither null nor empty — used to exclude empty fields from
    // negative operators (NotContains / NotEquals).
    private static Expression HasTextValue(Expression prop) =>
        Expression.AndAlso(
            Expression.NotEqual(prop, Expression.Constant(null, typeof(string))),
            Expression.NotEqual(prop, Expression.Constant(string.Empty, typeof(string))));

    public Expression<Func<Book, bool>>? BuildRelationPredicate(SearchField field, SearchOperator op, string value)
    {
        var val = value;

        return field switch
        {
            // Author — navigate through Contributors join table
            SearchField.Author => op switch
            {
                SearchOperator.IsEmpty    => b => !b.Contributors.Any(c => c.ContributorRole != null && c.ContributorRole.Code == "Author"),
                SearchOperator.IsNotEmpty => b => b.Contributors.Any(c => c.ContributorRole != null && c.ContributorRole.Code == "Author"),
                SearchOperator.Contains   => b => b.Contributors.Any(c => c.ContributorRole != null && c.ContributorRole.Code == "Author"
                                                    && c.Person != null && EF.Functions.Like(c.Person.DisplayName, $"%{val}%")),
                SearchOperator.NotContains => b => b.Contributors.Any(c => c.ContributorRole != null && c.ContributorRole.Code == "Author")
                                                    && !b.Contributors.Any(c => c.ContributorRole != null && c.ContributorRole.Code == "Author"
                                                    && c.Person != null && EF.Functions.Like(c.Person.DisplayName, $"%{val}%")),
                SearchOperator.NotEquals  => b => b.Contributors.Any(c => c.ContributorRole != null && c.ContributorRole.Code == "Author")
                                                    && !b.Contributors.Any(c => c.ContributorRole != null && c.ContributorRole.Code == "Author"
                                                    && c.Person != null && EF.Functions.Collate(c.Person.DisplayName, "NOCASE") == EF.Functions.Collate(val, "NOCASE")),
                SearchOperator.Equals     => b => b.Contributors.Any(c => c.ContributorRole != null && c.ContributorRole.Code == "Author"
                                                    && c.Person != null && EF.Functions.Collate(c.Person.DisplayName, "NOCASE") == EF.Functions.Collate(val, "NOCASE")),
                SearchOperator.StartsWith => b => b.Contributors.Any(c => c.ContributorRole != null && c.ContributorRole.Code == "Author"
                                                    && c.Person != null && EF.Functions.Like(c.Person.DisplayName, $"{val}%")),
                SearchOperator.EndsWith   => b => b.Contributors.Any(c => c.ContributorRole != null && c.ContributorRole.Code == "Author"
                                                    && c.Person != null && EF.Functions.Like(c.Person.DisplayName, $"%{val}")),
                _                         => null
            },

            // Publisher — navigate through Publisher navigation property
            SearchField.Publisher => op switch
            {
                SearchOperator.IsEmpty    => b => b.Publisher == null,
                SearchOperator.IsNotEmpty => b => b.Publisher != null,
                SearchOperator.Contains   => b => b.Publisher != null && EF.Functions.Like(b.Publisher.Name, $"%{val}%"),
                SearchOperator.NotContains => b => b.Publisher != null && !EF.Functions.Like(b.Publisher.Name, $"%{val}%"),
                SearchOperator.NotEquals  => b => b.Publisher != null && EF.Functions.Collate(b.Publisher.Name, "NOCASE") != EF.Functions.Collate(val, "NOCASE"),
                SearchOperator.Equals     => b => b.Publisher != null && EF.Functions.Collate(b.Publisher.Name, "NOCASE") == EF.Functions.Collate(val, "NOCASE"),
                SearchOperator.StartsWith => b => b.Publisher != null && EF.Functions.Like(b.Publisher.Name, $"{val}%"),
                SearchOperator.EndsWith   => b => b.Publisher != null && EF.Functions.Like(b.Publisher.Name, $"%{val}"),
                _                         => null
            },

            // Series — navigate through Series navigation property
            SearchField.Series => op switch
            {
                SearchOperator.IsEmpty    => b => b.Series == null,
                SearchOperator.IsNotEmpty => b => b.Series != null,
                SearchOperator.Contains   => b => b.Series != null && EF.Functions.Like(b.Series.Name, $"%{val}%"),
                SearchOperator.NotContains => b => b.Series != null && !EF.Functions.Like(b.Series.Name, $"%{val}%"),
                SearchOperator.NotEquals  => b => b.Series != null && EF.Functions.Collate(b.Series.Name, "NOCASE") != EF.Functions.Collate(val, "NOCASE"),
                SearchOperator.Equals     => b => b.Series != null && EF.Functions.Collate(b.Series.Name, "NOCASE") == EF.Functions.Collate(val, "NOCASE"),
                SearchOperator.StartsWith => b => b.Series != null && EF.Functions.Like(b.Series.Name, $"{val}%"),
                SearchOperator.EndsWith   => b => b.Series != null && EF.Functions.Like(b.Series.Name, $"%{val}"),
                _                         => null
            },

            // Category — navigate through BookCategory join table
            SearchField.Category => op switch
            {
                SearchOperator.IsEmpty    => b => !b.Categories.Any(),
                SearchOperator.IsNotEmpty => b => b.Categories.Any(),
                SearchOperator.Contains   => b => b.Categories.Any(bc => bc.Category != null && EF.Functions.Like(bc.Category.Name, $"%{val}%")),
                SearchOperator.NotContains => b => b.Categories.Any() && !b.Categories.Any(bc => bc.Category != null && EF.Functions.Like(bc.Category.Name, $"%{val}%")),
                SearchOperator.NotEquals  => b => b.Categories.Any() && !b.Categories.Any(bc => bc.Category != null && EF.Functions.Collate(bc.Category.Name, "NOCASE") == EF.Functions.Collate(val, "NOCASE")),
                SearchOperator.Equals     => b => b.Categories.Any(bc => bc.Category != null && EF.Functions.Collate(bc.Category.Name, "NOCASE") == EF.Functions.Collate(val, "NOCASE")),
                SearchOperator.StartsWith => b => b.Categories.Any(bc => bc.Category != null && EF.Functions.Like(bc.Category.Name, $"{val}%")),
                SearchOperator.EndsWith   => b => b.Categories.Any(bc => bc.Category != null && EF.Functions.Like(bc.Category.Name, $"%{val}")),
                _                         => null
            },

            // Format — navigate through Format navigation property
            SearchField.Format => op switch
            {
                SearchOperator.IsEmpty    => b => b.Format == null,
                SearchOperator.IsNotEmpty => b => b.Format != null,
                SearchOperator.Contains   => b => b.Format != null && EF.Functions.Like(b.Format.Name, $"%{val}%"),
                SearchOperator.NotContains => b => b.Format != null && !EF.Functions.Like(b.Format.Name, $"%{val}%"),
                SearchOperator.NotEquals  => b => b.Format != null && EF.Functions.Collate(b.Format.Name, "NOCASE") != EF.Functions.Collate(val, "NOCASE"),
                SearchOperator.Equals     => b => b.Format != null && EF.Functions.Collate(b.Format.Name, "NOCASE") == EF.Functions.Collate(val, "NOCASE"),
                SearchOperator.StartsWith => b => b.Format != null && EF.Functions.Like(b.Format.Name, $"{val}%"),
                SearchOperator.EndsWith   => b => b.Format != null && EF.Functions.Like(b.Format.Name, $"%{val}"),
                _                         => null
            },

            // Language — navigate through Language navigation property
            SearchField.Language => op switch
            {
                SearchOperator.IsEmpty    => b => b.Language == null,
                SearchOperator.IsNotEmpty => b => b.Language != null,
                SearchOperator.Contains   => b => b.Language != null && EF.Functions.Like(b.Language.Name, $"%{val}%"),
                SearchOperator.NotContains => b => b.Language != null && !EF.Functions.Like(b.Language.Name, $"%{val}%"),
                SearchOperator.NotEquals  => b => b.Language != null && EF.Functions.Collate(b.Language.Name, "NOCASE") != EF.Functions.Collate(val, "NOCASE"),
                SearchOperator.Equals     => b => b.Language != null && EF.Functions.Collate(b.Language.Name, "NOCASE") == EF.Functions.Collate(val, "NOCASE"),
                SearchOperator.StartsWith => b => b.Language != null && EF.Functions.Like(b.Language.Name, $"{val}%"),
                SearchOperator.EndsWith   => b => b.Language != null && EF.Functions.Like(b.Language.Name, $"%{val}"),
                _                         => null
            },

            // Rating — navigate through Rating navigation property
            SearchField.Rating => op switch
            {
                SearchOperator.IsEmpty    => b => b.Rating == null,
                SearchOperator.IsNotEmpty => b => b.Rating != null,
                SearchOperator.Contains   => b => b.Rating != null && EF.Functions.Like(b.Rating.Name, $"%{val}%"),
                SearchOperator.NotContains => b => b.Rating != null && !EF.Functions.Like(b.Rating.Name, $"%{val}%"),
                SearchOperator.NotEquals  => b => b.Rating != null && EF.Functions.Collate(b.Rating.Name, "NOCASE") != EF.Functions.Collate(val, "NOCASE"),
                SearchOperator.Equals     => b => b.Rating != null && EF.Functions.Collate(b.Rating.Name, "NOCASE") == EF.Functions.Collate(val, "NOCASE"),
                SearchOperator.StartsWith => b => b.Rating != null && EF.Functions.Like(b.Rating.Name, $"{val}%"),
                SearchOperator.EndsWith   => b => b.Rating != null && EF.Functions.Like(b.Rating.Name, $"%{val}"),
                _                         => null
            },

            // Status — navigate through Status navigation property
            SearchField.Status => op switch
            {
                SearchOperator.IsEmpty    => b => b.Status == null,
                SearchOperator.IsNotEmpty => b => b.Status != null,
                SearchOperator.Contains   => b => b.Status != null && EF.Functions.Like(b.Status.Name, $"%{val}%"),
                SearchOperator.NotContains => b => b.Status != null && !EF.Functions.Like(b.Status.Name, $"%{val}%"),
                SearchOperator.NotEquals  => b => b.Status != null && EF.Functions.Collate(b.Status.Name, "NOCASE") != EF.Functions.Collate(val, "NOCASE"),
                SearchOperator.Equals     => b => b.Status != null && EF.Functions.Collate(b.Status.Name, "NOCASE") == EF.Functions.Collate(val, "NOCASE"),
                SearchOperator.StartsWith => b => b.Status != null && EF.Functions.Like(b.Status.Name, $"{val}%"),
                SearchOperator.EndsWith   => b => b.Status != null && EF.Functions.Like(b.Status.Name, $"%{val}"),
                _                         => null
            },

            // Location — navigate through Location navigation property
            SearchField.Location => op switch
            {
                SearchOperator.IsEmpty    => b => b.Location == null,
                SearchOperator.IsNotEmpty => b => b.Location != null,
                SearchOperator.Contains   => b => b.Location != null && EF.Functions.Like(b.Location.Name, $"%{val}%"),
                SearchOperator.NotContains => b => b.Location != null && !EF.Functions.Like(b.Location.Name, $"%{val}%"),
                SearchOperator.NotEquals  => b => b.Location != null && EF.Functions.Collate(b.Location.Name, "NOCASE") != EF.Functions.Collate(val, "NOCASE"),
                SearchOperator.Equals     => b => b.Location != null && EF.Functions.Collate(b.Location.Name, "NOCASE") == EF.Functions.Collate(val, "NOCASE"),
                SearchOperator.StartsWith => b => b.Location != null && EF.Functions.Like(b.Location.Name, $"{val}%"),
                SearchOperator.EndsWith   => b => b.Location != null && EF.Functions.Like(b.Location.Name, $"%{val}"),
                _                         => null
            },

            // Owner — navigate through Owner navigation property
            SearchField.Owner => op switch
            {
                SearchOperator.IsEmpty    => b => b.Owner == null,
                SearchOperator.IsNotEmpty => b => b.Owner != null,
                SearchOperator.Contains   => b => b.Owner != null && EF.Functions.Like(b.Owner.Name, $"%{val}%"),
                SearchOperator.NotContains => b => b.Owner != null && !EF.Functions.Like(b.Owner.Name, $"%{val}%"),
                SearchOperator.NotEquals  => b => b.Owner != null && EF.Functions.Collate(b.Owner.Name, "NOCASE") != EF.Functions.Collate(val, "NOCASE"),
                SearchOperator.Equals     => b => b.Owner != null && EF.Functions.Collate(b.Owner.Name, "NOCASE") == EF.Functions.Collate(val, "NOCASE"),
                SearchOperator.StartsWith => b => b.Owner != null && EF.Functions.Like(b.Owner.Name, $"{val}%"),
                SearchOperator.EndsWith   => b => b.Owner != null && EF.Functions.Like(b.Owner.Name, $"%{val}"),
                _                         => null
            },

            _ => null
        };
    }
}
