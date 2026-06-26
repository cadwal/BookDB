using System.Threading;
using System.Threading.Tasks;
using BookDB.Data.DbContexts;
using BookDB.Data.Interfaces;

namespace BookDB.Data.Sqlite;

/// <summary>
/// SQLite derives the next rowid for an INTEGER PRIMARY KEY from the table's current maximum, so an explicit-id
/// migration copy needs no sequence fix-up.
/// </summary>
public sealed class SqliteIdentitySequenceResync : IIdentitySequenceResync
{
    public Task ResyncAsync(BookDbContext context, CancellationToken ct = default) => Task.CompletedTask;
}
