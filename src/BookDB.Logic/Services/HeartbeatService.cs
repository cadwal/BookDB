using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using BookDB.Data.DbContexts;
using BookDB.Models;
using BookDB.Models.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace BookDB.Logic.Services;

/// <summary>The presence of other live clients in the shared database, for the connect-time concurrency check.</summary>
public interface IHeartbeatService
{
    string SessionId { get; }

    /// <summary>True only for a remote backend; the local SQLite backend has no concurrency to detect.</summary>
    bool IsEnabled { get; }

    /// <summary>Other clients seen within the staleness window — excludes this session and crashed (stale) rows.</summary>
    Task<IReadOnlyList<ClientSession>> GetActiveSessionsAsync(CancellationToken ct = default);
}

/// <summary>
/// Records this client's session row in the shared database and refreshes its last-seen timestamp on a timer,
/// so concurrent access to a remote backend can be detected. A row older than <see cref="StalenessWindow"/> is
/// a crashed client: it is ignored by the active-session query and removed opportunistically, so a crash can
/// never lock anyone out. No-op for the local SQLite backend.
/// </summary>
public sealed class HeartbeatService : IHeartbeatService, IHostedService, IAsyncDisposable
{
    public static readonly TimeSpan StalenessWindow = TimeSpan.FromMinutes(3);
    public static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(60);

    private readonly IDbContextFactory<BookDbContext> _factory;
    private readonly TimeProvider _clock;
    private readonly string _hostname;
    private readonly string _userName;
    private readonly string _appVersion;
    private ITimer? _timer;

    public string SessionId { get; } = Guid.NewGuid().ToString("N");
    public bool IsEnabled { get; }

    public HeartbeatService(IDbContextFactory<BookDbContext> factory, AppSettings appSettings, TimeProvider clock)
    {
        _factory = factory;
        _clock = clock;
        IsEnabled = appSettings.Backend == DatabaseBackend.PostgreSql;
        _hostname = Environment.MachineName;
        _userName = Environment.UserName;
        _appVersion = Assembly.GetEntryAssembly()
            ?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";
    }

    public async Task StartAsync(CancellationToken ct)
    {
        if (!IsEnabled)
            return;

        var now = _clock.GetUtcNow().UtcDateTime;
        await using var db = await _factory.CreateDbContextAsync(ct);

        await RemoveDeadPredecessorsAsync(db, now, ct);

        db.ClientSessions.Add(new ClientSession
        {
            SessionId = SessionId,
            Hostname = _hostname,
            UserName = _userName,
            AppVersion = _appVersion,
            StartedAt = now,
            LastSeenAt = now,
        });
        await db.SaveChangesAsync(ct);

        _timer = _clock.CreateTimer(_ => _ = RefreshAsync(), null, RefreshInterval, RefreshInterval);
    }

    public async Task StopAsync(CancellationToken ct)
    {
        if (_timer is not null)
        {
            await _timer.DisposeAsync();
            _timer = null;
        }

        if (!IsEnabled)
            return;

        try
        {
            await using var db = await _factory.CreateDbContextAsync(ct);
            await db.ClientSessions.Where(s => s.SessionId == SessionId).ExecuteDeleteAsync(ct);
        }
        catch (Exception ex)
        {
            // Non-fatal: a leftover row ages out of the staleness window on its own.
            Log.Debug(ex, "HeartbeatService: could not remove the session row on stop");
        }
    }

    public async Task<IReadOnlyList<ClientSession>> GetActiveSessionsAsync(CancellationToken ct = default)
    {
        var cutoff = _clock.GetUtcNow().UtcDateTime - StalenessWindow;
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.ClientSessions.AsNoTracking()
            .Where(s => s.SessionId != SessionId && s.LastSeenAt >= cutoff)
            .ToListAsync(ct);
    }

    /// <summary>Updates this session's last-seen timestamp; the refresh timer fires it, but tests call it directly.</summary>
    public async Task RefreshAsync(CancellationToken ct = default)
    {
        try
        {
            var now = _clock.GetUtcNow().UtcDateTime;
            await using var db = await _factory.CreateDbContextAsync(ct);
            await db.ClientSessions.Where(s => s.SessionId == SessionId)
                .ExecuteUpdateAsync(set => set.SetProperty(s => s.LastSeenAt, now), ct);
        }
        catch (Exception ex)
        {
            // Non-fatal: a transient blip self-heals on the next tick; a real loss is handled at the write path.
            Log.Debug(ex, "HeartbeatService: heartbeat refresh failed");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_timer is not null)
            await _timer.DisposeAsync();
    }

    // Removes rows that cannot represent a live rival: those past the staleness window (crashed clients), and
    // any from this same host+user. The single-instance lock allows only one live client per machine+user, so a
    // same-host+user row is always a dead predecessor of this instance — clearing it stops a crash or forced
    // exit followed by a quick relaunch from falsely reporting "another client is connected". Runs before this
    // session's own row is added, so it never deletes the row we are about to insert.
    private Task RemoveDeadPredecessorsAsync(BookDbContext db, DateTime now, CancellationToken ct)
    {
        var cutoff = now - StalenessWindow;
        return db.ClientSessions
            .Where(s => s.LastSeenAt < cutoff || (s.Hostname == _hostname && s.UserName == _userName))
            .ExecuteDeleteAsync(ct);
    }
}
