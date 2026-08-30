using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BookDB.Data.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace BookDB.Logic.Services;

public sealed class CompanionQueueStatusService : ICompanionQueueStatusService
{
    private readonly IDbContextFactory<BookDbContext> _factory;

    public CompanionQueueStatusService(IDbContextFactory<BookDbContext> factory) => _factory = factory;

    public async Task<IReadOnlyList<CompanionQueueItemStatus>> GetStatusesAsync(
        IReadOnlyList<int> batchQueueItemIds, CancellationToken ct = default)
    {
        if (batchQueueItemIds.Count == 0)
        {
            return [];
        }

        await using var db = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);

        return await db.BatchQueueItems
            .Where(i => batchQueueItemIds.Contains(i.BatchQueueItemId))
            .Select(i => new CompanionQueueItemStatus(i.BatchQueueItemId, i.Status, i.FailureCode, i.BookId))
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }
}
