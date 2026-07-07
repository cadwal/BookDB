using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Xunit;

namespace BookDB.Desktop.UITests;

/// <summary>
/// Every window and pane builds from a fresh host, shows headless, and loads without an exception or a binding
/// error (the gate in <see cref="HeadlessTest.RunUi"/>). Broad, cheap coverage that catches the blank-window /
/// binding-typo / missing-resource class manual UAT misses.
/// </summary>
public class SmokeTests : HeadlessTest
{
    public static IEnumerable<object[]> WindowNames => SurfaceRegistry.Windows.Select(s => new object[] { s.Name });
    public static IEnumerable<object[]> PaneNames => SurfaceRegistry.Panes.Select(s => new object[] { s.Name });
    public static IEnumerable<object[]> DialogNames => SurfaceRegistry.Dialogs.Select(s => new object[] { s.Name });

    [Theory]
    [MemberData(nameof(WindowNames))]
    public Task Window_LoadsHeadless_WithoutBindingErrors(string name) => Smoke(name);

    [Theory]
    [MemberData(nameof(PaneNames))]
    public Task Pane_RendersHeadless_WithoutBindingErrors(string name) => Smoke(name);

    [Theory]
    [MemberData(nameof(DialogNames))]
    public Task Dialog_RendersHeadless_WithoutBindingErrors(string name) => Smoke(name);

    private static Task Smoke(string name) => RunUi(async () =>
    {
        using var host = TestHost.Create();
        var content = await SurfaceRegistry.ByName(name).BuildAsync(host);

        var window = content as Window ?? new Window { Content = content, Width = 1000, Height = 700 };
        window.Show();
        Ui.Pump();

        Assert.True(window.IsVisible);
        window.Close();
    });
}
