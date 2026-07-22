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

namespace BookDB.Data.MySql.Tests;

/// <summary>
/// Statistics on MySQL/MariaDB. The riskiest query is <c>GetBooksPerYearAsync</c> — it groups by a date-part
/// (<c>Added.Year</c>), which each provider translates differently — so it gets a value assertion; the rest are
/// exercised end-to-end so any expression the provider can't render to SQL is caught. Run on both engines via
/// the subclasses at the bottom.
/// </summary>
public abstract class MySqlStatisticsTests
{
    private readonly MySqlTestDbFixture _fixture;

    protected MySqlStatisticsTests(MySqlTestDbFixture fixture) => _fixture = fixture;

    private async Task<(ServiceProvider sp, IDbContextFactory<BookDbContext> factory)> BuildProviderAsync(CancellationToken ct)
    {
        var runner = new MySqlDbUpRunner(_fixture.ConnectionString, NullLogger<DatabaseStartupService>.Instance);
        await runner.RunAsync(new Progress<(int applied, int total)>(), ct);

        var services = new ServiceCollection();
        services.AddSingleton<IDataChangeTracker, DataChangeTracker>();
        services.AddMySqlProvider(_fixture.ConnectionString);
        var sp = services.BuildServiceProvider();
        return (sp, sp.GetRequiredService<IDbContextFactory<BookDbContext>>());
    }

    [Fact]
    public async Task GetBooksPerYear_ReturnsYearCounts()
    {
        Assert.SkipUnless(_fixture.IsAvailable, _fixture.SkipReason);
        var ct = TestContext.Current.CancellationToken;
        var (sp, factory) = await BuildProviderAsync(ct);
        await using var scope = sp;

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

        var result = await service.GetBooksPerYearAsync(ct);

        Assert.Contains(result, r => r.Year == 2021 && r.Count >= 2);
        Assert.Contains(result, r => r.Year == 2024 && r.Count >= 1);
    }

    [Fact]
    public async Task AllStatistics_TranslateToSql()
    {
        Assert.SkipUnless(_fixture.IsAvailable, _fixture.SkipReason);
        var ct = TestContext.Current.CancellationToken;
        var (sp, factory) = await BuildProviderAsync(ct);
        await using var scope = sp;

        // Seed one fully-populated book so every breakdown has a row to render (and a non-empty grouping path).
        var tag = Guid.NewGuid().ToString("N")[..8];
        await using (var db = await factory.CreateDbContextAsync(ct))
        {
            db.Books.Add(new Book
            {
                Title = $"{tag}-stats", PubDate = "2020", Added = DateTime.UtcNow, Updated = DateTime.UtcNow,
            });
            await db.SaveChangesAsync(ct);
        }

        var service = new StatisticsService(factory);

        // A throwing call here means the provider could not render the query — that is the failure we catch.
        Assert.NotNull(await service.GetBooksPerYearAsync(ct));
        Assert.NotNull(await service.GetLibraryGrowthAsync(ct));
        Assert.NotNull(await service.GetBreakdownByFormatAsync(ct));
        Assert.NotNull(await service.GetBreakdownByCollectionAsync(ct));
        Assert.NotNull(await service.GetBreakdownByLanguageAsync(ct));
        Assert.NotNull(await service.GetBreakdownByPublishedYearAsync(ct));
        Assert.NotNull(await service.GetTopAuthorsAsync(12, ct));
        Assert.True(await service.GetTotalBookCountAsync(ct) >= 1);
    }
}

public sealed class MySqlServerStatisticsTests : MySqlStatisticsTests, IClassFixture<MySqlServerFixture>
{
    public MySqlServerStatisticsTests(MySqlServerFixture fixture) : base(fixture) { }
}

public sealed class MariaDbStatisticsTests : MySqlStatisticsTests, IClassFixture<MariaDbFixture>
{
    public MariaDbStatisticsTests(MariaDbFixture fixture) : base(fixture) { }
}
