using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BookDB.Logic.Services;
using BookDB.Models;
using Xunit;

namespace BookDB.Logic.Tests;

/// <summary>
/// Drives the monitor's state machine deterministically: <c>autoProbe: false</c> suppresses the background
/// backoff loop, so each reachability check is stepped explicitly via <see cref="ConnectionHealthMonitor.CheckOnceAsync"/>
/// against a fixed clock and a scripted probe.
/// </summary>
public sealed class ConnectionHealthMonitorTests
{
    private sealed class FixedClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 6, 16, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class ScriptedProbe : IConnectionReachabilityProbe
    {
        private readonly Queue<bool> _results = new();
        public bool Default { get; set; }
        public void Enqueue(params bool[] values) { foreach (var v in values) _results.Enqueue(v); }
        public Task<bool> IsReachableAsync(CancellationToken ct = default) =>
            Task.FromResult(_results.Count > 0 ? _results.Dequeue() : Default);
    }

    private readonly FixedClock _clock = new();
    private readonly ScriptedProbe _probe = new();

    private ConnectionHealthMonitor Create(DatabaseBackend backend = DatabaseBackend.PostgreSql) =>
        new(_probe, new AppSettings { Backend = backend }, _clock, autoProbe: false);

    [Theory]
    [InlineData(DatabaseBackend.Sqlite, false)]
    [InlineData(DatabaseBackend.PostgreSql, true)]
    public void IsEnabled_OnlyForRemoteBackend(DatabaseBackend backend, bool expected)
    {
        Assert.Equal(expected, Create(backend).IsEnabled);
    }

    [Fact]
    public void ReportConnectionFailure_LocalBackend_StaysHealthy()
    {
        var monitor = Create(DatabaseBackend.Sqlite);

        monitor.ReportConnectionFailure();

        Assert.Equal(ConnectionHealth.Healthy, monitor.State);
    }

    [Fact]
    public void ReportConnectionFailure_GoesDegraded_AndRaisesStateChanged()
    {
        var monitor = Create();
        int changes = 0;
        monitor.StateChanged += (_, _) => changes++;

        monitor.ReportConnectionFailure();

        Assert.Equal(ConnectionHealth.Degraded, monitor.State);
        Assert.Equal(1, changes);
    }

    [Fact]
    public async Task CheckOnce_WhenReachableAgain_ReturnsHealthy_AndRaisesReconnected()
    {
        var monitor = Create();
        monitor.ReportConnectionFailure();
        bool reconnected = false;
        monitor.Reconnected += (_, _) => reconnected = true;

        _probe.Enqueue(true);
        await monitor.CheckOnceAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ConnectionHealth.Healthy, monitor.State);
        Assert.True(reconnected);
    }

    [Fact]
    public async Task CheckOnce_StillDownBeforeWindow_StaysDegraded_NoEscalation()
    {
        var monitor = Create();
        monitor.ReportConnectionFailure();
        bool escalated = false;
        monitor.Escalated += (_, _) => escalated = true;

        _clock.Now = _clock.Now.AddMinutes(2); // inside the 3-minute window
        _probe.Enqueue(false);
        await monitor.CheckOnceAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ConnectionHealth.Degraded, monitor.State);
        Assert.False(escalated);
    }

    [Fact]
    public async Task CheckOnce_StillDownPastWindow_Escalates_Once()
    {
        var monitor = Create();
        monitor.ReportConnectionFailure();
        int escalations = 0;
        monitor.Escalated += (_, _) => escalations++;

        _clock.Now = _clock.Now.AddMinutes(3); // reaches the escalation window
        _probe.Enqueue(false, false);
        await monitor.CheckOnceAsync(TestContext.Current.CancellationToken);
        await monitor.CheckOnceAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ConnectionHealth.Lost, monitor.State);
        Assert.Equal(1, escalations); // raised once on entering Lost, not on every subsequent failure
    }

    [Fact]
    public async Task CheckOnce_RecoversAfterEscalation_ReturnsHealthy()
    {
        var monitor = Create();
        monitor.ReportConnectionFailure();
        _clock.Now = _clock.Now.AddMinutes(4);
        _probe.Enqueue(false);
        await monitor.CheckOnceAsync(TestContext.Current.CancellationToken);
        Assert.Equal(ConnectionHealth.Lost, monitor.State);

        _probe.Enqueue(true);
        await monitor.CheckOnceAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ConnectionHealth.Healthy, monitor.State);
    }

    [Fact]
    public async Task CheckOnce_WhenHealthy_IsNoOp_AndDoesNotProbe()
    {
        var monitor = Create();
        _probe.Default = false; // would force Degraded if it were consulted

        await monitor.CheckOnceAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ConnectionHealth.Healthy, monitor.State);
    }

    [Theory]
    [InlineData(0, 2)]
    [InlineData(1, 4)]
    [InlineData(2, 8)]
    [InlineData(5, 8)] // capped
    public void BackoffFor_FollowsCappedSchedule(int attempt, int expectedSeconds)
    {
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), ConnectionHealthMonitor.BackoffFor(attempt));
    }
}
