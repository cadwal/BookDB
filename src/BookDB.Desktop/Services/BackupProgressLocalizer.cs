using System;
using BookDB.Desktop.Localization;
using BookDB.Models;

namespace BookDB.Desktop.Services;

/// <summary>
/// Maps a typed <see cref="BackupProgressStep"/> update to its localized status string for the progress window.
/// Backup and restore run in the Logic layer emitting steps; localization stays here in the Desktop layer.
/// </summary>
public static class BackupProgressLocalizer
{
    public static string ToDisplayString(ProgressUpdate<BackupProgressStep> update) => update.Step switch
    {
        BackupProgressStep.FlushingLog => Resources.Backup_Status_FlushingLog,
        BackupProgressStep.CopyingDatabase => Resources.Backup_Status_CopyingDatabase,
        BackupProgressStep.CreatingArchive => Resources.Backup_Status_CreatingArchive,
        BackupProgressStep.ExportingBooks => Resources.Backup_Status_ExportingBooks,
        BackupProgressStep.ExportingPeople => Resources.Backup_Status_ExportingPeople,
        BackupProgressStep.ExportingPublishers => Resources.Backup_Status_ExportingPublishers,
        BackupProgressStep.ExportingSeries => Resources.Backup_Status_ExportingSeries,
        BackupProgressStep.ExportingCollections => Resources.Backup_Status_ExportingCollections,
        BackupProgressStep.ExportingCategories => Resources.Backup_Status_ExportingCategories,
        BackupProgressStep.ExportingFormats => Resources.Backup_Status_ExportingFormats,
        BackupProgressStep.ExportingLanguages => Resources.Backup_Status_ExportingLanguages,
        BackupProgressStep.ExportingLocations => Resources.Backup_Status_ExportingLocations,
        BackupProgressStep.ExportingOwners => Resources.Backup_Status_ExportingOwners,
        BackupProgressStep.ExportingRelationships => Resources.Backup_Status_ExportingRelationships,
        BackupProgressStep.ExportingLookups => Resources.Backup_Status_ExportingLookups,
        BackupProgressStep.ExportingLoans => Resources.Backup_Status_ExportingLoans,
        BackupProgressStep.ExportingSettings => Resources.Backup_Status_ExportingSettings,
        BackupProgressStep.ExportingCoverImages => Resources.Backup_Status_ExportingCoverImages,
        BackupProgressStep.ExportingCoverImagesCount =>
            string.Format(Resources.Backup_Status_ExportingCoverImagesCount, update.Current, update.Total),
        BackupProgressStep.SavingSafetyBackup => Resources.Restore_Status_SavingSafetyBackup,
        BackupProgressStep.ExtractingArchive => Resources.Restore_Status_ExtractingArchive,
        BackupProgressStep.ReplacingLibrary => Resources.Restore_Status_ReplacingLibrary,
        _ => throw new ArgumentOutOfRangeException(
            nameof(update), update.Step, "Unmapped backup progress step."),
    };

    /// <summary>
    /// Wraps a string progress sink (the progress window) so a Logic-layer operation can report typed steps;
    /// each step is localized here before it reaches the window. Returns null when there is no sink.
    /// </summary>
    public static IProgress<ProgressUpdate<BackupProgressStep>>? Localizing(IProgress<string>? sink)
        => sink is null ? null : new StepSink(sink);

    private sealed class StepSink : IProgress<ProgressUpdate<BackupProgressStep>>
    {
        private readonly IProgress<string> _sink;
        public StepSink(IProgress<string> sink) => _sink = sink;
        public void Report(ProgressUpdate<BackupProgressStep> value) => _sink.Report(ToDisplayString(value));
    }
}
