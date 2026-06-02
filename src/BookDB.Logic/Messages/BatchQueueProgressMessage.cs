namespace BookDB.Logic.Messages;

/// <summary>
/// Sent by BatchQueueProcessor during and after batch ISBN processing.
/// </summary>
public sealed class BatchQueueProgressMessage
{
    public int Current { get; init; }
    public int Total { get; init; }
    public string? CurrentIsbn { get; init; }
    public bool IsRunning { get; init; }
    public BatchProgressStatus StatusCode { get; init; }
    /// <summary>Number of source results — used when StatusCode is ProcessingResults.</summary>
    public int ResultCount { get; init; }
}
