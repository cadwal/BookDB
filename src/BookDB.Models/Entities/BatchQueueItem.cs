using System;

namespace BookDB.Models.Entities;

public class BatchQueueItem
{
    public int BatchQueueItemId { get; set; }
    public string Isbn { get; set; } = string.Empty;
    public int? BookId { get; set; }
    public string Status { get; set; } = BatchStatus.Pending;
    public string? ResultJson { get; set; }

    /// <summary>
    /// Skips the processor's auto-accept for this item so it always lands in review — the guided
    /// single-book add promises the user a confirm step even when all sources agree.
    /// </summary>
    public bool ForceReview { get; set; }

    /// <summary>Failure reason code (enum name), localized by the UI; null while not failed.</summary>
    public string? FailureCode { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
