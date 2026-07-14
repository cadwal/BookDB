using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using BookDB.Desktop.ViewModels;
using BookDB.Desktop.Views;
using Xunit;

namespace BookDB.Desktop.UITests;

/// <summary>
/// Every TabControl-hosted window carries <c>LinearTabOrderBehavior</c> because Avalonia's TabControl
/// otherwise remembers the last-focused descendant of its selected content and, after one full lap,
/// wraps Tab back into that element instead of the window's first control — collapsing the cycle into a
/// two-element oscillation that drops the tab header and earlier controls. These tests drive real Tab and
/// Shift+Tab key presses and assert the focus order is a clean rotation: it returns to where it started,
/// every control appears exactly once per lap, and the same order repeats indefinitely.
/// </summary>
public class TabControlFocusCycleTests : HeadlessTest
{
    [Fact]
    public async Task Maintenance_TabCyclesCleanly_BothDirections()
    {
        await RunUi(async () =>
        {
            using var host = TestHost.Create();
            var window = new MaintenanceDialog { DataContext = host.Resolve<MaintenanceViewModel>() };
            window.Show();
            Ui.Pump();

            AssertCleanCycle(window, forward: true);
            AssertCleanCycle(window, forward: false);
            window.Close();
        });
    }

    [Fact]
    public async Task Maintenance_MoveLibraryTab_TabCyclesCleanly()
    {
        await RunUi(async () =>
        {
            using var host = TestHost.Create();
            var window = new MaintenanceDialog { DataContext = host.Resolve<MaintenanceViewModel>() };
            window.Show();
            Ui.Pump();
            window.Find<TabControl>().SelectedIndex = 1; // Move library — has the target radio group + hidden log
            Ui.Pump();

            AssertCleanCycle(window, forward: true);
            AssertCleanCycle(window, forward: false);
            window.Close();
        });
    }

    [Fact]
    public async Task Settings_DatabaseTab_TabCyclesCleanly()
    {
        await RunUi(async () =>
        {
            using var host = TestHost.Create();
            var vm = host.Resolve<SettingsWindowViewModel>();
            await vm.InitializeAsync();
            var window = new SettingsWindow { DataContext = vm };
            window.Show();
            Ui.Pump();
            vm.SelectedTabIndex = 7; // Database — the backend radio group lives here
            vm.DatabaseTab.IsPostgreSqlSelected = true; // reveal the server form so it joins the cycle
            Ui.Pump();

            AssertCleanCycle(window, forward: true);
            AssertCleanCycle(window, forward: false);
            window.Close();
        });
    }

    [Fact]
    public async Task ManageLookups_TabCyclesCleanly()
    {
        await RunUi(async () =>
        {
            using var host = TestHost.Create();
            var vm = host.Resolve<ManageLookupsViewModel>();
            await vm.InitializeAsync(null);
            var window = new ManageLookupsWindow { DataContext = vm };
            window.Show();
            Ui.Pump();

            AssertCleanCycle(window, forward: true);
            AssertCleanCycle(window, forward: false);
            window.Close();
        });
    }

    /// <summary>Drives Tab (or Shift+Tab) until focus has returned to its starting control twice, then
    /// asserts the captured sequence is a clean rotation: a lap of length ≥ 3, all-distinct within the lap,
    /// repeating exactly. The old oscillation bug fails on the "returns to start twice" wait (it never does)
    /// or on the distinctness check (Close/last-control repeat every other press).</summary>
    private static void AssertCleanCycle(Window window, bool forward)
    {
        var mods = forward ? RawInputModifiers.None : RawInputModifiers.Shift;

        // Establish a deterministic starting focus.
        window.Press(PhysicalKey.Tab, mods);
        var start = Focused(window);
        Assert.NotNull(start);

        var seq = new List<object> { start! };
        var startHits = 1;
        for (var i = 0; i < 500 && startHits < 3; i++)
        {
            window.Press(PhysicalKey.Tab, mods);
            var f = Focused(window);
            Assert.NotNull(f);
            seq.Add(f!);
            if (ReferenceEquals(f, start)) startHits++;
        }

        Assert.True(startHits >= 3, $"{Dir(forward)}: focus never returned to its start twice — the cycle is stuck.");

        var period = seq.FindIndex(1, x => ReferenceEquals(x, start));
        Assert.True(period >= 3, $"{Dir(forward)}: degenerate cycle of period {period} (a stuck oscillation).");

        // Every control in a lap is distinct...
        for (var a = 0; a < period; a++)
            for (var b = a + 1; b < period; b++)
                Assert.False(ReferenceEquals(seq[a], seq[b]),
                    $"{Dir(forward)}: a control is focused twice within a single lap.");

        // ...and the lap repeats exactly.
        for (var i = 0; i < seq.Count; i++)
            Assert.Same(seq[i % period], seq[i]);
    }

    private static object? Focused(Window window) =>
        TopLevel.GetTopLevel(window)?.FocusManager?.GetFocusedElement();

    private static string Dir(bool forward) => forward ? "forward" : "reverse";
}
