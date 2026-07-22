namespace BookDB.Models.Entities;

/// <summary>
/// Why a batch lookup item failed. Stored as the enum name in <see cref="BatchQueueItem.FailureCode"/>;
/// the UI localizes it — the stored name must never be shown raw.
/// </summary>
public enum BatchFailureReason
{
    /// <summary>At least one source answered, but none had the ISBN.</summary>
    NoResults,

    /// <summary>Every queried source errored or the per-item timeout fired — nothing answered.</summary>
    NetworkError,

    /// <summary>No source had the ISBN and at least one was rate-limited (HTTP 429) — worth retrying later.</summary>
    RateLimited,

    /// <summary>No source was queried because every lookup source is disabled in settings.</summary>
    AllSourcesDisabled,

    /// <summary>Processing threw outside the lookup itself (database error, bad payload, …).</summary>
    Unexpected,
}
