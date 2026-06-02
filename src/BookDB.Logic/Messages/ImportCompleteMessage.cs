namespace BookDB.Logic.Messages;

/// <summary>
/// Sent by ImportService after a successful (or cancelled) import run.
/// The book list subscribes to this to trigger a single full refresh.
/// </summary>
public record ImportCompleteMessage(int ImportedCount, int UpdatedCount, bool WasCancelled);
