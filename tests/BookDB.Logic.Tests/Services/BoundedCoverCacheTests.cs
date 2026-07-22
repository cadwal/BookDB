using BookDB.Logic.Services;
using Xunit;

namespace BookDB.Logic.Tests.Services;

public sealed class BoundedCoverCacheTests
{
    [Fact]
    public void SetAndTryGet_RoundTripsPerItemAndSource()
    {
        var cache = new BoundedCoverCache();
        cache.Set(1, "Source1", [1, 2, 3]);
        cache.Set(1, "Source2", [4, 5]);
        cache.Set(2, "Source1", [6]);

        Assert.Equal(new byte[] { 1, 2, 3 }, cache.TryGet(1, "Source1"));
        Assert.Equal(new byte[] { 4, 5 }, cache.TryGet(1, "Source2"));
        Assert.Equal(new byte[] { 6 }, cache.TryGet(2, "Source1"));
        Assert.Null(cache.TryGet(1, "Source3"));
        Assert.Null(cache.TryGet(3, "Source1"));
    }

    [Fact]
    public void Set_ReplacesExistingEntryForSameKey()
    {
        var cache = new BoundedCoverCache();
        cache.Set(1, "Source1", [1, 2, 3]);
        cache.Set(1, "Source1", [9]);

        Assert.Equal(new byte[] { 9 }, cache.TryGet(1, "Source1"));
    }

    [Fact]
    public void Set_EvictsLeastRecentlyUsed_WhenByteBudgetExceeded()
    {
        var cache = new BoundedCoverCache(maxTotalBytes: 10);
        cache.Set(1, "A", new byte[4]);
        cache.Set(2, "B", new byte[4]);

        // Touch the older entry so item 2 becomes the eviction candidate.
        Assert.NotNull(cache.TryGet(1, "A"));

        cache.Set(3, "C", new byte[4]);

        Assert.NotNull(cache.TryGet(1, "A"));
        Assert.Null(cache.TryGet(2, "B"));
        Assert.NotNull(cache.TryGet(3, "C"));
    }

    [Fact]
    public void Set_IgnoresPayloadLargerThanTheWholeBudget()
    {
        var cache = new BoundedCoverCache(maxTotalBytes: 10);
        cache.Set(1, "A", new byte[3]);
        cache.Set(1, "B", new byte[11]);

        Assert.Null(cache.TryGet(1, "B"));
        Assert.NotNull(cache.TryGet(1, "A"));
    }

    [Fact]
    public void RemoveItem_DropsAllSourcesOfThatItemOnly()
    {
        var cache = new BoundedCoverCache();
        cache.Set(1, "Source1", [1]);
        cache.Set(1, "Source2", [2]);
        cache.Set(2, "Source1", [3]);

        cache.RemoveItem(1);

        Assert.Null(cache.TryGet(1, "Source1"));
        Assert.Null(cache.TryGet(1, "Source2"));
        Assert.NotNull(cache.TryGet(2, "Source1"));
    }
}
