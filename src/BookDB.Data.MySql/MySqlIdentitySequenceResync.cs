using System.Threading;
using System.Threading.Tasks;
using BookDB.Data.DbContexts;
using BookDB.Data.Interfaces;

namespace BookDB.Data.MySql;

/// <inheritdoc cref="IIdentitySequenceResync"/>
public sealed class MySqlIdentitySequenceResync : IIdentitySequenceResync
{
    // No-op: InnoDB advances a table's AUTO_INCREMENT counter to MAX(id)+1 automatically as rows with explicit
    // primary keys are inserted, so after a migration copy the next id is already correct. There is no standalone
    // sequence object to reset (unlike Postgres serial sequences), so nothing to do here.
    public Task ResyncAsync(BookDbContext context, CancellationToken ct = default) => Task.CompletedTask;
}
