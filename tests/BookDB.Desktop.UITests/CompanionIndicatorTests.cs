using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.VisualTree;
using BookDB.Desktop.Localization;
using BookDB.Desktop.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BookDB.Desktop.UITests;

/// <summary>
/// The status bar's companion indicator. The companion is a listener with no window of its own, so before
/// this the only place it was ever mentioned was a row in Settings — a companion that was off, or that had
/// quietly failed to take its port, looked exactly like one that was working. Raised in UAT.
/// </summary>
public class CompanionIndicatorTests : HeadlessTest
{
    private sealed class StubReporter : ICompanionStatusReporter
    {
        public CompanionHostStatus Status { get; set; } = CompanionHostStatus.Stopped;

        public int PairedDeviceCount { get; set; }

        public event EventHandler? StatusChanged;

        public void Announce() => StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    [Fact]
    public Task AStoppedCompanion_ShowsNothing() => RunUi(async () =>
    {
        var window = await ShowMainWindow(new StubReporter());

        Assert.False(window.Find<Border>("CompanionIndicator").IsEffectivelyVisible);

        window.Close();
    });

    [Fact]
    public Task ARunningCompanion_SaysSoWithItsPortAndDeviceCount() => RunUi(async () =>
    {
        var window = await ShowMainWindow(new StubReporter
        {
            Status = new CompanionHostStatus(CompanionHostState.Running, Endpoint: "7443"),
            PairedDeviceCount = 2,
        });

        var indicator = window.Find<Border>("CompanionIndicator");
        Assert.True(indicator.IsEffectivelyVisible);

        var texts = OpenPopupTexts(indicator);
        Assert.Contains(string.Format(null, Resources.StatusBar_Companion_Running, "7443"), texts);
        Assert.Contains(string.Format(null, Resources.StatusBar_Companion_Devices, 2), texts);

        window.Close();
    });

    /// <summary>Enabled but not listening is the state most worth surfacing: nothing else about it looks any
    /// different from working, and a phone that cannot connect gives no clue which end is at fault.</summary>
    [Fact]
    public Task ACompanionThatCouldNotStart_SaysSoAndDropsTheDeviceCount() => RunUi(async () =>
    {
        var window = await ShowMainWindow(new StubReporter
        {
            Status = new CompanionHostStatus(CompanionHostState.Failed, Error: "port in use"),
        });

        var indicator = window.Find<Border>("CompanionIndicator");
        Assert.True(indicator.IsEffectivelyVisible);
        Assert.Contains("failed", indicator.Classes);

        var texts = OpenPopupTexts(indicator);
        Assert.Contains(Resources.StatusBar_Companion_Failed, texts);
        Assert.DoesNotContain(string.Format(null, Resources.StatusBar_Companion_Devices, 0), texts);

        window.Close();
    });

    /// <summary>Turning the companion on in Settings, or pairing a phone, has to reach the bar without a
    /// restart — the indicator follows the reporter rather than reading it once at startup.</summary>
    [Fact]
    public Task TurningTheCompanionOn_ReachesTheBarWithoutARestart() => RunUi(async () =>
    {
        var reporter = new StubReporter();
        var window = await ShowMainWindow(reporter);
        var indicator = window.Find<Border>("CompanionIndicator");

        Assert.False(indicator.IsEffectivelyVisible);

        reporter.Status = new CompanionHostStatus(CompanionHostState.Running, Endpoint: "7443");
        reporter.PairedDeviceCount = 1;
        reporter.Announce();
        Ui.Pump();

        Assert.True(indicator.IsEffectivelyVisible);
        Assert.Contains(
            string.Format(null, Resources.StatusBar_Companion_Devices, 1), OpenPopupTexts(indicator));

        window.Close();
    });

    /// <summary>
    /// UAT 10d: a companion that was running and then could not take its port. Two things went untested and
    /// both are here — the transition *into* Failed (the other tests either start there or go the other way),
    /// and the **colour itself**. Asserting `Classes` proves the binding set a class; it says nothing about
    /// whether any style matched it, so a style that never applies looks exactly like one that does.
    /// </summary>
    [Fact]
    public Task ACompanionThatFailsWhileRunning_ActuallyTurnsRed() => RunUi(async () =>
    {
        var reporter = new StubReporter
        {
            Status = new CompanionHostStatus(CompanionHostState.Running, Endpoint: "7443"),
        };
        var window = await ShowMainWindow(reporter);
        var indicator = window.Find<Border>("CompanionIndicator");

        reporter.Status = new CompanionHostStatus(CompanionHostState.Failed, Error: "port in use");
        reporter.Announce();
        Ui.Pump();

        Assert.True(indicator.IsEffectivelyVisible);
        Assert.Contains("failed", indicator.Classes);

        // Both halves: the style sets two brushes, and a guard on one of them passes while the other is
        // still being outranked by a value set in the element's own attributes.
        Assert.True(window.TryFindResource("BrushError", out var error));
        Assert.Equal(error, indicator.BorderBrush);

        Assert.True(window.TryFindResource("BrushWarningBackground", out var warning));
        Assert.Equal(warning, indicator.Background);

        window.Close();
    });

    private static async Task<Window> ShowMainWindow(ICompanionStatusReporter reporter)
    {
        using var host = TestHost.Create(s => s.AddSingleton(reporter));
        var window = (Window)await SurfaceRegistry.ByName("Main").BuildAsync(host);
        window.Show();
        Ui.Pump();
        return window;
    }

    /// <summary>Opens the indicator's tooltip for real and returns every rendered TextBlock text.</summary>
    private static string[] OpenPopupTexts(Border indicator)
    {
        ToolTip.SetIsOpen(indicator, true);
        Ui.Pump();
        try
        {
            var tip = Assert.IsAssignableFrom<Control>(ToolTip.GetTip(indicator));
            return tip.GetVisualDescendants().OfType<TextBlock>()
                .Where(t => t.IsEffectivelyVisible)
                .Select(t => t.Text ?? string.Empty)
                .ToArray();
        }
        finally
        {
            ToolTip.SetIsOpen(indicator, false);
            Ui.Pump();
        }
    }
}
