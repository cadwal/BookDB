namespace BookDB.Logic.Services;

/// <summary>
/// Bounded in-memory cache of downloaded cover images, keyed by batch queue item and source name.
/// Filled by BatchQueueProcessor when an item lands in PendingReview so the review dialog can show
/// covers without waiting on the network; consumed and released by the review flow.
/// In-memory only — after an app restart the review dialog streams covers in instead.
/// </summary>
public interface ICoverCache
{
    /// <summary>Returns the cached cover bytes, or null when absent.</summary>
    byte[]? TryGet(int batchQueueItemId, string sourceName);

    /// <summary>Stores cover bytes, evicting least-recently-used entries beyond the size budget.</summary>
    void Set(int batchQueueItemId, string sourceName, byte[] bytes);

    /// <summary>Drops every cached cover belonging to the given batch queue item.</summary>
    void RemoveItem(int batchQueueItemId);
}
