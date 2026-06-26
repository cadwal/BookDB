using System.Collections.Generic;

namespace BookDB.Models;

/// <summary>
/// A progress step reported during maintenance. The maintenance layer stays localization-free and emits these
/// enum values; the Desktop layer maps each to a localized string (with a test that the mapping is exhaustive,
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
    /// <summary>The integrity check returned "ok" and there were no foreign-key violations.</summary>
    Ok,

    /// <summary>The integrity check reported structural problems (real corruption).</summary>
    IntegrityFailed,

    /// <summary>Structure is sound but the foreign-key check found dangling references.</summary>
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
