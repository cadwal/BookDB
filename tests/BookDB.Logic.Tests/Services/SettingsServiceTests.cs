using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using BookDB.Data;
using BookDB.Data.DbContexts;
using BookDB.Logic.Services;
using DbUp;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BookDB.Logic.Tests.Services;

public sealed class SettingsServiceTests : IDisposable
{
    private readonly string _dbPath;
    private readonly TestBookDbContextFactory _factory;
    private readonly ISettingsService _sut;

    public SettingsServiceTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"bookdb_settings_test_{Guid.NewGuid():N}.db");
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
        var lookupService = new LookupService(_factory, new NullResourceProvider());
        _sut = lookupService;
    }

    public void Dispose()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        try { File.Delete(_dbPath); } catch { /* best effort */ }
    }

    [Fact]
    public async Task SetAsync_ThenGetAsync_ReturnsPersistedValue()
    {
        var ct = TestContext.Current.CancellationToken;

        await _sut.SetAsync("TestKey", "TestValue", ct);

        var result = await _sut.GetAsync("TestKey", ct);

        Assert.Equal("TestValue", result);
    }
}
