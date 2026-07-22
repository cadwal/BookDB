using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using BookDB.Desktop.Localization;
using BookDB.Desktop.Services;
using BookDB.Desktop.ViewModels;
using BookDB.Desktop.Views;
using BookDB.Help;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace BookDB.Desktop.UITests;

/// <summary>
/// Help window wiring: opening at any entry point lands on that tab, every tab carries loaded markdown
/// content, and walking all of them realizes each page under the binding gate.
/// </summary>
public class HelpWindowFlowTests : HeadlessTest
{
    [Fact]
    public async Task OpensAtTheRequestedTab_AndAllTabsRealizeWithContent()
    {
        await RunUi(async () =>
        {
            using var host = TestHost.Create();

            // Each entry point (menu items, in-app help links) maps to its own tab.
            foreach (var tab in Enum.GetValues<HelpTab>())
            {
                var probe = host.Resolve<HelpWindowViewModel>();
                await probe.InitializeAsync(tab);
                Assert.Equal((int)tab, probe.SelectedTabIndex);
            }

            var vm = host.Resolve<HelpWindowViewModel>();
            await vm.InitializeAsync(HelpTab.ImportGuide);
            var window = new HelpWindow { DataContext = vm };
            window.Show();
            Ui.Pump();

            var tabs = window.Find<TabControl>();
            Assert.Equal(Enum.GetValues<HelpTab>().Length, tabs.Items.Count);
            Assert.Equal((int)HelpTab.ImportGuide, tabs.SelectedIndex);

            // Every page loaded real markdown, and each tab realizes cleanly when selected.
            Assert.All(
                new[]
                {
                    vm.GettingStartedContent, vm.ShortcutsContent, vm.GlossaryContent,
                    vm.ImportGuideContent, vm.DataSourcesContent, vm.RemoteDatabasesContent,
                },
                content => Assert.False(string.IsNullOrWhiteSpace(content)));
            foreach (var index in Enumerable.Range(0, tabs.Items.Count))
            {
                vm.SelectedTabIndex = index;
                Ui.Pump();
                Assert.Equal(index, tabs.SelectedIndex);
            }
            window.Close();
        });
    }

    [Fact]
    public async Task HelpMenuItem_OpensTheRemoteDatabasesTab()
    {
        var windowService = Substitute.For<IWindowService>();

        await RunUi(async () =>
        {
            using var host = TestHost.Create(s => s.AddSingleton(windowService));
            var window = (Window)await SurfaceRegistry.ByName("Main").BuildAsync(host);
            window.Show();
            Ui.Pump();

            var item = MenuLeafItems(window.Find<Menu>())
                .Single(i => Equals(i.Header, Resources.Menu_Help_RemoteDatabases));
            item.Command!.Execute(null);

            windowService.Received(1).OpenHelpWindow(HelpTab.RemoteDatabases);
            window.Close();
        });
    }

    private static IEnumerable<MenuItem> MenuLeafItems(Menu menu)
    {
        static IEnumerable<MenuItem> Walk(MenuItem item)
        {
            var children = item.Items.OfType<MenuItem>().ToList();
            if (children.Count == 0)
            {
                yield return item;
                yield break;
            }
            foreach (var child in children)
                foreach (var leaf in Walk(child))
                    yield return leaf;
        }

        return menu.Items.OfType<MenuItem>().SelectMany(Walk);
    }
}
