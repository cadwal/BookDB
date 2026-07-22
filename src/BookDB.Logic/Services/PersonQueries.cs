using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BookDB.Data.DbContexts;
using BookDB.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace BookDB.Logic.Services;

internal static class PersonQueries
{
    /// <summary>
    /// Case-insensitive Person lookup by display name for reuse-or-create paths. A plain
    /// <c>==</c> comparison's case sensitivity is collation-dependent (SQLite/PostgreSQL
    /// case-sensitive, MySQL not); lowering both sides makes every backend reuse "tolkien" for
    /// "Tolkien". SQLite's LOWER() only folds ASCII, so non-ASCII case variants still create a
    /// new person there — same as the previous exact-match behaviour, no regression.
    /// Ordered so duplicate case-variants resolve to the same person on every backend.
    /// </summary>
    public static Task<Person?> FindByDisplayNameAsync(
        BookDbContext db, string displayName, CancellationToken ct)
    {
        var lowered = displayName.ToLowerInvariant();
        return db.People
            .Where(p => p.DisplayName.ToLower() == lowered)
            .OrderBy(p => p.PersonId)
            .FirstOrDefaultAsync(ct);
    }
}
