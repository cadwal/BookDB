using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using BookDB.Data.DbContexts;
using BookDB.Logic.Services;
using BookDB.Models;
using BookDB.Models.Entities;
using DbUp;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BookDB.Logic.Tests;

/// <summary>
/// Exercises the heartbeat against a real temp-file SQLite database (the Postgres path is covered by the
/// container-backed test in BookDB.Data.PostgreSQL.Tests). Backend is set to PostgreSql so the write path runs
/// even though the store is SQLite — the service's EF code is backend-agnostic; only <see cref="HeartbeatService.IsEnabled"/>
/// branches on it.
/// </summary>
public sealed class HeartbeatServiceTests : IDisposable
{
    private sealed class FixedClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; }
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private readonly string _dbPath;
    private readonly TestBookDbContextFactory _factory;
    private readonly FixedClock _clock = new() { Now = new DateTimeOffset(2026, 6, 16, 12, 0, 0, TimeSpan.Zero) };

    public HeartbeatServiceTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"bookdb_heartbeat_{Guid.NewGuid():N}.db");
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
            .Options;
        _factory = new TestBookDbContextFactory(options);
    }

    public void Dispose()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        try { File.Delete(_dbPath); } catch { /* best effort */ }
    }

    private HeartbeatService CreateService(DatabaseBackend backend = DatabaseBackend.PostgreSql)
        => new(_factory, new AppSettings { Backend = backend }, _clock);

    private async Task InsertSessionAsync(string id, DateTime lastSeen)
    {
        await using var db = _factory.CreateDbContext();
        db.ClientSessions.Add(new ClientSession
        {
            SessionId = id, Hostname = "host", UserName = "user", AppVersion = "1.0", StartedAt = lastSeen, LastSeenAt = lastSeen,
        });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task<int> SessionCountAsync()
    {
        await using var db = _factory.CreateDbContext();
        return await db.ClientSessions.CountAsync(TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData(DatabaseBackend.Sqlite, false)]
    [InlineData(DatabaseBackend.PostgreSql, true)]
    public void IsEnabled_OnlyForRemoteBackend(DatabaseBackend backend, bool expected)
    {
        Assert.Equal(expected, CreateService(backend).IsEnabled);
    }

    [Fact]
    public async Task StartAsync_LocalBackend_WritesNoRow()
    {
        var service = CreateService(DatabaseBackend.Sqlite);

        await service.StartAsync(TestContext.Current.CancellationToken);
        await service.StopAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, await SessionCountAsync());
    }

    [Fact]
    public async Task StartAsync_RemoteBackend_InsertsOwnRowWithLastSeenNow()
    {
        var service = CreateService();

        await service.StartAsync(TestContext.Current.CancellationToken);

        await using var db = _factory.CreateDbContext();
        var row = await db.ClientSessions.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(service.SessionId, row.SessionId);
        Assert.Equal(_clock.Now.UtcDateTime, row.LastSeenAt);

        await service.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task RefreshAsync_AdvancesLastSeenToCurrentTime()
    {
        var service = CreateService();
        await service.StartAsync(TestContext.Current.CancellationToken);

        _clock.Now = _clock.Now.AddSeconds(90);
        await service.RefreshAsync(TestContext.Current.CancellationToken);

        await using var db = _factory.CreateDbContext();
        var row = await db.ClientSessions.SingleAsync(s => s.SessionId == service.SessionId, TestContext.Current.CancellationToken);
        Assert.Equal(_clock.Now.UtcDateTime, row.LastSeenAt);

        await service.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task StopAsync_RemovesOwnRow()
    {
        var service = CreateService();
        await service.StartAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1, await SessionCountAsync());

        await service.StopAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, await SessionCountAsync());
    }

    [Fact]
    public async Task GetActiveSessions_ReturnsOtherFreshSessions_ExcludesOwnAndStale()
    {
        var now = _clock.Now.UtcDateTime;
        await InsertSessionAsync("fresh", now.AddMinutes(-1));
        await InsertSessionAsync("stale", now.AddMinutes(-4));
        var service = CreateService();
        await service.StartAsync(TestContext.Current.CancellationToken); // own row must not appear in the result

        var active = await service.GetActiveSessionsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["fresh"], active.Select(s => s.SessionId));

        await service.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task GetActiveSessions_OnlyStaleOthers_ReturnsEmpty_SoACrashedClientNeverBlocks()
    {
        await InsertSessionAsync("crashed", _clock.Now.UtcDateTime.AddMinutes(-10));
        var service = CreateService();

        var active = await service.GetActiveSessionsAsync(TestContext.Current.CancellationToken);

        Assert.Empty(active);
    }

    [Fact]
    public async Task StartAsync_RemovesStaleRows()
    {
        await InsertSessionAsync("crashed", _clock.Now.UtcDateTime.AddMinutes(-10));
        var service = CreateService();

        await service.StartAsync(TestContext.Current.CancellationToken);

        await using var db = _factory.CreateDbContext();
        Assert.False(await db.ClientSessions.AnyAsync(s => s.SessionId == "crashed", TestContext.Current.CancellationToken));
        Assert.True(await db.ClientSessions.AnyAsync(s => s.SessionId == service.SessionId, TestContext.Current.CancellationToken));

        await service.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task StartAsync_RemovesPriorSessionFromSameHostAndUser_EvenWhenNotStale()
    {
        // A non-stale row from this same machine+user can only be a dead predecessor (the single-instance lock
        // permits one live client per machine+user), so a crash or forced exit + quick relaunch must not see it.
        await using (var seed = _factory.CreateDbContext())
        {
            seed.ClientSessions.Add(new ClientSession
            {
                SessionId = "self-zombie", Hostname = Environment.MachineName, UserName = Environment.UserName,
                AppVersion = "1.0", StartedAt = _clock.Now.UtcDateTime.AddMinutes(-1), LastSeenAt = _clock.Now.UtcDateTime.AddMinutes(-1),
            });
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
        var service = CreateService();

        await service.StartAsync(TestContext.Current.CancellationToken);

        await using var db = _factory.CreateDbContext();
        Assert.False(await db.ClientSessions.AnyAsync(s => s.SessionId == "self-zombie", TestContext.Current.CancellationToken));

        await service.StopAsync(TestContext.Current.CancellationToken);
    }
}
