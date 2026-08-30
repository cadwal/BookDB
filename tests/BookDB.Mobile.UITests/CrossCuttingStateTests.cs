using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Shapes;
using Avalonia.VisualTree;
using BookDB.Mobile.Localization;
using BookDB.Mobile.Services;
using BookDB.Mobile.Tests;
using BookDB.Mobile.ViewModels;
using BookDB.Mobile.Views;
using Xunit;

namespace BookDB.Mobile.UITests;

/// <summary>
/// The states that live across screens rather than on one: the connection chip and what a screen says when the
/// camera says no. Both are decided in a view model and only *seen* here, which is the part a view model test
/// cannot check — a flag that flips while nothing on screen changes is the failure worth catching.
/// </summary>
public class CrossCuttingStateTests : HeadlessTest
{
    [Fact]
    public Task ThePairedShell_ShowsTheConnectionChip() => RunUi(() =>
    {
        var connection = ScreenRegistry.Connected();
        var shell = new MainViewModel(new FakeIdentityStore(paired: true), connection, ScreenRegistry.Pages());
        var window = Phone.Show(new MainView { DataContext = shell });

        // The shell starts out saying Offline and follows the service from there, which in the running app is
        // what the heartbeat's first tick provides.
        connection.Report(ConnectionStatus.Connected);
        Phone.Pump();

        Assert.NotEmpty(window.Showing(Resources.Status_Connected));
        Assert.Contains(window.Descendants<Ellipse>(), dot => dot.IsEffectivelyVisible);

        window.Close();
        return Task.CompletedTask;
    });

    /// <summary>An unpaired device has no computer to be connected to, so the chip is not merely reading
    /// Offline — it is not there.</summary>
    [Fact]
    public Task TheUnpairedShell_ShowsNoChipAtAll() => RunUi(() =>
    {
        var shell = new MainViewModel(
            new FakeIdentityStore(paired: false), ScreenRegistry.Connected(), ScreenRegistry.Pages());
        var window = Phone.Show(new MainView { DataContext = shell });

        Assert.Single(window.Descendants<PairingView>());
        Assert.Empty(window.Showing(Resources.Status_Offline));
        Assert.Empty(window.Showing(Resources.Status_Connected));
        Assert.DoesNotContain(window.Descendants<Ellipse>(), dot => dot.IsEffectivelyVisible);

        window.Close();
        return Task.CompletedTask;
    });

    /// <summary>A refused camera has a cure, so the screen says what it is and offers the way there.</summary>
    [Fact]
    public async Task ARefusedCamera_PutsTheReasonAndTheWayBackOnScreen()
    {
        await RunUi(async () =>
        {
            var scanner = new FakeBarcodeScanner { NextScan = BarcodeScan.PermissionDenied };
            var page = new IsbnScanViewModel(
                scanner, ScreenRegistry.Connected(), new FakeStagingStore(), new FakeDeviceSettings(), _ => { });
            var window = Phone.Show(new IsbnScanView { DataContext = page });

            Assert.Empty(window.Showing(Resources.Camera_PermissionDenied));

            await page.ScanCommand.ExecuteAsync(null);
            Phone.Pump();

            Assert.NotEmpty(window.Showing(Resources.Camera_PermissionDenied));
            Assert.Contains(window.Descendants<Button>(),
                button => button.IsEffectivelyVisible && Equals(button.Content, Resources.Camera_OpenSettings));

            window.Close();
        });
    }

    /// <summary>No camera at all has no cure, so the same notice explains it without a button that would do
    /// nothing when tapped.</summary>
    [Fact]
    public async Task NoCameraAtAll_IsExplainedWithoutASettingsButton()
    {
        await RunUi(async () =>
        {
            var scanner = new FakeBarcodeScanner { NextScan = BarcodeScan.CameraUnavailable };
            var page = new IsbnScanViewModel(
                scanner, ScreenRegistry.Connected(), new FakeStagingStore(), new FakeDeviceSettings(), _ => { });
            var window = Phone.Show(new IsbnScanView { DataContext = page });

            await page.ScanCommand.ExecuteAsync(null);
            Phone.Pump();

            Assert.NotEmpty(window.Showing(Resources.Camera_Unavailable));
            Assert.DoesNotContain(window.Descendants<Button>(),
                button => button.IsEffectivelyVisible && Equals(button.Content, Resources.Camera_OpenSettings));

            window.Close();
        });
    }

    /// <summary>The empty batch says so rather than showing an empty list, and the tray's own Upload is not
    /// offered when there is nothing to send.</summary>
    [Fact]
    public Task AnEmptyBatch_SaysSoInsteadOfShowingNothing() => RunUi(() =>
    {
        var page = new BatchTrayViewModel(
            new FakeStagingStore(), ScreenRegistry.Connected(), new FakeNavigator(),
            _ => new HubViewModel(new FakeNavigator(), new FakeStagingStore()), () => { });
        var window = Phone.Show(new BatchTrayView { DataContext = page });

        Assert.NotEmpty(window.Showing(Resources.Batch_Empty));
        Assert.DoesNotContain(window.Descendants<Button>(),
            button => button.IsEffectivelyVisible && ReferenceEquals(button.Command, page.UploadCommand));

        window.Close();
        return Task.CompletedTask;
    });

    /// <summary>
    /// A thumb near the edge of the page has to scroll it. A scrolling area only answers a drag that lands
    /// inside it, so a page that insets its whole grid leaves a band down each side where the touch reaches
    /// the page instead of the scroller and nothing moves — found on the handset, where the screens
    /// "scrolled but not if you touch the white background". The inset belongs inside the scroller, as its
    /// padding, and that is what this pins: the scrolling area is as wide as the page it is on.
    /// <para>
    /// Measured against the page rather than the window on purpose: the shell caps a page at a comfortable
    /// column and centres it, so on a wide screen there is space beside the page that is not part of it.
    /// </para>
    /// </summary>
    [Fact]
    public Task EveryScrollingScreen_ScrollsFromTheEdgesOfThePage() => RunUi(async () =>
    {
        var inset = new List<string>();

        foreach (var screen in ScreenRegistry.All)
        {
            var window = Phone.Show(await screen.BuildAsync(), Phone.Landscape);

            foreach (var scroller in window.Descendants<ScrollViewer>())
            {
                // A TextBox brings its own scroller, which is a control and not the page's scrolling area.
                if (scroller.GetVisualAncestors().OfType<TextBox>().Any())
                    continue;

                var page = scroller.GetVisualAncestors().OfType<UserControl>().First();
                var left = ((Visual)scroller).TranslatePoint(default, page)!.Value.X;
                if (left > 0 || left + scroller.Bounds.Width < page.Bounds.Width)
                {
                    inset.Add($"{screen.Name}/{page.GetType().Name} (spans " +
                              $"{left:F0}–{left + scroller.Bounds.Width:F0} of {page.Bounds.Width:F0})");
                }
            }

            window.Close();
        }

        Assert.True(inset.Count == 0,
            "Screens with a dead band beside their scrolling area, where a drag reaches the page and nothing "
            + "moves: " + string.Join(", ", inset.Distinct()));
    });

    /// <summary>
    /// A result row is the whole width of the page. The scrolling area reaching the edges (above) is about
    /// where a drag lands; this is about the row itself — its tap target and its press highlight. With the
    /// inset on the scroller the rows stopped short of the screen on both sides, which is what "no list
    /// actually extends to the full width" meant in UAT (5b/5c-bis). The inset moved onto the row's own
    /// padding, so the text sits exactly where it did and the row now reaches both edges.
    /// <para>Measured against the page, not the window: the shell centres a capped column on a wide screen.</para>
    /// </summary>
    [Fact]
    public Task ABrowseResultRow_IsAsWideAsThePage() => RunUi(async () =>
    {
        var window = Phone.Show(await ScreenRegistry.ByName("Browse").BuildAsync(), Phone.Portrait);
        var page = window.Descendants<BrowseView>().Single();

        var rows = page.Descendants<Button>()
            .Where(b => b.DataContext is BrowseRowViewModel)
            .ToList();
        Assert.NotEmpty(rows);

        foreach (var row in rows)
        {
            var left = ((Visual)row).TranslatePoint(default, page)!.Value.X;
            Assert.Equal(0, left, 0);
            Assert.Equal(page.Bounds.Width, left + row.Bounds.Width, 0);
        }

        window.Close();
    });
}
