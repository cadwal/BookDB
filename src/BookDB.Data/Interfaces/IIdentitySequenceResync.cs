using System.Threading;
using System.Threading.Tasks;
using BookDB.Data.DbContexts;

namespace BookDB.Data.Interfaces;

/// <summary>
/// Resynchronises auto-increment identity sequences after a migration copy inserts rows with explicit primary
/// keys. Postgres sequences are not advanced by an explicit-value insert, so the first new row afterwards would
/// collide; this fixes every identity sequence to the highest copied value. SQLite tracks the next rowid from
/// the table itself, so its implementation is a no-op.
/// </summary>
public interface IIdentitySequenceResync
{
    Task ResyncAsync(BookDbContext context, CancellationToken ct = default);
}
