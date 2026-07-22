using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using BookDB.Desktop.Localization;
using BookDB.Desktop.ViewModels;
using BookDB.Desktop.Views;
using BookDB.Logic.Messages;
using CommunityToolkit.Mvvm.Messaging;
using Xunit;

namespace BookDB.Desktop.UITests;

/// <summary>
/// The main window's batch section in the status bar: while a batch runs it renders the
/// progress-plus-outcome text (composed from the status-bar resources), and it disappears
/// once the batch completes without the window being minimized.
/// </summary>
public class BatchStatusBarTests : HeadlessTest
{
    [Fact]
    public async Task StatusBar_RendersProgressWithOutcomeWhileRunning_AndHidesOnCompletion()
    {
        await RunUi(async () =>
        {
            using var host = TestHost.Create();
            var main = host.Resolve<MainWindow>();
            await ((MainWindowViewModel)main.DataContext!).InitializeAsync();
            main.Show();
            Ui.Pump();

            var messenger = host.Resolve<IMessenger>();
            messenger.Send(new BatchQueueProgressMessage
            {
                Current = 3,
                Total = 10,
                IsRunning = true,
                ToReviewCount = 2,
                FailedCount = 1
            });
            Ui.Pump();

            var expected = string.Format(
                Resources.StatusBar_BatchQueue_WithOutcome,
                string.Format(Resources.StatusBar_BatchQueue_Progress, 3, 10),
                string.Format(Resources.StatusBar_BatchQueue_OutcomePair,
                    string.Format(Resources.StatusBar_BatchQueue_ToReview, 2),
                    string.Format(Resources.StatusBar_BatchQueue_Failed, 1)));
            var statusText = main.Descendants<TextBlock>().Single(t => t.Text == expected);
            Assert.True(statusText.IsEffectivelyVisible);

            // Batch completes without the batch window having been minimized — the section hides.
            messenger.Send(new BatchQueueProgressMessage
            {
                Current = 10,
                Total = 10,
                IsRunning = false,
                ToReviewCount = 2,
                FailedCount = 1
            });
            Ui.Pump();

            Assert.False(statusText.IsEffectivelyVisible);

            main.Close();
        });
    }
}
