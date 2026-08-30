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
/// Contextual help links: the two remote-database surfaces (Settings ▸ Database, Maintenance ▸ Move library)
/// open the Help window on the Remote Databases tab, and the two companion surfaces (Settings ▸ Companion,
/// Maintenance ▸ Devices) open it on the Companion tab. Asserted through a real headless click so hit-testing
/// is covered, not just the command wiring.
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

    [Fact]
    public async Task CompanionSettingsTab_HelpLink_OpensHelpAtCompanion()
    {
        var windowService = Substitute.For<IWindowService>();

        await RunUi(async () =>
        {
            using var host = TestHost.Create(s => s.AddSingleton(windowService));
            var vm = host.Resolve<SettingsWindowViewModel>();
            await vm.InitializeAsync();
            var window = new SettingsWindow { DataContext = vm };
            window.Show();
            vm.SelectedTabIndex = 7; // Companion
            Ui.Pump();

            ClickLink(window, window.ButtonFor(vm.CompanionTab.OpenCompanionHelpCommand));

            windowService.Received(1).OpenHelpWindow(HelpTab.Companion);
            window.Close();
        });
    }

    [Fact]
    public Task DevicesPane_HelpLink_OpensHelpAtCompanion() => RunUi(() =>
    {
        var windowService = Substitute.For<IWindowService>();
        using var host = TestHost.Create(s => s.AddSingleton(windowService));

        var vm = host.Resolve<DevicesViewModel>();
        var view = new DevicesView { DataContext = vm };
        var window = view.Host();

        ClickLink(window, view.ButtonFor(vm.OpenCompanionHelpCommand));

        windowService.Received(1).OpenHelpWindow(HelpTab.Companion);
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
