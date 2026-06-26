using System;
using System.Threading;
using System.Threading.Tasks;
using BookDB.Models;

namespace BookDB.Logic.Services;

public enum ConnectionHealth
{
    /// <summary>The database is reachable; normal operation.</summary>
    Healthy,

    /// <summary>A loss was observed and the monitor is retrying in the background (status-bar indicator).</summary>
    Degraded,

    /// <summary>Still unreachable after the escalation window — the user has been prompted.</summary>
    Lost,
}

/// <summary>
/// Tracks reachability of a remote database after a read or write observes a connection loss. On the first
/// reported failure it goes <see cref="ConnectionHealth.Degraded"/> and retries on a capped backoff; once the
/// server answers again it returns to <see cref="ConnectionHealth.Healthy"/> and raises <see cref="Reconnected"/>
/// so views can refresh. If it is still unreachable after <see cref="EscalationWindow"/> it goes
/// <see cref="ConnectionHealth.Lost"/> and raises <see cref="Escalated"/> once. No-op for the local SQLite backend.
/// </summary>
public sealed class ConnectionHealthMonitor : IConnectionHealthMonitor, IDisposable
{
    public static readonly TimeSpan EscalationWindow = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan[] Backoff =
        [TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(8)];

    private readonly IConnectionReachabilityProbe _probe;
    private readonly TimeProvider _clock;
    private readonly bool _autoProbe;
    private readonly object _gate = new();

    private CancellationTokenSource? _loopCts;
    private DateTimeOffset _firstFailureAt;

    // Written from the background backoff loop and read from the UI thread, so it must be volatile for visibility.
    private volatile ConnectionHealth _state = ConnectionHealth.Healthy;

    public ConnectionHealthMonitor(IConnectionReachabilityProbe probe, AppSettings appSettings, TimeProvider clock)
        : this(probe, appSettings, clock, autoProbe: true) { }

    // autoProbe is disabled by tests so the state machine can be stepped deterministically via CheckOnceAsync,
    // without a real background backoff loop.
    internal ConnectionHealthMonitor(
        IConnectionReachabilityProbe probe, AppSettings appSettings, TimeProvider clock, bool autoProbe)
    {
        _probe = probe;
        _clock = clock;
        _autoProbe = autoProbe;
        IsEnabled = appSettings.Backend == DatabaseBackend.PostgreSql;
    }

    public bool IsEnabled { get; }

    public ConnectionHealth State => _state;

    public event EventHandler? StateChanged;
    public event EventHandler? Reconnected;
    public event EventHandler? Escalated;

    public void ReportConnectionFailure()
    {
        if (!IsEnabled)
            return;

        lock (_gate)
        {
            if (State != ConnectionHealth.Healthy)
                return;
            _firstFailureAt = _clock.GetUtcNow();
            SetState(ConnectionHealth.Degraded);

            if (_autoProbe)
            {
                // A prior loop may still be unwinding after a reconnect; cancel and drop it before starting a new one.
                _loopCts?.Cancel();
                _loopCts?.Dispose();
                _loopCts = new CancellationTokenSource();
                _ = RunBackoffLoopAsync(_loopCts.Token);
            }
        }
    }

    /// <summary>Computed backoff for the given zero-based retry attempt: 2 s, 4 s, then 8 s capped.</summary>
    public static TimeSpan BackoffFor(int attempt) =>
        Backoff[Math.Min(attempt, Backoff.Length - 1)];

    /// <summary>
    /// One reachability check against the current clock: returns to Healthy (raising <see cref="Reconnected"/>)
    /// when the server answers, or escalates to Lost (raising <see cref="Escalated"/> once) past the window.
    /// The production loop interleaves this with backoff delays; tests call it directly.
    /// </summary>
    public async Task CheckOnceAsync(CancellationToken ct = default)
    {
        if (State == ConnectionHealth.Healthy)
            return;

        bool reachable;
        try { reachable = await _probe.IsReachableAsync(ct); }
        catch { reachable = false; }

        if (reachable)
        {
            SetState(ConnectionHealth.Healthy);
            Reconnected?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (State != ConnectionHealth.Lost && _clock.GetUtcNow() - _firstFailureAt >= EscalationWindow)
        {
            SetState(ConnectionHealth.Lost);
            Escalated?.Invoke(this, EventArgs.Empty);
        }
    }

    private async Task RunBackoffLoopAsync(CancellationToken ct)
    {
        for (int attempt = 0; !ct.IsCancellationRequested; attempt++)
        {
            try { await Task.Delay(BackoffFor(attempt), _clock, ct); }
            catch (OperationCanceledException) { return; }

            await CheckOnceAsync(ct);
            if (State == ConnectionHealth.Healthy)
                return;
        }
    }

    private void SetState(ConnectionHealth state)
    {
        if (_state == state)
            return;
        _state = state;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        _loopCts?.Cancel();
        _loopCts?.Dispose();
    }
}
