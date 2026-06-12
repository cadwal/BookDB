using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using BookDB.Data;
using BookDB.Data.DbContexts;
using BookDB.Data.Interceptors;
using BookDB.Logic.Services;
using DbUp;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BookDB.Logic.Tests.Services;

/// <summary>
/// End-to-end check that settings writes interact correctly with the data-change tracker: a real change flags
/// it (settings count toward "worth backing up"), but re-saving an identical value issues no SQL and so never
/// flags it — which is what keeps view-only chrome churn from triggering a backup on every exit.
/// </summary>
public sealed class SettingsChangeTrackingTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DataChangeTracker _tracker = new();
    private readonly LookupService _settings;

    public SettingsChangeTrackingTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"bookdb_settings_track_test_{Guid.NewGuid():N}.db");
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
            .AddInterceptors(new DataChangeCommandInterceptor(_tracker))
            .Options;

        var factory = new TestBookDbContextFactory(options);
        _settings = new LookupService(factory, new NullResourceProvider());
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { File.Delete(_dbPath); } catch { /* best effort */ }
    }

    [Fact]
    public async Task SetAsync_NewValue_FlagsTracker()
    {
        var ct = TestContext.Current.CancellationToken;

        await _settings.SetAsync("UiTheme", "Vibrant", ct);

        Assert.True(_tracker.HasChanges);
    }

    [Fact]
    public async Task SetAsync_UnchangedValue_DoesNotFlagTracker()
    {
        var ct = TestContext.Current.CancellationToken;
        await _settings.SetAsync("UiTheme", "Vibrant", ct);
        _tracker.Reset();

        // Re-saving the same value must be a no-op (no SQL, no flag).
        await _settings.SetAsync("UiTheme", "Vibrant", ct);

        Assert.False(_tracker.HasChanges);
    }

    [Fact]
    public async Task SetAsync_ChangedValue_FlagsTracker()
    {
        var ct = TestContext.Current.CancellationToken;
        await _settings.SetAsync("UiTheme", "Vibrant", ct);
        _tracker.Reset();

        await _settings.SetAsync("UiTheme", "Mocha", ct);

        Assert.True(_tracker.HasChanges);
    }
}
