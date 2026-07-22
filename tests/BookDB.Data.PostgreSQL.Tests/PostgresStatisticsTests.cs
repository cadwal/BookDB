using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BookDB.Data;
using BookDB.Data.DbContexts;
using BookDB.Logic.Services;
using BookDB.Models.Entities;
using BookDB.Models.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BookDB.Data.PostgreSQL.Tests;

/// <summary>
/// Statistics on Postgres. <c>GetBooksPerYearAsync</c> used to project a <c>ValueTuple</c> inside the query,
/// which EF emits as a SQL composite (record) that Npgsql cannot read — the Statistics window was blank and
/// the read threw <see cref="InvalidCastException"/>. Materialising the row and mapping to the tuple in memory
/// keeps it working on the remote backend.
/// </summary>
public sealed class PostgresStatisticsTests : IClassFixture<PostgresTestDbFixture>
{
    private readonly PostgresTestDbFixture _fixture;

    public PostgresStatisticsTests(PostgresTestDbFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task GetBooksPerYear_ReturnsYearCounts_OnPostgres()
    {
        Assert.SkipUnless(_fixture.IsAvailable, _fixture.SkipReason);
        var ct = TestContext.Current.CancellationToken;

        var runner = new PostgresDbUpRunner(_fixture.ConnectionString, NullLogger<DatabaseStartupService>.Instance);
        await runner.RunAsync(new Progress<(int applied, int total)>(), ct);

        var services = new ServiceCollection();
        services.AddSingleton<IDataChangeTracker, DataChangeTracker>();
        services.AddPostgresProvider(_fixture.ConnectionString);
        await using var sp = services.BuildServiceProvider();
        var factory = sp.GetRequiredService<IDbContextFactory<BookDbContext>>();

        var tag = Guid.NewGuid().ToString("N")[..8];
        await using (var db = await factory.CreateDbContextAsync(ct))
        {
            db.Books.AddRange(
                new Book { Title = $"{tag}-a", Added = new DateTime(2021, 2, 1), Updated = new DateTime(2021, 2, 1) },
                new Book { Title = $"{tag}-b", Added = new DateTime(2021, 9, 1), Updated = new DateTime(2021, 9, 1) },
                new Book { Title = $"{tag}-c", Added = new DateTime(2024, 5, 1), Updated = new DateTime(2024, 5, 1) });
            await db.SaveChangesAsync(ct);
        }

        var service = new StatisticsService(factory);

        // The pre-fix query threw InvalidCastException here on Postgres.
        var result = await service.GetBooksPerYearAsync(ct);

        Assert.Contains(result, r => r.Year == 2021 && r.Count >= 2);
        Assert.Contains(result, r => r.Year == 2024 && r.Count >= 1);
    }

    [Fact]
    public async Task GetTopAuthorsAndGrowth_TranslateAndRank_OnPostgres()
    {
        Assert.SkipUnless(_fixture.IsAvailable, _fixture.SkipReason);
        var ct = TestContext.Current.CancellationToken;

        var runner = new PostgresDbUpRunner(_fixture.ConnectionString, NullLogger<DatabaseStartupService>.Instance);
        await runner.RunAsync(new Progress<(int applied, int total)>(), ct);

        var services = new ServiceCollection();
        services.AddSingleton<IDataChangeTracker, DataChangeTracker>();
        services.AddPostgresProvider(_fixture.ConnectionString);
        await using var sp = services.BuildServiceProvider();
        var factory = sp.GetRequiredService<IDbContextFactory<BookDbContext>>();

        var tag = Guid.NewGuid().ToString("N")[..8];
        await using (var db = await factory.CreateDbContextAsync(ct))
        {
            var authorRoleId = db.ContributorRoles.First(r => r.Code == "Author").ContributorRoleId;
            var pat = new Person { DisplayName = $"{tag}-Pat", SortName = $"Pat, {tag}" };
            db.People.Add(pat);
            await db.SaveChangesAsync(ct);

            var now = DateTime.UtcNow;
            foreach (var i in Enumerable.Range(0, 2))
            {
                var book = new Book { Title = $"{tag}-{i}", Added = now, Updated = now };
                db.Books.Add(book);
                await db.SaveChangesAsync(ct);
                db.BookContributors.Add(new BookContributor { BookId = book.BookId, PersonId = pat.PersonId, ContributorRoleId = authorRoleId });
            }
            await db.SaveChangesAsync(ct);
        }

        var service = new StatisticsService(factory);

        var authors = await service.GetTopAuthorsAsync(50, ct);
        Assert.Contains(authors, a => a.Label == $"{tag}-Pat" && a.Count == 2);

        // The year+month grouping and cumulative mapping must also read back cleanly on Npgsql.
        var growth = await service.GetLibraryGrowthAsync(ct);
        Assert.NotEmpty(growth);
        Assert.True(growth[^1].CumulativeCount >= 2);
    }
}
