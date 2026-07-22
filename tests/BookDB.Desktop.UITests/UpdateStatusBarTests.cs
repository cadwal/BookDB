using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using BookDB.Desktop.Localization;
using BookDB.Desktop.Services.UpdateCheck;
using BookDB.Desktop.ViewModels;
using BookDB.Desktop.Views;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BookDB.Desktop.UITests;

/// <summary>
/// The status-bar "update available" pill: shown only when the weekly check reports a newer version,
/// hidden once the running version has caught up.
/// </summary>
public class UpdateStatusBarTests : HeadlessTest
{
    private sealed class FakeUpdateCheck(UpdateStatus status) : IUpdateCheckService
    {
        public Task<UpdateStatus> CheckAsync(CancellationToken ct = default) => Task.FromResult(status);
    }

    [Fact]
    public async Task Pill_IsShown_WhenAnUpdateIsAvailable()
    {
        await RunUi(async () =>
        {
            var status = new UpdateStatus(true, new UpdateVersion(3, 1, 0), new UpdateVersion(3, 2, 0), InstallChannel.GitHub);
            using var host = TestHost.Create(s => s.AddSingleton<IUpdateCheckService>(new FakeUpdateCheck(status)));
            var main = host.Resolve<MainWindow>();
            var vm = (MainWindowViewModel)main.DataContext!;
            await vm.InitializeAsync();
            main.Show();
            Ui.Pump();

            await vm.CheckForUpdatesCommand.ExecuteAsync(null);
            Ui.Pump();

            Assert.True(vm.ShowUpdateAvailable);
            var pill = main.Descendants<TextBlock>().Single(t => t.Text == Resources.StatusBar_UpdateAvailable);
            Assert.True(pill.IsEffectivelyVisible);
        });
    }

    [Fact]
    public async Task Pill_IsHidden_WhenUpToDate()
    {
        await RunUi(async () =>
        {
            var status = new UpdateStatus(false, new UpdateVersion(3, 2, 0), new UpdateVersion(3, 2, 0), InstallChannel.GitHub);
            using var host = TestHost.Create(s => s.AddSingleton<IUpdateCheckService>(new FakeUpdateCheck(status)));
            var main = host.Resolve<MainWindow>();
            var vm = (MainWindowViewModel)main.DataContext!;
            await vm.InitializeAsync();
            main.Show();
            Ui.Pump();

            await vm.CheckForUpdatesCommand.ExecuteAsync(null);
            Ui.Pump();

            Assert.False(vm.ShowUpdateAvailable);
            var pill = main.Descendants<TextBlock>().SingleOrDefault(t => t.Text == Resources.StatusBar_UpdateAvailable);
            Assert.True(pill is null || !pill.IsEffectivelyVisible);
        });
    }
}
