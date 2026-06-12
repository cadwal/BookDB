using System;
using BookDB.Logic.Services;

namespace BookDB.Desktop.Localization;

/// <summary>
/// Maps the maintenance enums emitted by <see cref="IDatabaseMaintenanceService"/> to localized strings. Each
/// arm references a typed <see cref="Resources"/> property, so a missing or renamed resource key is a compile
/// error; <c>MaintenanceTextTests</c> additionally proves every enum value is mapped to a non-empty string, so
/// the enums and the resources can never silently drift apart.
/// </summary>
public static class MaintenanceText
{
    public static string Describe(MaintenanceStep step) => step switch
    {
        MaintenanceStep.CheckingIntegrity   => Resources.Maintenance_Step_CheckingIntegrity,
        MaintenanceStep.CheckingForeignKeys => Resources.Maintenance_Step_CheckingForeignKeys,
        MaintenanceStep.SafetyBackup        => Resources.Maintenance_Step_SafetyBackup,
        MaintenanceStep.Reindex             => Resources.Maintenance_Step_Reindex,
        MaintenanceStep.Vacuum              => Resources.Maintenance_Step_Vacuum,
        MaintenanceStep.Checkpoint          => Resources.Maintenance_Step_Checkpoint,
        _ => throw new ArgumentOutOfRangeException(nameof(step), step, "Unmapped MaintenanceStep"),
    };

    public static string Describe(MaintenanceCheckStatus status) => status switch
    {
        MaintenanceCheckStatus.Ok                   => Resources.Maintenance_Status_Ok,
        MaintenanceCheckStatus.IntegrityFailed      => Resources.Maintenance_Status_IntegrityFailed,
        MaintenanceCheckStatus.ForeignKeyViolations => Resources.Maintenance_Status_ForeignKeyViolations,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unmapped MaintenanceCheckStatus"),
    };
}
