using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using BookDB.Desktop.Services;
using BookDB.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BookDB.Desktop.UITests;

/// <summary>
/// Drives a real pointer hover over the storage-kind indicator (unlike StorageKindIndicatorTests, which
/// forces the tooltip open) so the input pipeline — hit-test, tooltip arming, show timer — stays covered,
/// and pins the control-anchored placement: pointer placement landed the popup under the cursor on scaled
/// Wayland, closing it instantly in an open/close flicker loop (found in v2.3 UAT; headless cannot observe
/// the flicker itself, only the placement contract).
/// </summary>
public class StorageKindHoverTests : HeadlessTest
{
    [Fact]
    public Task Sqlite_HoverOpensThePopup() => RunUi(() => HoverProbe(null));

    [Fact]
    public Task Postgres_HoverOpensThePopup() => RunUi(() => HoverProbe(DatabaseBackend.PostgreSql));

    private async Task HoverProbe(DatabaseBackend? remoteBackend)
    {
        using var host = remoteBackend is null
            ? TestHost.Create()
            : TestHost.Create(s => s.AddSingleton(new AppSettings { Backend = remoteBackend.Value }));
        if (remoteBackend == DatabaseBackend.PostgreSql)
        {
            host.Resolve<IBootstrapConfigService>().Update(config =>
            {
                config.Postgres.Host = "books.example.org";
                config.Postgres.Port = 5433;
                config.Postgres.Database = "library";
                config.Postgres.Username = "reader";
            });
        }

        var window = (Window)await SurfaceRegistry.ByName("Main").BuildAsync(host);
        window.Show();
        Ui.Pump();

        var indicator = window.Find<Border>("StorageKindIndicator");

        Assert.Equal(PlacementMode.Top, ToolTip.GetPlacement(indicator));
        Assert.Equal(0, ToolTip.GetVerticalOffset(indicator));

        var center = indicator.TranslatePoint(
            new Point(indicator.Bounds.Width / 2, indicator.Bounds.Height / 2), window)!.Value;

        window.MouseMove(center);
        Ui.Pump();

        await Ui.PumpUntil(() => ToolTip.GetIsOpen(indicator), System.Threading.CancellationToken.None);

        window.Close();
    }
}
