using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BookDB.Data;
using BookDB.Data.DbContexts;
using BookDB.Data.Interfaces;
using BookDB.Models.Entities;
using BookDB.Models.Enums;
using BookDB.Models.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BookDB.Data.PostgreSQL.Tests;

/// <summary>
/// Verifies the PostgreSQL search provider against a live container: tsvector full-text, the
/// case-insensitive ILIKE text/relation predicate matrix, and wildcard escaping. Every predicate is
/// also exercised end-to-end so a translation failure (an expression Npgsql can't render to SQL) is caught.
/// </summary>
public sealed class PostgresBookSearchProviderTests : IClassFixture<PostgresTestDbFixture>
{
    private readonly PostgresTestDbFixture _fixture;

    public PostgresBookSearchProviderTests(PostgresTestDbFixture fixture) => _fixture = fixture;

    private async Task<(ServiceProvider sp, IDbContextFactory<BookDbContext> factory, IBookSearchProvider provider)>
        BuildAsync(CancellationToken ct)
    {
        var runner = new PostgresDbUpRunner(_fixture.ConnectionString, NullLogger<DatabaseStartupService>.Instance);
        await runner.RunAsync(new Progress<(int applied, int total)>(), ct);

        var services = new ServiceCollection();
        services.AddSingleton<IDataChangeTracker, DataChangeTracker>();
        services.AddPostgresProvider(_fixture.ConnectionString);
        var sp = services.BuildServiceProvider();
        return (sp, sp.GetRequiredService<IDbContextFactory<BookDbContext>>(), sp.GetRequiredService<IBookSearchProvider>());
    }

    private static async Task<int> SeedBookAsync(IDbContextFactory<BookDbContext> factory, Book book, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        db.Books.Add(book);
        await db.SaveChangesAsync(ct);
        return book.BookId;
    }

    private static async Task<List<int>> RunTextAsync(
        IDbContextFactory<BookDbContext> factory, IBookSearchProvider provider,
        string field, SearchOperator op, string value, CancellationToken ct)
    {
        var predicate = provider.BuildTextPredicate(field, op, value);
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Books.Where(predicate!).Select(b => b.BookId).ToListAsync(ct);
    }

    private static async Task<List<int>> RunRelationAsync(
        IDbContextFactory<BookDbContext> factory, IBookSearchProvider provider,
        SearchField field, SearchOperator op, string value, CancellationToken ct)
    {
        var predicate = provider.BuildRelationPredicate(field, op, value);
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Books.Where(predicate!).Select(b => b.BookId).ToListAsync(ct);
    }

    // ---- Full-text search ----

    [Fact]
    public async Task Fts_SingleToken_PrefixMatches()
    {
        Assert.SkipUnless(_fixture.IsAvailable, _fixture.SkipReason);
        var ct = TestContext.Current.CancellationToken;
        var (sp, factory, provider) = await BuildAsync(ct);
        await using var scope = sp;

        var marker = Guid.NewGuid().ToString("N");
        var id1 = await SeedBookAsync(factory, new Book { Title = $"{marker}aaa" }, ct);
        var id2 = await SeedBookAsync(factory, new Book { Title = $"{marker}bbb" }, ct);

        // The marker is a prefix of both lexemes — to_tsquery appends ':*'.
        var ids = await provider.SearchBookIdsAsync(marker, ct);

        Assert.Contains(id1, ids);
        Assert.Contains(id2, ids);
    }

    [Fact]
    public async Task Fts_MultiWord_RequiresAllTokens()
    {
        Assert.SkipUnless(_fixture.IsAvailable, _fixture.SkipReason);
        var ct = TestContext.Current.CancellationToken;
        var (sp, factory, provider) = await BuildAsync(ct);
        await using var scope = sp;

        var m = Guid.NewGuid().ToString("N");
        var both = await SeedBookAsync(factory, new Book { Title = $"{m}red {m}blue" }, ct);
        var onlyOne = await SeedBookAsync(factory, new Book { Title = $"{m}red {m}green" }, ct);

        var ids = await provider.SearchBookIdsAsync($"{m}red {m}blue", ct);

        Assert.Contains(both, ids);
        Assert.DoesNotContain(onlyOne, ids);
    }

    [Fact]
    public async Task Fts_NoMatch_ReturnsEmpty()
    {
        Assert.SkipUnless(_fixture.IsAvailable, _fixture.SkipReason);
        var ct = TestContext.Current.CancellationToken;
        var (sp, factory, provider) = await BuildAsync(ct);
        await using var scope = sp;

        var ids = await provider.SearchBookIdsAsync(Guid.NewGuid().ToString("N"), ct);

        Assert.Empty(ids);
    }

    [Fact]
    public async Task Fts_BlankQuery_ReturnsEmpty_WithoutHittingDb()
    {
        Assert.SkipUnless(_fixture.IsAvailable, _fixture.SkipReason);
        var ct = TestContext.Current.CancellationToken;
        var (sp, _, provider) = await BuildAsync(ct);
        await using var scope = sp;

        Assert.Empty(await provider.SearchBookIdsAsync("   ", ct));
    }

    // ---- Case-insensitive Equals ----

    [Fact]
    public async Task TextEquals_IsCaseInsensitive()
    {
        Assert.SkipUnless(_fixture.IsAvailable, _fixture.SkipReason);
        var ct = TestContext.Current.CancellationToken;
        var (sp, factory, provider) = await BuildAsync(ct);
        await using var scope = sp;

        var marker = Guid.NewGuid().ToString("N");
        var stored = $"Dragon{marker}";
        var id = await SeedBookAsync(factory, new Book { Title = stored }, ct);

        // A lowercase query matches the mixed-case stored title.
        var ids = await RunTextAsync(factory, provider, "Title", SearchOperator.Equals, stored.ToLowerInvariant(), ct);

        Assert.Contains(id, ids);
    }

    [Fact]
    public async Task RelationEquals_IsCaseInsensitive()
    {
        Assert.SkipUnless(_fixture.IsAvailable, _fixture.SkipReason);
        var ct = TestContext.Current.CancellationToken;
        var (sp, factory, provider) = await BuildAsync(ct);
        await using var scope = sp;

        var marker = Guid.NewGuid().ToString("N");
        var publisherName = $"Penguin{marker}";
        int id;
        await using (var db = await factory.CreateDbContextAsync(ct))
        {
            var publisher = new Publisher { Name = publisherName };
            var book = new Book { Title = $"Has {marker}", Publisher = publisher };
            db.Books.Add(book);
            await db.SaveChangesAsync(ct);
            id = book.BookId;
        }

        var ids = await RunRelationAsync(factory, provider, SearchField.Publisher, SearchOperator.Equals,
            publisherName.ToUpperInvariant(), ct);

        Assert.Contains(id, ids);
    }

    // ---- Wildcard escaping ----

    [Fact]
    public async Task Contains_EscapesLikeWildcards()
    {
        Assert.SkipUnless(_fixture.IsAvailable, _fixture.SkipReason);
        var ct = TestContext.Current.CancellationToken;
        var (sp, factory, provider) = await BuildAsync(ct);
        await using var scope = sp;

        var marker = Guid.NewGuid().ToString("N");
        var literalPercent = await SeedBookAsync(factory, new Book { Title = $"discount 50% off {marker}" }, ct);
        var noPercent = await SeedBookAsync(factory, new Book { Title = $"discount 50ish off {marker}" }, ct);

        // "50%" must match the literal percent only — not act as a LIKE wildcard that swallows "50ish".
        var ids = await RunTextAsync(factory, provider, "Title", SearchOperator.Contains, "50%", ct);

        Assert.Contains(literalPercent, ids);
        Assert.DoesNotContain(noPercent, ids);
    }

    // ---- Full operator matrix translates to SQL ----

    [Fact]
    public async Task AllTextOperators_TranslateToSql()
    {
        Assert.SkipUnless(_fixture.IsAvailable, _fixture.SkipReason);
        var ct = TestContext.Current.CancellationToken;
        var (sp, factory, provider) = await BuildAsync(ct);
        await using var scope = sp;

        foreach (var op in Enum.GetValues<SearchOperator>())
        {
            // A throwing call here means Npgsql could not render the predicate — that is the failure we catch.
            var ids = await RunTextAsync(factory, provider, "Title", op, "anything", ct);
            Assert.NotNull(ids);
        }
    }

    [Theory]
    [InlineData(SearchField.Author)]
    [InlineData(SearchField.Publisher)]
    [InlineData(SearchField.Series)]
    [InlineData(SearchField.Category)]
    [InlineData(SearchField.Format)]
    [InlineData(SearchField.Language)]
    [InlineData(SearchField.Rating)]
    [InlineData(SearchField.Status)]
    [InlineData(SearchField.Location)]
    [InlineData(SearchField.Owner)]
    public async Task AllRelationOperators_TranslateToSql(SearchField field)
    {
        Assert.SkipUnless(_fixture.IsAvailable, _fixture.SkipReason);
        var ct = TestContext.Current.CancellationToken;
        var (sp, factory, provider) = await BuildAsync(ct);
        await using var scope = sp;

        foreach (var op in Enum.GetValues<SearchOperator>())
        {
            var ids = await RunRelationAsync(factory, provider, field, op, "anything", ct);
            Assert.NotNull(ids);
        }
    }
}
