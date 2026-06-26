using System;
using System.Threading;
using System.Threading.Tasks;
using BookDB.Models;

namespace BookDB.Data.Interfaces;

/// <summary>
/// Engine-specific database maintenance: read-only integrity checks and the safe optimize/repair pass
/// (e.g. REINDEX/VACUUM/checkpoint on SQLite). The provider-neutral safety backup is taken by the calling
/// service, not here.
/// </summary>
public interface IMaintenanceProvider
{
    Task<MaintenanceCheckResult> CheckIntegrityAsync(IProgress<MaintenanceStep>? progress, CancellationToken ct);

    Task<MaintenanceRepairResult> OptimizeAndRepairAsync(IProgress<MaintenanceStep>? progress, CancellationToken ct);
}
