using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using BookDB.Data.DbContexts;
using BookDB.Data.Interfaces;
using BookDB.Models;
using BookDB.Models.Entities;
using BookDB.Models.Enums;
using Microsoft.EntityFrameworkCore;

namespace BookDB.Logic.Services;

public sealed class BookSearchService : IBookSearchService
{
    private readonly IDbContextFactory<BookDbContext> _factory;
    private readonly IBookSearchProvider _provider;

    public BookSearchService(IDbContextFactory<BookDbContext> factory, IBookSearchProvider provider)
    {
        _factory = factory;
        _provider = provider;
    }

    // ---------------------------------------------------------------------------
    // Full-text search
    // ---------------------------------------------------------------------------

    public Task<IReadOnlyList<int>> SearchBookIdsAsync(string rawQuery, CancellationToken ct = default)
        => _provider.SearchBookIdsAsync(rawQuery, ct);

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

    // Maps text-property SearchField values to the exact Book entity property names. Relation fields
    // (Author, Publisher, …) are not here — they route to the provider's relation predicate builder.
    private static readonly Dictionary<SearchField, string> _fieldToProperty =
        new()
        {
            [SearchField.Title]    = "Title",
            [SearchField.Keywords] = "Keywords",
            [SearchField.Comments] = "Comments",
            [SearchField.Isbn]     = "Isbn",
        };

    private Expression<Func<Book, bool>>? BuildPredicate(SearchCondition condition)
    {
        var op = condition.Operator;
        var val = condition.Value;

        if (_fieldToProperty.TryGetValue(condition.Field, out var propertyName))
            return _provider.BuildTextPredicate(propertyName, op, val);

        // Year compares the PubDate text column.
        if (condition.Field == SearchField.Year)
            return _provider.BuildTextPredicate("PubDate", op, val);

        return _provider.BuildRelationPredicate(condition.Field, op, val);
    }

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

        // An empty selection means no collection filter (show all) — consistent with GetBooksAsync; a
        // non-empty selection scopes to it, including the Uncategorized sentinel.
        var baseQuery = collectionIds is { Count: > 0 }
            ? dbContext.Books.Where(CollectionFilter.Predicate(collectionIds))
            : dbContext.Books;

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
