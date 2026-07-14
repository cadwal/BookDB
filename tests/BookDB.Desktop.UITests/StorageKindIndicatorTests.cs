using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Svg.Skia;
using Avalonia.VisualTree;
using BookDB.Desktop.Localization;
using BookDB.Desktop.Services;
using BookDB.Desktop.ViewModels;
using BookDB.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BookDB.Desktop.UITests;

/// <summary>
/// Pins the status-bar storage-kind indicator: the local-file vs remote-server glyph is asserted by
/// resource identity (Icons.axaml key), and the hover popup renders the connection facts — backend
/// name, file path or host/database/user, and connection state — from resources, never credentials.
/// </summary>
public class StorageKindIndicatorTests : HeadlessTest
{
    [Fact]
    public Task SqliteBackend_ShowsTheLocalIcon_AndThePopupNamesTheDatabaseFile() => RunUi(async () =>
    {
        using var host = TestHost.Create();
        var window = await ShowMainWindow(host);
        var appSettings = host.Resolve<AppSettings>();

        var indicator = window.Find<Border>("StorageKindIndicator");
        AssertVisibleIcon(window, indicator, "Icon.StorageLocal");

        var texts = OpenPopupTexts(indicator);
        Assert.Contains(Resources.Settings_Database_Backend_Sqlite, texts);
        Assert.Contains(Resources.StatusBar_Storage_File_Label, texts);
        Assert.Contains(appSettings.SqliteLibraryPath!, texts);

        // Local storage has no connection to describe — the remote-only rows must not render.
        Assert.DoesNotContain(Resources.StatusBar_Storage_State_Label, texts);
        Assert.DoesNotContain(Resources.StatusBar_Storage_User_Label, texts);

        window.Close();
    });

    [Theory]
    [InlineData(DatabaseBackend.PostgreSql)]
    [InlineData(DatabaseBackend.MySql)]
    public Task RemoteBackend_ShowsTheRemoteIcon_AndThePopupDescribesTheConnection(DatabaseBackend backend) =>
        RunUi(async () =>
        {
            using var host = TestHost.Create(s =>
                s.AddSingleton(new AppSettings { Backend = backend }));
            host.Resolve<IBootstrapConfigService>().Update(config =>
            {
                if (backend == DatabaseBackend.PostgreSql)
                {
                    config.Postgres.Host = "books.example.org";
                    config.Postgres.Port = 5433;
                    config.Postgres.Database = "library";
                    config.Postgres.Username = "reader";
                }
                else
                {
                    config.MySql.Host = "books.example.org";
                    config.MySql.Port = 5433;
                    config.MySql.Database = "library";
                    config.MySql.Username = "reader";
                }
            });
            var window = await ShowMainWindow(host);

            var indicator = window.Find<Border>("StorageKindIndicator");
            AssertVisibleIcon(window, indicator, "Icon.StorageRemote");

            var expectedBackendName = backend == DatabaseBackend.PostgreSql
                ? Resources.Settings_Database_Backend_Postgres
                : Resources.Settings_Database_Backend_MySql;
            var texts = OpenPopupTexts(indicator);
            Assert.Contains(expectedBackendName, texts);
            Assert.Contains(Resources.StatusBar_Storage_Host_Label, texts);
            Assert.Contains("books.example.org:5433", texts);
            Assert.Contains(Resources.StatusBar_Storage_Database_Label, texts);
            Assert.Contains("library", texts);
            Assert.Contains(Resources.StatusBar_Storage_User_Label, texts);
            Assert.Contains("reader", texts);
            Assert.Contains(Resources.StatusBar_Storage_State_Label, texts);
            Assert.Contains(Resources.StatusBar_Storage_State_Connected, texts);

            window.Close();
        });

    [Fact]
    public Task StorageIconResources_ResolveTheirThemeCss() => RunUi(async () =>
    {
        using var host = TestHost.Create();
        var window = await ShowMainWindow(host);

        foreach (var key in new[] { "Icon.StorageLocal", "Icon.StorageRemote" })
        {
            var svg = Assert.IsType<SvgImage>(window.FindResource(key));
            Assert.False(string.IsNullOrEmpty(svg.Css), $"{key} did not resolve its IconCss.");
        }

        window.Close();
    });

    private static async Task<Window> ShowMainWindow(TestHost host)
    {
        var window = (Window)await SurfaceRegistry.ByName("Main").BuildAsync(host);
        window.Show();
        Ui.Pump();
        return window;
    }

    /// <summary>Exactly one of the two storage glyphs may be visible, and it must be the named resource.</summary>
    private static void AssertVisibleIcon(Window window, Border indicator, string resourceKey)
    {
        var visible = Assert.Single(indicator.Descendants<Image>(), i => i.IsEffectivelyVisible);
        Assert.Same(window.FindResource(resourceKey), visible.Source);
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
