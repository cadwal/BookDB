using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using BookDB.Mobile.Services;
using BookDB.Mobile.Tests;
using BookDB.Mobile.ViewModels;
using BookDB.Mobile.Views;
using Xunit;

namespace BookDB.Mobile.UITests;

/// <summary>
/// The phone runs on tablets too, so no screen may be a stretched phone screen and none may only fit upright.
/// These are the two things a layout can get wrong that no view-model test can see.
/// </summary>
public class TabletLayoutTests : HeadlessTest
{
    /// <summary>On a tablet the page keeps a readable column instead of running the full width, and it sits in
    /// the middle of the window rather than against one edge.</summary>
    [Fact]
    public Task OnATablet_ThePageIsAColumnInTheMiddle_NotTheFullWidth() => RunUi(() =>
    {
        var shell = Shell();
        var window = Phone.Show(new MainView { DataContext = shell }, Phone.Tablet);

        var frame = window.PageFrame(shell);
        Assert.True(frame.Bounds.Width < Phone.Tablet.Width,
            $"the page filled the tablet's {Phone.Tablet.Width} px width ({frame.Bounds.Width})");

        var left = frame.TranslatePoint(default, window)!.Value.X;
        var right = Phone.Tablet.Width - (left + frame.Bounds.Width);
        Assert.True(System.Math.Abs(left - right) < 1,
            $"the page is not centred: {left} px on the left, {right} px on the right");

        window.Close();
        return Task.CompletedTask;
    });

    /// <summary>The same page on a handset uses what width there is — the cap is a maximum, not a fixed size.</summary>
    [Fact]
    public Task OnAHandset_ThePageUsesTheWholeWidth() => RunUi(() =>
    {
        var shell = Shell();
        var window = Phone.Show(new MainView { DataContext = shell }, Phone.Portrait);

        Assert.Equal(Phone.Portrait.Width, window.PageFrame(shell).Bounds.Width);

        window.Close();
        return Task.CompletedTask;
    });

    /// <summary>
    /// A handset on its side is wider than the tablet column, and it is still a handset. Capping it there
    /// leaves a strip down each edge that no thumb can scroll, with the scroll bar stranded in the middle of
    /// the glass — so the cap goes by the smaller side, not the width. Raised in UAT 5b, where the dead strip
    /// was 80 px on each edge of an 800 px screen.
    /// </summary>
    [Fact]
    public Task OnAHandsetTurnedOnItsSide_ThePageAndItsScrollerStillReachBothEdges() => RunUi(() =>
    {
        var shell = Shell();
        var window = Phone.Show(new MainView { DataContext = shell }, Phone.Landscape);

        var frame = window.PageFrame(shell);
        Assert.Equal(Phone.Landscape.Width, frame.Bounds.Width);

        // The scroller specifically: the page can span the screen while the part that answers a drag does not.
        var scroller = frame.Find<ScrollViewer>();
        Assert.Equal(0, scroller.TranslatePoint(default, window)!.Value.X);
        Assert.Equal(Phone.Landscape.Width, scroller.Bounds.Width);

        window.Close();
        return Task.CompletedTask;
    });

    /// <summary>
    /// Landscape is where a screen built to be centred runs out of height. On the scan step the big scan
    /// button is at the top and the way to type a number by hand is under it, so that is the control which
    /// must still be reachable — the whole point of the screen when the barcode is damaged.
    /// </summary>
    [Fact]
    public Task InLandscape_TypingTheNumberByHandIsStillReachable() => RunUi(() =>
    {
        var page = new IsbnScanViewModel(
            new FakeBarcodeScanner(), ScreenRegistry.Connected(), new FakeStagingStore(),
            new FakeDeviceSettings(), _ => { });
        var window = Phone.Show(new IsbnScanView { DataContext = page }, Phone.Landscape);

        var typeItIn = window.ButtonFor(page.UseManualIsbnCommand);
        window.ScrollToBottom(typeItIn);

        Assert.True(window.IsOnScreen(typeItIn),
            $"the way to type an ISBN is off the bottom of a {Phone.Landscape.Height} px landscape screen");

        window.Close();
        return Task.CompletedTask;
    });

    /// <summary>The hub's second job must not fall off the bottom either.</summary>
    [Fact]
    public Task InLandscape_BothOfTheHubsJobsAreReachable() => RunUi(() =>
    {
        var page = new HubViewModel(new FakeNavigator(), ScreenRegistry.Batch());
        var window = Phone.Show(new HubView { DataContext = page }, Phone.Landscape);

        var browse = window.ButtonFor(page.BrowseCommand);
        window.ScrollToBottom(browse);

        Assert.True(window.IsOnScreen(browse),
            $"Browse is off the bottom of a {Phone.Landscape.Height} px landscape screen");

        window.Close();
        return Task.CompletedTask;
    });

    private static MainViewModel Shell() =>
        new(new FakeIdentityStore(paired: true), ScreenRegistry.Connected(), ScreenRegistry.Pages());
}
