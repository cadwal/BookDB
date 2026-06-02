using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using BookDB.Data;
using BookDB.Data.DbContexts;
using BookDB.Logic.Services;
using BookDB.Models.Entities;
using DbUp;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BookDB.Logic.Tests.Services;

public sealed class StatisticsServiceTests : IDisposable
{
    private readonly string _dbPath;
    private readonly TestBookDbContextFactory _factory;
    private readonly StatisticsService _sut;

    public StatisticsServiceTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"bookdb_statistics_test_{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={_dbPath}";

        var upgrader = SqliteExtensions.SqliteDatabase(DeployChanges.To, connectionString)
            .WithScriptsEmbeddedInAssembly(
                Assembly.GetAssembly(typeof(BookDbContext))!,
                name => name.Contains(".Migrations."))
            .LogToNowhere()
            .Build();

        var result = upgrader.PerformUpgrade();
        if (!result.Successful)
            throw new InvalidOperationException($"DbUp migration failed: {result.Error}");

        var options = new DbContextOptionsBuilder<BookDbContext>()
            .UseSqlite(connectionString)
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
            .Options;

        _factory = new TestBookDbContextFactory(options);
        _sut = new StatisticsService(_factory);
    }

    public void Dispose()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        try { File.Delete(_dbPath); } catch { /* best effort */ }
    }

    [Fact]
    public async Task GetBooksPerYearAsync_ReturnsCorrectYearCountPairs()
    {
        var ct = TestContext.Current.CancellationToken;

        using (var db = _factory.CreateDbContext())
        {
            db.Books.AddRange(
                new Book { Title = "Book 2020-A", Added = new DateTime(2020, 1, 1), Updated = new DateTime(2020, 1, 1) },
                new Book { Title = "Book 2020-B", Added = new DateTime(2020, 6, 1), Updated = new DateTime(2020, 6, 1) },
                new Book { Title = "Book 2023-A", Added = new DateTime(2023, 3, 1), Updated = new DateTime(2023, 3, 1) });
            db.SaveChanges();
        }

        var result = await _sut.GetBooksPerYearAsync(ct);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, r => r.Year == 2020 && r.Count == 2);
        Assert.Contains(result, r => r.Year == 2023 && r.Count == 1);
        Assert.True(result[0].Year < result[1].Year, "Results should be ordered by year ascending");
    }

    [Fact]
    public async Task GetBreakdownByFormatAsync_ReturnsCountAndPercentage()
    {
        var ct = TestContext.Current.CancellationToken;

        // Use existing seeded formats (V002_SeedLookups.sql seeds Hardcover=1, Paperback=2, etc.)
        int formatXId;
        int formatYId;

        using (var db = _factory.CreateDbContext())
        {
            formatXId = db.Formats.First(f => f.Name == "Hardcover").FormatId;
            formatYId = db.Formats.First(f => f.Name == "Paperback").FormatId;

            var now = DateTime.UtcNow;
            db.Books.AddRange(
                new Book { Title = "B1", FormatId = formatXId, Added = now, Updated = now },
                new Book { Title = "B2", FormatId = formatXId, Added = now, Updated = now },
                new Book { Title = "B3", FormatId = formatYId, Added = now, Updated = now });
            db.SaveChanges();
        }

        var result = await _sut.GetBreakdownByFormatAsync(ct);

        Assert.NotEmpty(result);
        var totalPercentage = result.Sum(r => r.Percentage);
        Assert.True(Math.Abs(totalPercentage - 100.0) < 1.0, $"Percentages should sum to ~100, got {totalPercentage}");
        Assert.Contains(result, r => r.Label == "Hardcover" && r.Count == 2);
        Assert.Contains(result, r => r.Label == "Paperback" && r.Count == 1);
    }

    [Fact]
    public async Task GetBreakdownByCollectionAsync_ReturnsCountAndPercentage()
    {
        var ct = TestContext.Current.CancellationToken;

        // Use existing seeded collections (V002_SeedLookups.sql seeds Fiction, Non-Fiction, Comics, etc.)
        int colAId;
        int colBId;

        using (var db = _factory.CreateDbContext())
        {
            colAId = db.Collections.First(c => c.Name == "Fiction").CollectionId;
            colBId = db.Collections.First(c => c.Name == "Comics").CollectionId;

            var now = DateTime.UtcNow;
            db.Books.AddRange(
                new Book { Title = "B1", CollectionId = colAId, Added = now, Updated = now },
                new Book { Title = "B2", CollectionId = colAId, Added = now, Updated = now },
                new Book { Title = "B3", CollectionId = colBId, Added = now, Updated = now });
            db.SaveChanges();
        }

        var result = await _sut.GetBreakdownByCollectionAsync(ct);

        Assert.NotEmpty(result);
        var totalPercentage = result.Sum(r => r.Percentage);
        Assert.True(Math.Abs(totalPercentage - 100.0) < 1.0, $"Percentages should sum to ~100, got {totalPercentage}");
        Assert.Contains(result, r => r.Label == "Fiction" && r.Count == 2);
        Assert.Contains(result, r => r.Label == "Comics" && r.Count == 1);
    }

    [Fact]
    public async Task GetBreakdownByLanguageAsync_ReturnsCountAndPercentage()
    {
        var ct = TestContext.Current.CancellationToken;

        // Use existing seeded languages (V002_SeedLookups.sql seeds English, Swedish, etc.)
        int langAId;
        int langBId;

        using (var db = _factory.CreateDbContext())
        {
            langAId = db.Languages.First(l => l.Name == "English").LanguageId;
            langBId = db.Languages.First(l => l.Name == "Swedish").LanguageId;

            var now = DateTime.UtcNow;
            db.Books.AddRange(
                new Book { Title = "B1", LanguageId = langAId, Added = now, Updated = now },
                new Book { Title = "B2", LanguageId = langAId, Added = now, Updated = now },
                new Book { Title = "B3", LanguageId = langBId, Added = now, Updated = now });
            db.SaveChanges();
        }

        var result = await _sut.GetBreakdownByLanguageAsync(ct);

        Assert.NotEmpty(result);
        var totalPercentage = result.Sum(r => r.Percentage);
        Assert.True(Math.Abs(totalPercentage - 100.0) < 1.0, $"Percentages should sum to ~100, got {totalPercentage}");
        Assert.Contains(result, r => r.Label == "English" && r.Count == 2);
        Assert.Contains(result, r => r.Label == "Swedish" && r.Count == 1);
    }

    [Fact]
    public async Task GetBreakdownByPublishedYearAsync_ReturnsCountAndPercentage()
    {
        var ct = TestContext.Current.CancellationToken;

        using (var db = _factory.CreateDbContext())
        {
            var now = DateTime.UtcNow;
            db.Books.AddRange(
                new Book { Title = "B1", PubDate = "2010", Added = now, Updated = now },
                new Book { Title = "B2", PubDate = "2010", Added = now, Updated = now },
                new Book { Title = "B3", PubDate = "2020", Added = now, Updated = now },
                new Book { Title = "B4", PubDate = null,   Added = now, Updated = now });
            db.SaveChanges();
        }

        var result = await _sut.GetBreakdownByPublishedYearAsync(ct);

        // Only rows with non-null PubDate are included
        Assert.Equal(2, result.Count);
        Assert.Contains(result, r => r.Label == "2010" && r.Count == 2);
        Assert.Contains(result, r => r.Label == "2020" && r.Count == 1);
        // Percentages computed against total books (4), so sums to <= 100
        var totalPercentage = result.Sum(r => r.Percentage);
        Assert.True(totalPercentage <= 100.0, "Percentages should not exceed 100");
    }
}
