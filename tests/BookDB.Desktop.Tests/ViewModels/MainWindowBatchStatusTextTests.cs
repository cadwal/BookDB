using BookDB.Desktop.Localization;
using BookDB.Desktop.ViewModels;
using Xunit;

namespace BookDB.Desktop.Tests.ViewModels;

/// <summary>
/// The main window's status-bar batch text: progress plus the running outcome (items to review,
/// failures) so a batch can be followed without its window. Expectations are composed from the
/// same resources the implementation uses, so the tests stay culture-independent.
/// </summary>
public sealed class MainWindowBatchStatusTextTests
{
    private static string Progress(int current, int total) =>
        string.Format(Resources.StatusBar_BatchQueue_Progress, current, total);

    [Fact]
    public void Running_WithoutOutcome_ShowsOnlyProgress()
    {
        var text = MainWindowViewModel.ComposeBatchStatusText(
            isRunning: true, isMinimized: false, current: 3, total: 10, toReview: 0, failed: 0);

        Assert.Equal(Progress(3, 10), text);
    }

    [Fact]
    public void Running_WithReviewItemsOnly_AppendsTheReviewCount()
    {
        var text = MainWindowViewModel.ComposeBatchStatusText(
            isRunning: true, isMinimized: false, current: 3, total: 10, toReview: 2, failed: 0);

        Assert.Equal(
            string.Format(Resources.StatusBar_BatchQueue_WithOutcome,
                Progress(3, 10),
                string.Format(Resources.StatusBar_BatchQueue_ToReview, 2)),
            text);
    }

    [Fact]
    public void Running_WithReviewAndFailures_AppendsBothCounts()
    {
        var text = MainWindowViewModel.ComposeBatchStatusText(
            isRunning: true, isMinimized: false, current: 7, total: 10, toReview: 2, failed: 1);

        Assert.Equal(
            string.Format(Resources.StatusBar_BatchQueue_WithOutcome,
                Progress(7, 10),
                string.Format(Resources.StatusBar_BatchQueue_OutcomePair,
                    string.Format(Resources.StatusBar_BatchQueue_ToReview, 2),
                    string.Format(Resources.StatusBar_BatchQueue_Failed, 1))),
            text);
    }

    [Fact]
    public void CompleteAndMinimized_ShowsCompletionHeaderWithOutcome()
    {
        var text = MainWindowViewModel.ComposeBatchStatusText(
            isRunning: false, isMinimized: true, current: 10, total: 10, toReview: 0, failed: 3);

        Assert.Equal(
            string.Format(Resources.StatusBar_BatchQueue_WithOutcome,
                Resources.BatchQueue_CompleteHeader,
                string.Format(Resources.StatusBar_BatchQueue_Failed, 3)),
            text);
    }

    [Fact]
    public void CompleteAndNotMinimized_IsEmpty()
    {
        var text = MainWindowViewModel.ComposeBatchStatusText(
            isRunning: false, isMinimized: false, current: 10, total: 10, toReview: 2, failed: 1);

        Assert.Equal(string.Empty, text);
    }
}
