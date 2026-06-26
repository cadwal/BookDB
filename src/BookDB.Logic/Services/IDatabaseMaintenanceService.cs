using System;
using System.Threading;
using System.Threading.Tasks;
using BookDB.Models;

namespace BookDB.Logic.Services;

/// <summary>
/// Read-only integrity checks and safe optimize/repair for the active library. Repair never attempts risky
/// in-place surgery on a corrupt file — it runs only safe operations after taking a safety backup; genuine
/// corruption is reported so the user can restore from a backup instead. The engine-specific work is performed
/// by an <see cref="BookDB.Data.Interfaces.IMaintenanceProvider"/>; the safety backup stays here.
/// </summary>
public interface IDatabaseMaintenanceService
{
    /// <summary>Runs the engine's read-only integrity and foreign-key checks.</summary>
    Task<MaintenanceCheckResult> CheckIntegrityAsync(
        CancellationToken ct = default, IProgress<MaintenanceStep>? progress = null);

    /// <summary>
    /// Takes a safety backup, then runs the engine's safe optimize/repair pass. <paramref name="safetyBackupReport"/>
    /// receives the backup's location the moment it is written, so the UI can show it in step order rather than
    /// only at the end from the result.
    /// </summary>
    Task<MaintenanceRepairResult> OptimizeAndRepairAsync(
        CancellationToken ct = default, IProgress<MaintenanceStep>? progress = null,
        IProgress<string>? safetyBackupReport = null);
}
