using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using BookDB.Desktop.Services;
using BookDB.Desktop.ViewModels;
using BookDB.Desktop.Views;
using Xunit;

namespace BookDB.Desktop.UITests;

/// <summary>
/// ProgressWindow: the IWindowService handle drives the status line (from worker threads) and closes the
/// window; the chromeless card variant renders decoration-less with the indeterminate bar.
/// </summary>
public class ProgressWindowTests : HeadlessTest
{
    [Fact]
    public async Task Handle_ReportsUpdateTheStatusLine_AndCloseClosesTheWindow()
    {
        await RunUi(async () =>
        {
            using var host = TestHost.Create();
            var (main, windowService) = await ShowMainWindowAsync(host);

            var handle = windowService.ShowProgressWindow("Smoke operation");
            Ui.Pump();
            var window = main.OwnedWindows.OfType<ProgressWindow>().Single();
            var viewModel = (ProgressWindowViewModel)window.DataContext!;
            Assert.False(viewModel.IsCard);
            Assert.Contains(window.Descendants<TextBlock>(), t => t.Text == viewModel.Status);

            // Reports arrive from worker threads in production (the operations run under Task.Run).
            await Task.Run(() => handle.Report("Copying covers"));
            Ui.Pump();
            Assert.Equal("Copying covers", viewModel.Status);
            Assert.Contains(window.Descendants<TextBlock>(), t => t.Text == "Copying covers");

            handle.Close();
            Ui.Pump();
            Assert.Empty(main.OwnedWindows.OfType<ProgressWindow>());
            main.Close();
        });
    }

    [Fact]
    public async Task CardVariant_RendersDecorationLess_WithIndeterminateBar()
    {
        await RunUi(() =>
        {
            var normal = new ProgressWindow { DataContext = new ProgressWindowViewModel("Header") };
            normal.Show();
            Ui.Pump();
            Assert.Equal(WindowDecorations.Full, normal.WindowDecorations);
            Assert.False(normal.Descendants<ProgressBar>().Single().IsVisible);
            normal.Close();

            var card = new ProgressWindow { DataContext = new ProgressWindowViewModel("Header", isCard: true) };
            card.Show();
            Ui.Pump();
            Assert.Equal(WindowDecorations.None, card.WindowDecorations);
            var bar = card.Descendants<ProgressBar>().Single();
            Assert.True(bar.IsVisible);
            Assert.True(bar.IsIndeterminate);
            Assert.Contains("card", card.FindControl<Border>("Root")!.Classes);
            card.Close();
            return Task.CompletedTask;
        });
    }

    /// <summary>Shows the app's real MainWindow so the WindowService-run window has its production owner,
    /// and returns it with the real IWindowService.</summary>
    private static async Task<(MainWindow Main, IWindowService WindowService)> ShowMainWindowAsync(TestHost host)
    {
        var main = host.Resolve<MainWindow>();
        await ((MainWindowViewModel)main.DataContext!).InitializeAsync();
        main.Show();
        Ui.Pump();
        return (main, host.Resolve<IWindowService>());
    }
}
