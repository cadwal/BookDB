using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;
using Xunit;

namespace BookDB.Desktop.UITests;

/// <summary>
/// How the pairing code reaches the camera. The renderer's own tests cover the bitmap; this covers the one
/// thing that can undo it on the way to the screen.
/// </summary>
public class PairingCodeDisplayTests : HeadlessTest
{
    /// <summary>
    /// The code is shown at exactly the size it was drawn. The renderer picks a whole number of pixels per
    /// module, and resampling blurs one module into the next — at ~150 modules for a real payload that is the
    /// difference between a read and a camera that stares at it. UAT found the code only became reliable once
    /// it was displayed at its own size; a fixed Width would hold only until the payload changed length.
    /// </summary>
    [Fact]
    public Task ThePairingCode_IsShownAtItsOwnSizeRatherThanResampled() => RunUi(async () =>
    {
        using var host = TestHost.Create();
        var dialog = (Window)await SurfaceRegistry.ByName("PairingDialog").BuildAsync(host);
        dialog.Show();
        Ui.Pump();

        // The code is the image on the white plate — the plate is there so a scanner has the contrast.
        var code = Assert.Single(
            dialog.GetVisualDescendants().OfType<Image>(),
            image => image.GetVisualAncestors().OfType<Border>()
                .Any(border => Equals(border.Background, Brushes.White)));

        Assert.Equal(Stretch.None, code.Stretch);
        Assert.True(double.IsNaN(code.Width) && double.IsNaN(code.Height),
            "the code is pinned to a size of its own, so a payload of a different length gets resampled");

        dialog.Close();
    });
}
