using System;
using BookDB.Desktop.ViewModels;
using BookDB.Logic.Messages;
using Xunit;

namespace BookDB.Desktop.Tests.ViewModels;

/// <summary>
/// The batch window's per-item stage text: every progress status the processor can emit must render
/// localized, so a stage (querying, processing, fetching covers, saving) is never silently invisible.
/// Assertions compare against the resource properties, not literals, to stay culture-independent.
/// </summary>
public sealed class BatchQueueStatusMappingTests
{
    [Fact]
    public void EveryStatusExceptNone_MapsToNonEmptyText()
    {
        foreach (var status in Enum.GetValues<BatchProgressStatus>())
        {
            var text = BatchQueueWindowViewModel.DescribeStatus(status, "9780451526538", 2);
            if (status == BatchProgressStatus.None)
                Assert.Equal(string.Empty, text);
            else
                Assert.False(string.IsNullOrWhiteSpace(text), $"BatchProgressStatus.{status} has no localized text");
        }
    }

    [Fact]
    public void FetchingCovers_UsesItsOwnResourceText()
    {
        Assert.Equal(
            BookDB.Desktop.Localization.Resources.BatchQueue_StatusFetchingCovers,
            BatchQueueWindowViewModel.DescribeStatus(BatchProgressStatus.FetchingCovers, "9780451526538", 0));
    }

    [Fact]
    public void QueryingSources_EmbedsTheIsbn()
    {
        var text = BatchQueueWindowViewModel.DescribeStatus(BatchProgressStatus.QueryingSources, "9780451526538", 0);
        Assert.Contains("9780451526538", text);
    }
}
