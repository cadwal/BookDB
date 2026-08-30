using System.Threading.Tasks;
using BookDB.Desktop.Services;
using BookDB.Desktop.ViewModels;
using BookDB.Desktop.Views;
using BookDB.Logic.Messages;
using BookDB.Logic.Services;
using BookDB.Models.Entities;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace BookDB.Desktop.UITests;

/// <summary>
/// A book sent from the phone is catalogued by a queue nobody is watching: there is no batch-queue window
/// open, because the run was not started from this computer. When such a run leaves an item to be reviewed,
/// the desktop has to bring the review up itself — the phone has already promised it — and it must not do so
/// for a batch the user is watching, whose window offers Start review of its own.
/// </summary>
public class CompanionReviewSurfacesTests : HeadlessTest
{
    [Fact]
    public async Task ARunThatEndsWithAReviewWaitingAndNoWindowWatching_BringsUpTheReview()
    {
        await RunUi(async () =>
        {
            var runner = Substitute.For<IBatchReviewRunner>();
            runner.ReviewItemAsync(Arg.Any<BatchQueueItem>(), Arg.Any<int?>())
                .Returns(Task.FromResult(BatchReviewOutcome.Skipped));

            using var host = TestHost.Create(s => s.AddSingleton(runner));
            var queue = host.Resolve<BatchQueueService>();
            var item = await queue.EnqueueAsync("9780441013593", bookId: null);
            await queue.UpdateStatusAsync(item.BatchQueueItemId, BatchStatus.PendingReview, "{}");

            var main = host.Resolve<MainWindow>();
            await ((MainWindowViewModel)main.DataContext!).InitializeAsync();
            main.Show();
            Ui.Pump();

            host.Resolve<IMessenger>().Send(new BatchQueueProgressMessage
            {
                Current = 1,
                Total = 1,
                IsRunning = false,
                ToReviewCount = 1,
            });
            Ui.Pump();

            await runner.Received(1).ReviewItemAsync(
                Arg.Is<BatchQueueItem>(i => i != null && i.BatchQueueItemId == item.BatchQueueItemId),
                Arg.Any<int?>());

            host.Resolve<IWindowService>().CloseAllSecondaryWindows();
            main.Close();
        });
    }

    [Fact]
    public async Task WithTheBatchWindowOpen_TheReviewIsLeftToTheUserToStart()
    {
        await RunUi(async () =>
        {
            var runner = Substitute.For<IBatchReviewRunner>();
            runner.ReviewItemAsync(Arg.Any<BatchQueueItem>(), Arg.Any<int?>())
                .Returns(Task.FromResult(BatchReviewOutcome.Skipped));

            using var host = TestHost.Create(s => s.AddSingleton(runner));
            var queue = host.Resolve<BatchQueueService>();
            var item = await queue.EnqueueAsync("9780441013593", bookId: null);
            await queue.UpdateStatusAsync(item.BatchQueueItemId, BatchStatus.PendingReview, "{}");

            var main = host.Resolve<MainWindow>();
            await ((MainWindowViewModel)main.DataContext!).InitializeAsync();
            main.Show();
            var windows = host.Resolve<IWindowService>();
            windows.OpenBatchQueueWindow();
            Ui.Pump();

            host.Resolve<IMessenger>().Send(new BatchQueueProgressMessage
            {
                Current = 1,
                Total = 1,
                IsRunning = false,
                ToReviewCount = 1,
            });
            Ui.Pump();

            await runner.DidNotReceive().ReviewItemAsync(Arg.Any<BatchQueueItem>(), Arg.Any<int?>());

            windows.CloseAllSecondaryWindows();
            main.Close();
        });
    }

    /// <summary>
    /// The other half of the same defect: opening the window between runs zeroed the counts and cleared the
    /// completion flag, so items waiting to be reviewed were invisible and Start review was not offered.
    /// </summary>
    [Fact]
    public async Task AWindowOpenedBetweenRuns_ShowsWhatIsWaitingToBeReviewed()
    {
        await RunUi(async () =>
        {
            using var host = TestHost.Create();
            var queue = host.Resolve<BatchQueueService>();
            var item = await queue.EnqueueAsync("9780441013593", bookId: null);
            await queue.UpdateStatusAsync(item.BatchQueueItemId, BatchStatus.PendingReview, "{}");

            var viewModel = host.Resolve<BatchQueueWindowViewModel>();
            viewModel.ResetStats();
            await viewModel.ShowStoredSummaryAsync();

            Assert.Equal(1, viewModel.PendingReviewCount);
            Assert.True(viewModel.IsComplete);
            Assert.False(viewModel.IsIdle);
        });
    }
}
