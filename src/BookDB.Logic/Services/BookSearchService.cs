using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BookDB.Data.DbContexts;
using BookDB.Models.Entities;
using BookDB.Models.Enums;
using Microsoft.EntityFrameworkCore;
namespace BookDB.Logic.Services;
public sealed class BookSearchService : IBookSearchService
{
    private readonly IDbContextFactory<BookDbContext> _factory;
    private record FtsResult(int BookId);
    public BookSearchService(IDbContextFactory<BookDbContext> factory)
    {
        _factory = factory;
    }    // ---------------------------------------------------------------------------
    // FTS5 Search
    // ---------------------------------------------------------------------------

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

    public async Task<IReadOnlyList<int>> SearchBookIdsAsync(
        string rawQuery,
        CancellationToken ct = default)
    {
        var ftsQuery = BuildFtsQuery(rawQuery);
        await using var dbContext = await _factory.CreateDbContextAsync(ct);
        var results = await dbContext.Database
            .SqlQuery<FtsResult>($"SELECT rowid AS BookId FROM fts_books WHERE fts_books MATCH {ftsQuery} ORDER BY rank")
            .ToListAsync(ct);
        return results.Select(r => r.BookId).ToList();
    }

    // ---------------------------------------------------------------------------
    // Advanced Condition Search
    // ---------------------------------------------------------------------------

    public async Task<IReadOnlyList<long>> SearchByConditionsAsync(
        IReadOnlyList<SearchCondition> conditions,
        string combinator,
        CancellationToken ct = default)
    {
        await using var dbContext = await _factory.CreateDbContextAsync(ct);

        IQueryable<Book> query = dbContext.Books;

        if (combinator.Equals("AND", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var condition in conditions)
            {
                var predicate = BuildPredicate(condition);
                if (predicate != null)
                    query = query.Where(predicate);
            }
        }
        else // OR
        {
            Expression<Func<Book, bool>>? combined = null;
            foreach (var condition in conditions)
            {
                var predicate = BuildPredicate(condition);
                if (predicate == null) continue;
                combined = combined == null
                    ? predicate
                    : OrElse(combined, predicate);
            }

            if (combined != null)
                query = query.Where(combined);
        }

        return await query.Select(b => (long)b.BookId).ToListAsync(ct);
    }

    // Maps SearchField enum values to the exact Book entity property names used by
    // Expression.Property for text fields that can be searched via reflection.
    private static readonly Dictionary<SearchField, string> _fieldToProperty =
        new()
        {
            [SearchField.Title]    = "Title",
            [SearchField.Keywords] = "Keywords",
            [SearchField.Comments] = "Comments",
            [SearchField.Isbn]     = "Isbn",
        };

    private static Expression<Func<Book, bool>>? BuildPredicate(SearchCondition condition)
    {
        var op = condition.Operator;
        var val = condition.Value;

        // Text-property fields (Title, Keywords, Comments, Isbn) — use reflection-based predicate
        if (_fieldToProperty.TryGetValue(condition.Field, out var propertyName))
        {
            return BuildTextPredicate(propertyName, op, val);
        }

        return condition.Field switch
        {
            // Year — compare PubDate string
            SearchField.Year => BuildTextPredicate("PubDate", op, val),

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

    private static Expression<Func<Book, bool>> BuildTextPredicate(
        string field,
        SearchOperator op,
        string value)
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

    // True when a text property is neither null nor empty — used to exclude empty
    // fields from negative operators (NotContains / NotEquals).
    private static Expression HasTextValue(Expression prop) =>
        Expression.AndAlso(
            Expression.NotEqual(prop, Expression.Constant(null, typeof(string))),
            Expression.NotEqual(prop, Expression.Constant(string.Empty, typeof(string))));

    private static Expression<Func<Book, bool>> OrElse(
        Expression<Func<Book, bool>> left,
        Expression<Func<Book, bool>> right)
    {
        var param = left.Parameters[0];
        var rightBody = new ReplaceParameterVisitor(right.Parameters[0], param).Visit(right.Body);
        return Expression.Lambda<Func<Book, bool>>(
            Expression.OrElse(left.Body, rightBody), param);
    }

    // ---------------------------------------------------------------------------
    // Facet Counts
    // ---------------------------------------------------------------------------

    public async Task<IReadOnlyList<FacetCount>> GetFacetCountsAsync(
        IReadOnlySet<int> collectionIds,
        string facetName,
        CancellationToken ct = default)
    {
        await using var dbContext = await _factory.CreateDbContextAsync(ct);

        var baseQuery = dbContext.Books.Where(b =>
            b.CollectionId == null || collectionIds.Contains(b.CollectionId.Value));

        return facetName switch
        {
            "Author"    => await GetAuthorFacetCountsAsync(baseQuery, dbContext, ct),
            "Series"    => await GetSeriesFacetCountsAsync(baseQuery, dbContext, ct),
            "Publisher" => await GetPublisherFacetCountsAsync(baseQuery, dbContext, ct),
            "Category"  => await GetCategoryFacetCountsAsync(baseQuery, dbContext, ct),
            "Format"    => await GetFormatFacetCountsAsync(baseQuery, dbContext, ct),
            "Language"  => await GetLanguageFacetCountsAsync(baseQuery, dbContext, ct),
            "Rating"    => await GetRatingFacetCountsAsync(baseQuery, dbContext, ct),
            "Status"    => await GetStatusFacetCountsAsync(baseQuery, dbContext, ct),
            "Location"  => await GetLocationFacetCountsAsync(baseQuery, dbContext, ct),
            "Owner"     => await GetOwnerFacetCountsAsync(baseQuery, dbContext, ct),
            _           => new List<FacetCount>()
        };
    }

    private async Task<IReadOnlyList<FacetCount>> GetAuthorFacetCountsAsync(
        IQueryable<Book> baseQuery, BookDbContext dbContext, CancellationToken ct)
        => await (
            from b in baseQuery
            join bc in dbContext.BookContributors on b.BookId equals bc.BookId
            join cr in dbContext.ContributorRoles on bc.ContributorRoleId equals cr.ContributorRoleId
            join p in dbContext.People on bc.PersonId equals p.PersonId
            where cr.Code == "Author"
            group b by new { bc.PersonId, p.SortName, p.DisplayName } into g
            orderby g.Key.SortName
            select new FacetCount(
                g.Key.PersonId,
                string.IsNullOrWhiteSpace(g.Key.SortName) ? g.Key.DisplayName : g.Key.SortName,
                g.Count(),
                g.Key.DisplayName))
            .ToListAsync(ct);

    private async Task<IReadOnlyList<FacetCount>> GetSeriesFacetCountsAsync(
        IQueryable<Book> baseQuery, BookDbContext dbContext, CancellationToken ct)
        => await (
            from b in baseQuery.Where(b => b.SeriesId != null)
            join s in dbContext.Series on b.SeriesId equals s.SeriesId
            group b by new { b.SeriesId, s.Name } into g
            orderby g.Key.Name
            select new FacetCount(g.Key.SeriesId!.Value, g.Key.Name, g.Count()))
            .ToListAsync(ct);

    private async Task<IReadOnlyList<FacetCount>> GetPublisherFacetCountsAsync(
        IQueryable<Book> baseQuery, BookDbContext dbContext, CancellationToken ct)
        => await (
            from b in baseQuery.Where(b => b.PublisherId != null)
            join p in dbContext.Publishers on b.PublisherId equals p.PublisherId
            group b by new { b.PublisherId, p.Name } into g
            orderby g.Key.Name
            select new FacetCount(g.Key.PublisherId!.Value, g.Key.Name, g.Count()))
            .ToListAsync(ct);

    private async Task<IReadOnlyList<FacetCount>> GetCategoryFacetCountsAsync(
        IQueryable<Book> baseQuery, BookDbContext dbContext, CancellationToken ct)
        => await (
            from b in baseQuery
            join bc in dbContext.BookCategories on b.BookId equals bc.BookId
            join c in dbContext.Categories on bc.CategoryId equals c.CategoryId
            group b by new { bc.CategoryId, c.Name } into g
            orderby g.Key.Name
            select new FacetCount(g.Key.CategoryId, g.Key.Name, g.Count()))
            .ToListAsync(ct);

    private async Task<IReadOnlyList<FacetCount>> GetFormatFacetCountsAsync(
        IQueryable<Book> baseQuery, BookDbContext dbContext, CancellationToken ct)
        => await (
            from b in baseQuery.Where(b => b.FormatId != null)
            join f in dbContext.Formats on b.FormatId equals f.FormatId
            group b by new { b.FormatId, f.Name } into g
            orderby g.Key.Name
            select new FacetCount(g.Key.FormatId!.Value, g.Key.Name, g.Count()))
            .ToListAsync(ct);

    private async Task<IReadOnlyList<FacetCount>> GetLanguageFacetCountsAsync(
        IQueryable<Book> baseQuery, BookDbContext dbContext, CancellationToken ct)
        => await (
            from b in baseQuery.Where(b => b.LanguageId != null)
            join l in dbContext.Languages on b.LanguageId equals l.LanguageId
            group b by new { b.LanguageId, l.Name } into g
            orderby g.Key.Name
            select new FacetCount(g.Key.LanguageId!.Value, g.Key.Name, g.Count()))
            .ToListAsync(ct);

    private async Task<IReadOnlyList<FacetCount>> GetRatingFacetCountsAsync(
        IQueryable<Book> baseQuery, BookDbContext dbContext, CancellationToken ct)
        => await (
            from b in baseQuery.Where(b => b.RatingId != null)
            join r in dbContext.Ratings on b.RatingId equals r.RatingId
            group b by new { b.RatingId, r.Name } into g
            orderby g.Key.Name
            select new FacetCount(g.Key.RatingId!.Value, g.Key.Name, g.Count()))
            .ToListAsync(ct);

    private async Task<IReadOnlyList<FacetCount>> GetStatusFacetCountsAsync(
        IQueryable<Book> baseQuery, BookDbContext dbContext, CancellationToken ct)
        => await (
            from b in baseQuery.Where(b => b.StatusId != null)
            join s in dbContext.Statuses on b.StatusId equals s.StatusId
            group b by new { b.StatusId, s.Name } into g
            orderby g.Key.Name
            select new FacetCount(g.Key.StatusId!.Value, g.Key.Name, g.Count()))
            .ToListAsync(ct);

    private async Task<IReadOnlyList<FacetCount>> GetLocationFacetCountsAsync(
        IQueryable<Book> baseQuery, BookDbContext dbContext, CancellationToken ct)
        => await (
            from b in baseQuery.Where(b => b.LocationId != null)
            join l in dbContext.Locations on b.LocationId equals l.LocationId
            group b by new { b.LocationId, l.Name } into g
            orderby g.Key.Name
            select new FacetCount(g.Key.LocationId!.Value, g.Key.Name, g.Count()))
            .ToListAsync(ct);

    private async Task<IReadOnlyList<FacetCount>> GetOwnerFacetCountsAsync(
        IQueryable<Book> baseQuery, BookDbContext dbContext, CancellationToken ct)
        => await (
            from b in baseQuery.Where(b => b.OwnerId != null)
            join o in dbContext.Owners on b.OwnerId equals o.OwnerId
            group b by new { b.OwnerId, o.Name } into g
            orderby g.Key.Name
            select new FacetCount(g.Key.OwnerId!.Value, g.Key.Name, g.Count()))
            .ToListAsync(ct);

    private sealed class ReplaceParameterVisitor : ExpressionVisitor
    {
        private readonly ParameterExpression _oldParam;
        private readonly ParameterExpression _newParam;

        public ReplaceParameterVisitor(ParameterExpression oldParam, ParameterExpression newParam)
        {
            _oldParam = oldParam;
            _newParam = newParam;
        }

        protected override Expression VisitParameter(ParameterExpression node)
            => node == _oldParam ? _newParam : base.VisitParameter(node);
    }
}
