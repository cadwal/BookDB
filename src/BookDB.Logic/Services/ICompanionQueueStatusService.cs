using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BookDB.Logic.Services;

public sealed record CompanionQueueItemStatus(int BatchQueueItemId, string Status, string? FailureCode, int? BookId);

/// <summary>
/// Reads back where the catalogue queue has got to with particular items. The processor records outcomes
/// on the rows themselves and publishes only aggregate progress, so following one item means reading it.
/// </summary>
public interface ICompanionQueueStatusService
{
    Task<IReadOnlyList<CompanionQueueItemStatus>> GetStatusesAsync(
        IReadOnlyList<int> batchQueueItemIds, CancellationToken ct = default);
}
