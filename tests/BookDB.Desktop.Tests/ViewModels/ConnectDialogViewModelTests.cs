using System;
using System.Collections.Generic;
using BookDB.Desktop.ViewModels;
using BookDB.Models.Entities;
using Xunit;

namespace BookDB.Desktop.Tests.ViewModels;

public sealed class ConnectDialogViewModelTests
{
    private sealed class FixedClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; }
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static readonly DateTimeOffset Anchor = new(2026, 6, 16, 12, 0, 0, TimeSpan.Zero);

    private static ClientSession Session(string id, DateTime lastSeen, string host = "host", string version = "1.2.0")
        => new() { SessionId = id, Hostname = host, UserName = "u", AppVersion = version, StartedAt = lastSeen, LastSeenAt = lastSeen };

    private static ConnectDialogViewModel CreateViewModel(params ClientSession[] sessions)
        => new(sessions, new FixedClock { Now = Anchor });

    // ---- RelativeTime bucketing (culture-independent) ----

    [Theory]
    [InlineData(0, RelativeTimeUnit.Seconds, 0)]
    [InlineData(42, RelativeTimeUnit.Seconds, 42)]
    [InlineData(59, RelativeTimeUnit.Seconds, 59)]
    [InlineData(60, RelativeTimeUnit.Minutes, 1)]
    [InlineData(90, RelativeTimeUnit.Minutes, 1)]
    [InlineData(59 * 60, RelativeTimeUnit.Minutes, 59)]
    [InlineData(60 * 60, RelativeTimeUnit.Hours, 1)]
    [InlineData(23 * 60 * 60, RelativeTimeUnit.Hours, 23)]
    [InlineData(24 * 60 * 60, RelativeTimeUnit.Days, 1)]
    public void RelativeTime_FromAge_BucketsByMagnitude(int ageSeconds, RelativeTimeUnit unit, long value)
    {
        var rt = RelativeTime.FromAge(TimeSpan.FromSeconds(ageSeconds));

        Assert.Equal(unit, rt.Unit);
        Assert.Equal(value, rt.Value);
    }

    [Fact]
    public void RelativeTime_NegativeAge_ClampsToZeroSeconds()
    {
        var rt = RelativeTime.FromAge(TimeSpan.FromSeconds(-5));

        Assert.Equal(RelativeTimeUnit.Seconds, rt.Unit);
        Assert.Equal(0, rt.Value);
    }

    // ---- Session selection ----

    [Fact]
    public void Constructor_SurfacesTheMostRecentlySeenSession()
    {
        var vm = CreateViewModel(
            Session("old", Anchor.UtcDateTime.AddSeconds(-120), host: "old-host", version: "1.0.0"),
            Session("recent", Anchor.UtcDateTime.AddSeconds(-10), host: "recent-host", version: "1.2.0"));

        Assert.Equal("recent-host", vm.Hostname);
        Assert.Equal("1.2.0", vm.Version);
        Assert.Contains("recent-host", vm.BodyText);
        Assert.Contains("1.2.0", vm.BodyText);
    }

    // ---- Countdown gating ----

    [Fact]
    public void ConnectAnyway_DisabledUntilCountdownReachesZero()
    {
        var vm = CreateViewModel(Session("a", Anchor.UtcDateTime.AddSeconds(-5)));

        Assert.Equal(ConnectDialogViewModel.CountdownStartSeconds, vm.CountdownSeconds);
        Assert.False(vm.CanConnectAnyway);
        Assert.False(vm.ConnectAnywayCommand.CanExecute(null));

        for (int i = 0; i < ConnectDialogViewModel.CountdownStartSeconds; i++)
            vm.Tick();

        Assert.True(vm.CanConnectAnyway);
        Assert.True(vm.ConnectAnywayCommand.CanExecute(null));
        Assert.Equal(0, vm.CountdownSeconds);
    }

    [Fact]
    public void Tick_DoesNotCountBelowZero()
    {
        var vm = CreateViewModel(Session("a", Anchor.UtcDateTime));

        for (int i = 0; i < ConnectDialogViewModel.CountdownStartSeconds + 3; i++)
            vm.Tick();

        Assert.Equal(0, vm.CountdownSeconds);
    }

    // ---- Results ----

    [Fact]
    public void Quit_SetsQuitResultAndCloses()
    {
        var vm = CreateViewModel(Session("a", Anchor.UtcDateTime));
        var closed = false;
        vm.CloseDialog = () => closed = true;

        vm.QuitCommand.Execute(null);

        Assert.Equal(ConnectChoice.Quit, vm.Result);
        Assert.True(closed);
    }

    [Fact]
    public void ConnectAnyway_AfterCountdown_SetsConnectResultAndCloses()
    {
        var vm = CreateViewModel(Session("a", Anchor.UtcDateTime));
        var closed = false;
        vm.CloseDialog = () => closed = true;
        for (int i = 0; i < ConnectDialogViewModel.CountdownStartSeconds; i++)
            vm.Tick();

        vm.ConnectAnywayCommand.Execute(null);

        Assert.Equal(ConnectChoice.ConnectAnyway, vm.Result);
        Assert.True(closed);
    }

    [Fact]
    public void Result_NullBeforeAnyChoice()
    {
        var vm = CreateViewModel(Session("a", Anchor.UtcDateTime));

        Assert.Null(vm.Result);
    }
}
