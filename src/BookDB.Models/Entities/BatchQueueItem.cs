using System;

namespace BookDB.Models.Entities;

public class BatchQueueItem
{
    public int BatchQueueItemId { get; set; }
    public string Isbn { get; set; } = string.Empty;
    public int? BookId { get; set; }
    public string Status { get; set; } = BatchStatus.Pending;
    public string? ResultJson { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
