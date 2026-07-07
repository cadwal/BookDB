using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using BookDB.Data;
using BookDB.Data.DbContexts;
using BookDB.Logic.Services;
using BookDB.Models.Entities;
using DbUp;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BookDB.Logic.Tests;

/// <summary>
/// Content mapping for every facet: each of the ten facet groups must draw its values (and counts) from its own
/// field, not another. One book carries a unique "solo" value in each dimension and two books share a "shared"
/// value, so a facet wired to the wrong field would surface the wrong names or counts. Temp-file SQLite (FTS5 and
/// the facet joins need the real provider); DbUp builds the schema.
/// </summary>
public sealed class BookSearchServiceFacetTests : IDisposable
{
    private readonly string _dbPath;
    private readonly TestBookDbContextFactory _factory;
    private readonly BookSearchService _sut;

    public BookSearchServiceFacetTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"bookdb_facet_test_{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={_dbPath}";

        var upgrader = SqliteExtensions.SqliteDatabase(DeployChanges.To, connectionString)
            .WithScriptsEmbeddedInAssembly(
                Assembly.GetAssembly(typeof(BookDB.Data.Sqlite.SqliteDbUpRunner))!,
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
        _sut = new BookSearchService(_factory, new BookDB.Data.Sqlite.SqliteBookSearchProvider(_factory));
    }

    public void Dispose()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        try { File.Delete(_dbPath); } catch { /* best effort */ }
    }

    [Fact]
    public async Task EveryFacet_DrawsItsValuesFromItsOwnFieldWithCorrectCounts()
    {
        var ct = TestContext.Current.CancellationToken;
        await FacetSample.SeedAsync(_factory, ct);

        // Author's facet name is the SortName; the rest use the lookup Name. Each pair below is distinct across
        // fields, so a facet reading the wrong column would return names that don't match here.
        var expected = new[]
        {
            (Facet: "Author",    Solo: "Ann Author",     Shared: "Bob Writer"),
            (Facet: "Series",    Solo: "Solo Series",    Shared: "Shared Series"),
            (Facet: "Publisher", Solo: "Solo Publisher", Shared: "Shared Publisher"),
            (Facet: "Category",  Solo: "Solo Category",  Shared: "Shared Category"),
            (Facet: "Format",    Solo: "Solo Format",    Shared: "Shared Format"),
            (Facet: "Language",  Solo: "Solo Language",  Shared: "Shared Language"),
            (Facet: "Rating",    Solo: "Solo Rating",    Shared: "Shared Rating"),
            (Facet: "Status",    Solo: "Solo Status",    Shared: "Shared Status"),
            (Facet: "Location",  Solo: "Solo Location",  Shared: "Shared Location"),
            (Facet: "Owner",     Solo: "Solo Owner",     Shared: "Shared Owner"),
        };

        foreach (var (facet, solo, shared) in expected)
        {
            var counts = await _sut.GetFacetCountsAsync(new HashSet<int>(), facet, ct);
            var byName = counts.ToDictionary(c => c.Name, c => c.Count);

            Assert.True(byName.Count == 2, $"{facet} facet returned {byName.Count} values ({string.Join(", ", byName.Keys)}); expected exactly '{solo}' and '{shared}'.");
            Assert.True(byName.TryGetValue(solo, out var soloCount), $"{facet} facet is missing '{solo}'.");
            Assert.Equal(1, soloCount);
            Assert.True(byName.TryGetValue(shared, out var sharedCount), $"{facet} facet is missing '{shared}'.");
            Assert.Equal(2, sharedCount);
        }
    }
}
