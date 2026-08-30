using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Svg.Skia;
using BookDB.Desktop.Localization;
using BookDB.Desktop.ViewModels;
using BookDB.Desktop.Views;
using Xunit;

namespace BookDB.Desktop.UITests;

/// <summary>
/// The shared, non-blocking ISBN check-digit indicator: a valid full-length ISBN shows the success
/// glyph, a failed check digit shows the warning glyph with a localized tooltip, and empty or partial
/// input shows nothing. It is purely advisory — the lookup stays enabled for a mistyped ISBN. The
/// multi-line lookup wizard shows the same judgement as an aggregate count instead of a per-line glyph.
/// </summary>
public class IsbnChecksumIndicatorTests : HeadlessTest
{
    [Fact]
    public Task GuidedIsbnField_ShowsValidWarningOrNothing_AndNeverBlocksLookup() => RunUi(() =>
    {
        using var host = TestHost.Create();
        var vm = host.Resolve<AddBookIdentifyViewModel>();
        var dialog = new AddBookIdentifyDialog { DataContext = vm };
        dialog.Show();
        Ui.Pump();

        var indicator = dialog.Find<Image>("IsbnChecksumIndicator");

        // Empty: nothing shown.
        Assert.False(indicator.IsVisible);

        // Valid check digit: success glyph + valid tooltip.
        vm.IsbnText = "9780306406157";
        Ui.Pump();
        Assert.True(indicator.IsVisible);
        Assert.Same(dialog.FindResource("Icon.IsbnValid"), indicator.Source);
        Assert.Equal(Resources.Isbn_ChecksumValid, ToolTip.GetTip(indicator));

        // Failed check digit: warning glyph + warning tooltip — and the lookup stays enabled.
        vm.IsbnText = "9780306406158";
        Ui.Pump();
        Assert.True(indicator.IsVisible);
        Assert.Same(dialog.FindResource("Icon.IsbnWarning"), indicator.Source);
        Assert.Equal(Resources.Isbn_ChecksumWarning, ToolTip.GetTip(indicator));
        Assert.True(vm.LookUpCommand.CanExecute(null), "A mistyped ISBN must not disable the lookup.");

        // A wrong length can't be a valid ISBN either — it warns rather than showing nothing.
        vm.IsbnText = "978030640";
        Ui.Pump();
        Assert.True(indicator.IsVisible);
        Assert.Same(dialog.FindResource("Icon.IsbnWarning"), indicator.Source);

        // Cleared back to empty: hidden again.
        vm.IsbnText = "";
        Ui.Pump();
        Assert.False(indicator.IsVisible);

        dialog.Close();
        return Task.CompletedTask;
    });

    [Fact]
    public Task LookupWizard_AggregatesFailedCheckDigits_AcrossTheTypedLines() => RunUi(() =>
    {
        using var host = TestHost.Create();
        var vm = host.Resolve<LookupWizardViewModel>();

        Assert.False(vm.HasChecksumWarnings);

        vm.IsbnText = "9780306406157\n9780306406158\n978030640615A";
        Assert.Equal(2, vm.InvalidChecksumCount);
        Assert.True(vm.HasChecksumWarnings);

        // All-valid input clears the aggregate warning.
        vm.IsbnText = "9780306406157\n0306406152";
        Assert.Equal(0, vm.InvalidChecksumCount);
        Assert.False(vm.HasChecksumWarnings);

        return Task.CompletedTask;
    });

    [Fact]
    public Task IsbnIndicatorResources_ResolveTheirThemeCss() => RunUi(async () =>
    {
        using var host = TestHost.Create();
        var window = (Window)await SurfaceRegistry.ByName("Main").BuildAsync(host);
        window.Show();
        Ui.Pump();

        foreach (var key in new[] { "Icon.IsbnValid", "Icon.IsbnWarning" })
        {
            var svg = Assert.IsType<SvgImage>(window.FindResource(key));
            Assert.False(string.IsNullOrEmpty(svg.Css), $"{key} did not resolve its IconCss.");
        }

        window.Close();
    });
}
