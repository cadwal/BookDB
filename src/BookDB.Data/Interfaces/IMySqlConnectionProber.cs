using System.Threading;
using System.Threading.Tasks;
using BookDB.Models;

namespace BookDB.Data.Interfaces;

/// <summary>
/// Tests a MySQL/MariaDB server with the given parameters without disturbing the running app: a throwaway,
/// non-pooled connection bounded by a short timeout. Used by the Settings → Database tab before a backend
/// switch is applied. Registered regardless of the active backend, since a user on SQLite must be able to test
/// a MySQL server before switching to it.
/// </summary>
public interface IMySqlConnectionProber
{
    Task<ConnectionProbeResult> ProbeAsync(MySqlOptions options, string? password, CancellationToken ct = default);
}
