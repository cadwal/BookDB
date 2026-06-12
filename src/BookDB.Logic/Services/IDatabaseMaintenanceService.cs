using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BookDB.Logic.Services;

/// <summary>
/// A progress step reported during maintenance. The service stays localization-free and emits these enum
/// values; the Desktop layer maps each to a localized string (with a test that the mapping is exhaustive,
/// so a new step can never silently lack a resource).
/// </summary>
public enum MaintenanceStep
{
    CheckingIntegrity,
    CheckingForeignKeys,
    SafetyBackup,
    Reindex,
    Vacuum,
    Checkpoint,
}

/// <summary>Overall outcome of a database integrity check.</summary>
public enum MaintenanceCheckStatus
{
    /// <summary><c>integrity_check</c> returned "ok" and there were no foreign-key violations.</summary>
    Ok,

    /// <summary><c>PRAGMA integrity_check</c> reported structural problems (real corruption).</summary>
    IntegrityFailed,

    /// <summary>Structure is sound but <c>PRAGMA foreign_key_check</c> found dangling references.</summary>
    ForeignKeyViolations,
}

/// <summary>Result of a read-only integrity check. Detail lists are always populated for the log.</summary>
public sealed record MaintenanceCheckResult(
    MaintenanceCheckStatus Status,
    IReadOnlyList<string> IntegrityMessages,
    IReadOnlyList<string> ForeignKeyViolations);

/// <summary>Result of the optimize/repair pass.</summary>
public sealed record MaintenanceRepairResult(
    bool Success,
    string? SafetyBackupPath,
    long SizeBeforeBytes,
    long SizeAfterBytes,
    string? ErrorMessage);

/// <summary>
/// Read-only integrity checks and safe optimize/repair for the active SQLite library. Repair never attempts
/// risky in-place surgery on a corrupt file — it runs only safe operations (REINDEX, VACUUM, WAL checkpoint)
/// after taking a safety backup; genuine corruption is reported so the user can restore from a backup instead.
/// </summary>
public interface IDatabaseMaintenanceService
{
    /// <summary>
    /// Runs <c>PRAGMA integrity_check</c> and <c>PRAGMA foreign_key_check</c>. Read-only.
    /// </summary>
    Task<MaintenanceCheckResult> CheckIntegrityAsync(
        CancellationToken ct = default, IProgress<MaintenanceStep>? progress = null);

    /// <summary>
    /// Takes a safety backup, then runs <c>REINDEX</c>, <c>VACUUM</c> and <c>PRAGMA wal_checkpoint(TRUNCATE)</c>
    /// to rebuild indexes and compact the file. Writes to the database.
    /// </summary>
    Task<MaintenanceRepairResult> OptimizeAndRepairAsync(
        CancellationToken ct = default, IProgress<MaintenanceStep>? progress = null);
}
