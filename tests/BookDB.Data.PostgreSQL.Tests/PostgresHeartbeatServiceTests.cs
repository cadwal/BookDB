using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BookDB.Data;
using BookDB.Data.DbContexts;
using BookDB.Logic.Services;
using BookDB.Models;
using BookDB.Models.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BookDB.Data.PostgreSQL.Tests;

/// <summary>
/// Exercises <see cref="HeartbeatService"/> against a live PostgreSQL container — the remote backend it is
/// actually meant for — proving the session row round-trips and the active-session query ignores stale rows.
/// </summary>
public sealed class PostgresHeartbeatServiceTests : IClassFixture<PostgresTestDbFixture>
{
    private sealed class FixedClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; }
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private readonly PostgresTestDbFixture _fixture;

    public PostgresHeartbeatServiceTests(PostgresTestDbFixture fixture) => _fixture = fixture;

    private async Task<(ServiceProvider sp, IDbContextFactory<BookDbContext> factory)> BuildProviderAsync(CancellationToken ct)
    {
        var runner = new PostgresDbUpRunner(_fixture.ConnectionString, NullLogger<DatabaseStartupService>.Instance);
        await runner.RunAsync(new Progress<(int applied, int total)>(), ct);

        var services = new ServiceCollection();
        services.AddSingleton<BookDB.Models.Interfaces.IDataChangeTracker, DataChangeTracker>();
        services.AddPostgresProvider(_fixture.ConnectionString);
        var sp = services.BuildServiceProvider();
        return (sp, sp.GetRequiredService<IDbContextFactory<BookDbContext>>());
    }

    private static HeartbeatService Create(IDbContextFactory<BookDbContext> factory, TimeProvider clock)
        => new(factory, new AppSettings { Backend = DatabaseBackend.PostgreSql }, clock);

    [Fact]
    public async Task Heartbeat_RegistersAndClearsSession_AndActiveQueryIgnoresStale()
    {
        Assert.SkipUnless(_fixture.IsAvailable, _fixture.SkipReason);
        var ct = TestContext.Current.CancellationToken;
        var (sp, factory) = await BuildProviderAsync(ct);
        await using var scope = sp;

        // The container is shared across the class — start from an empty session table.
        await using (var clean = await factory.CreateDbContextAsync(ct))
            await clean.ClientSessions.ExecuteDeleteAsync(ct);

        var clock = new FixedClock { Now = new DateTimeOffset(2026, 6, 16, 12, 0, 0, TimeSpan.Zero) };

        var sessionA = Create(factory, clock);
        await sessionA.StartAsync(ct);

        // A crashed peer left a stale row; it must never count as an active session.
        await using (var seed = await factory.CreateDbContextAsync(ct))
        {
            seed.ClientSessions.Add(new ClientSession
            {
                SessionId = "stale-peer", Hostname = "old", UserName = "old", AppVersion = "1.0",
                StartedAt = clock.Now.UtcDateTime.AddMinutes(-30), LastSeenAt = clock.Now.UtcDateTime.AddMinutes(-30),
            });
            await seed.SaveChangesAsync(ct);
        }

        var sessionB = Create(factory, clock);
        var active = await sessionB.GetActiveSessionsAsync(ct);
        Assert.Equal([sessionA.SessionId], active.Select(s => s.SessionId));

        await sessionA.RefreshAsync(ct);
        await sessionA.StopAsync(ct);

        var afterStop = await sessionB.GetActiveSessionsAsync(ct);
        Assert.Empty(afterStop);
    }
}
