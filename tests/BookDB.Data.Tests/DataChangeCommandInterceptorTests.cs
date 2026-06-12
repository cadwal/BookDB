using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using BookDB.Data;
using BookDB.Data.DbContexts;
using BookDB.Data.Interceptors;
using BookDB.Models.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BookDB.Data.Tests;

/// <summary>
/// Verifies the command-layer interceptor flags the change tracker for every write — ordinary SaveChanges,
/// bulk ExecuteUpdate/ExecuteDelete (which bypass SaveChanges), and settings — but never for a pure read.
/// </summary>
public sealed class DataChangeCommandInterceptorTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbContextOptions<BookDbContext> _options;
    private readonly DataChangeTracker _tracker = new();

    public DataChangeCommandInterceptorTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"bookdb_cmdinterceptor_test_{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={_dbPath}";

        var upgrader = SqliteExtensions.SqliteDatabase(DbUp.DeployChanges.To, connectionString)
            .WithScriptsEmbeddedInAssembly(
                Assembly.GetAssembly(typeof(DatabaseStartupService))!,
                name => name.Contains(".Migrations."))
            .Build();

        var result = upgrader.PerformUpgrade();
        if (!result.Successful)
            throw new InvalidOperationException($"DbUp migration failed: {result.Error}");

        _options = new DbContextOptionsBuilder<BookDbContext>()
            .UseSqlite(connectionString)
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
            .AddInterceptors(new DataChangeCommandInterceptor(_tracker))
            .Options;
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { File.Delete(_dbPath); } catch { /* best effort */ }
    }

    [Fact]
    public async Task SaveChanges_FlagsTracker_OnLibraryDataWrite()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = new BookDbContext(_options);

        ctx.Publishers.Add(new Publisher { Name = "Acme Press" });
        await ctx.SaveChangesAsync(ct);

        Assert.True(_tracker.HasChanges);
    }

    [Fact]
    public async Task BulkExecuteDelete_FlagsTracker()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = new BookDbContext(_options);
        ctx.Publishers.Add(new Publisher { Name = "To Delete" });
        await ctx.SaveChangesAsync(ct);
        _tracker.Reset();

        // Bulk delete bypasses SaveChanges — only the command interceptor can see it.
        await ctx.Publishers.Where(p => p.Name == "To Delete").ExecuteDeleteAsync(ct);

        Assert.True(_tracker.HasChanges);
    }

    [Fact]
    public async Task BulkExecuteDelete_NoMatchingRows_DoesNotFlag()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = new BookDbContext(_options);

        // A DELETE that matches nothing — like the batch-queue cleanup on a clean startup — is not a change.
        await ctx.Publishers.Where(p => p.Name == "Does Not Exist").ExecuteDeleteAsync(ct);

        Assert.False(_tracker.HasChanges);
    }

    [Fact]
    public async Task BulkExecuteUpdate_NoMatchingRows_DoesNotFlag()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = new BookDbContext(_options);

        await ctx.Publishers.Where(p => p.Name == "Does Not Exist")
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.Name, "Whatever"), ct);

        Assert.False(_tracker.HasChanges);
    }

    [Fact]
    public async Task BulkExecuteUpdate_FlagsTracker()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = new BookDbContext(_options);
        ctx.Publishers.Add(new Publisher { Name = "Before" });
        await ctx.SaveChangesAsync(ct);
        _tracker.Reset();

        await ctx.Publishers.Where(p => p.Name == "Before")
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.Name, "After"), ct);

        Assert.True(_tracker.HasChanges);
    }

    [Fact]
    public async Task SaveChanges_FlagsTracker_OnSettingsWrite()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = new BookDbContext(_options);

        // Settings count too — they are real, user-visible preferences worth backing up.
        ctx.Settings.Add(new Settings { Key = "UiTheme", Value = "Vibrant" });
        await ctx.SaveChangesAsync(ct);

        Assert.True(_tracker.HasChanges);
    }

    [Fact]
    public async Task Read_DoesNotFlagTracker()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = new BookDbContext(_options);
        ctx.Publishers.Add(new Publisher { Name = "Seed" });
        await ctx.SaveChangesAsync(ct);
        _tracker.Reset();

        _ = await ctx.Publishers.AsNoTracking().ToListAsync(ct);

        Assert.False(_tracker.HasChanges);
    }
}
