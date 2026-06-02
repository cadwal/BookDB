using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BookDB.Models.Entities;

namespace BookDB.Logic.Services;

public interface IBatchQueueProcessor
{
    bool IsPaused { get; }
    Task<IReadOnlyList<BatchQueueItem>> ReloadPendingFromDatabaseAsync(CancellationToken ct = default);
    Task StartBatch(IReadOnlyList<BatchQueueItem> items);
    Task CancelBatchAsync();
    Task PauseAsync();
    void Resume();
    Task StopAsync(CancellationToken ct);
}
