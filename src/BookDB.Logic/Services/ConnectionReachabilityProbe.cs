using System.Threading;
using System.Threading.Tasks;
using BookDB.Data.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace BookDB.Logic.Services;

/// <summary>A cheap "is the database reachable right now" check used by <see cref="ConnectionHealthMonitor"/>.</summary>
public interface IConnectionReachabilityProbe
{
    Task<bool> IsReachableAsync(CancellationToken ct = default);
}

/// <summary>
/// Reaches the live database through the same context factory the app uses, so the check exercises the real
/// connection (including its configured credentials and timeout). Any failure is reported as unreachable.
/// </summary>
public sealed class DbContextReachabilityProbe : IConnectionReachabilityProbe
{
    private readonly IDbContextFactory<BookDbContext> _factory;

    public DbContextReachabilityProbe(IDbContextFactory<BookDbContext> factory) => _factory = factory;

    public async Task<bool> IsReachableAsync(CancellationToken ct = default)
    {
        try
        {
            await using var db = await _factory.CreateDbContextAsync(ct);
            return await db.Database.CanConnectAsync(ct);
        }
        catch
        {
            return false;
        }
    }
}
