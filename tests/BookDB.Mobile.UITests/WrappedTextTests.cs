using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using BookDB.Contracts;
using BookDB.Mobile.Localization;
using BookDB.Mobile.Services;
using BookDB.Mobile.Tests;
using BookDB.Mobile.ViewModels;
using BookDB.Mobile.Views;
using Xunit;
using System;

namespace BookDB.Mobile.UITests;

/// <summary>
/// Wrapped text has to stay inside the window. A screen that scrolls measures its content with the height
/// unbounded, and text told to wrap can come back a single line wide enough to run off the edge — the reader
/// then loses the end of the sentence with nothing on screen to say so. Found in UAT on a real handset: the
/// pairing screen's intro wrapped correctly until a status message appeared and forced a second layout pass,
/// after which both it and the status were cut off mid-sentence.
/// </summary>
public class WrappedTextTests : HeadlessTest
{
    private static string ValidCode() => new PairingPayload
    {
        Endpoint = "192.168.1.20:7443",
        ServerThumbprints = ["6E:34:0B:9C:FF:B3:7A:98"],
        ClientPfxBase64 = "MIIDdTCCAl2gAwIBAgIJAKm",
        IssuedAtUtc = DateTimeOffset.UtcNow,
    }.ToQrString();

    /// <summary>The handset UAT ran on: 411 dp across, with the system font size at 1.15.</summary>
    private static readonly Size Handset = new(411, 916);

    [Fact]
    public Task ThePairingScreensText_StaysInsideTheWindow_OnceAStatusMessageAppears() => RunUi(async () =>
    {
        var previous = System.Globalization.CultureInfo.DefaultThreadCurrentUICulture;
        System.Globalization.CultureInfo.DefaultThreadCurrentUICulture =
            System.Globalization.CultureInfo.GetCultureInfo("sv");
        try
        {
        var vm = new PairingViewModel(
            new FakeBarcodeScanner { Next = ValidCode() },
            new FakePairingCoordinator(),
            new FakeDeviceSettings(),
            new FakeNavigator());

        var window = Phone.Show(new PairingView { DataContext = vm, FontSize = 15 * 1.15 }, Handset);

        // The second layout pass is the one that broke it on the device: the status message appears, the
        // panel re-measures, and the wrapped blocks come back a single line.
        await vm.ScanCommand.ExecuteAsync(null);
        Phone.Pump();
        window.Measure(Handset);
        window.Arrange(new Rect(Handset));
        Phone.Pump();

        foreach (var expected in new[] { Resources.Pairing_Intro, Resources.Pairing_Scanned })
        {
            var block = window.Showing(expected).SingleOrDefault();
            Assert.True(block is not null, $"the screen is not showing: {expected}");

            // The box has to be as tall as the wrapped text needs. Shorter means whole lines are painted
            // outside it and the reader loses the end of the sentence with nothing on screen to say so.
            Assert.True(block!.Bounds.Height >= block.DesiredSize.Height,
                $"the text is {block.DesiredSize.Height - block.Bounds.Height:F0} px taller than the box it "
                + $"was given, so its last line falls outside: {expected}");

            var right = block.TranslatePoint(default, window)!.Value.X + block.Bounds.Width;
            Assert.True(right <= Handset.Width,
                $"text runs {right - Handset.Width:F0} px off the right edge: {expected}");
        }

        window.Close();
        }
        finally
        {
            System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = previous;
        }
    });
}
