using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using BookDB.Desktop.Services;
using BookDB.Desktop.ViewModels;
using BookDB.Desktop.Views;
using BookDB.Help;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace BookDB.Desktop.UITests;

/// <summary>
/// Contextual help on the two remote-database surfaces (Settings ▸ Database, Maintenance ▸ Move library):
/// each carries a hyperlink-styled button that opens the Help window on the Remote Databases tab. Asserted
/// through a real headless click so hit-testing is covered, not just the command wiring.
/// </summary>
public class HelpLinkTests : HeadlessTest
{
    [Fact]
    public Task DatabaseSettingsTab_HelpLink_OpensHelpAtRemoteDatabases() => RunUi(() =>
    {
        var windowService = Substitute.For<IWindowService>();
        using var host = TestHost.Create(s => s.AddSingleton(windowService));

        var vm = host.Resolve<SettingsWindowViewModel>().DatabaseTab;
        var view = new DatabaseSettingsView { DataContext = vm };
        var window = view.Host();

        ClickLink(window, view.ButtonFor(vm.OpenRemoteDatabasesHelpCommand));

        windowService.Received(1).OpenHelpWindow(HelpTab.RemoteDatabases);
        window.Close();
        return Task.CompletedTask;
    });

    [Fact]
    public Task MoveLibraryPane_HelpLink_OpensHelpAtRemoteDatabases_EvenDuringAMove() => RunUi(() =>
    {
        var windowService = Substitute.For<IWindowService>();
        using var host = TestHost.Create(s => s.AddSingleton(windowService));

        var vm = host.Resolve<MoveLibraryViewModel>();
        var view = new MoveLibraryView { DataContext = vm };
        var window = view.Host();

        // The link lives outside the form's CanInteract gate: help stays reachable while a move runs.
        vm.IsRunning = true;
        Ui.Pump();
        var link = view.ButtonFor(vm.OpenRemoteDatabasesHelpCommand);
        Assert.True(link.IsEffectivelyEnabled);

        ClickLink(window, link);

        windowService.Received(1).OpenHelpWindow(HelpTab.RemoteDatabases);
        window.Close();
        return Task.CompletedTask;
    });

    /// <summary>Real click at the button's centre — proves the link is visible, enabled and hit-testable.</summary>
    private static void ClickLink(Window window, Button link)
    {
        Assert.True(link.IsVisible);
        Assert.True(link.IsEffectivelyEnabled);
        Assert.True(link.Focusable); // keyboard-reachable via tab order

        var center = link.TranslatePoint(new Point(link.Bounds.Width / 2, link.Bounds.Height / 2), window)!.Value;
        window.MouseDown(center, MouseButton.Left);
        window.MouseUp(center, MouseButton.Left);
        Ui.Pump();
    }
}
