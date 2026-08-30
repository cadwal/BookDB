using System.Threading.Tasks;
using Avalonia.Controls;
using BookDB.Desktop.ViewModels;
using BookDB.Desktop.Views;
using Xunit;

namespace BookDB.Desktop.UITests;

/// <summary>
/// The Companion tab's firewall hint, raised in UAT as two faults in one control. On Linux the hint's whole
/// job is to carry <c>sudo ufw allow …</c> lines the user pastes into a terminal, and a plain
/// <c>TextBlock</c> cannot be selected — the commands were readable and uncopyable, which is a picture of a
/// command rather than a command. Widening it to both columns is the other half: at the width of the value
/// column alone the commands wrapped, and the extra lines pushed the tab past the window into a scrollbar.
/// </summary>
public class CompanionFirewallHintTests : HeadlessTest
{
    private const int CompanionTabIndex = 7;

    [Fact]
    public Task TheFirewallHint_CanBeSelected() => RunUi(async () =>
    {
        var window = await ShowCompanionTab();

        Assert.IsAssignableFrom<SelectableTextBlock>(window.Find<Control>("CompanionFirewallHint"));

        window.Close();
    });

    /// <summary>
    /// Guards the mechanism that keeps the hint short rather than a pixel height, which would only measure
    /// this machine's fonts: the commands are as long as they are, so the room for them is the full width.
    /// </summary>
    [Fact]
    public Task TheFirewallHint_UsesTheFullWidthOfTheTab() => RunUi(async () =>
    {
        var window = await ShowCompanionTab();
        var hint = window.Find<Control>("CompanionFirewallHint");

        Assert.Equal(0, Grid.GetColumn(hint));
        Assert.Equal(2, Grid.GetColumnSpan(hint));

        window.Close();
    });

    private static async Task<Window> ShowCompanionTab()
    {
        using var host = TestHost.Create();
        var vm = host.Resolve<SettingsWindowViewModel>();
        await vm.InitializeAsync();
        var window = new SettingsWindow { DataContext = vm };
        window.Show();
        vm.SelectedTabIndex = CompanionTabIndex;
        Ui.Pump();
        return window;
    }
}
