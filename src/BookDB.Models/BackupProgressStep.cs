namespace BookDB.Models;

/// <summary>
/// Steps of a backup or restore, reported as a typed <see cref="ProgressUpdate{TStep}"/> so the operation
/// carries no localization dependency. The Desktop layer maps each step to a status string for the progress
/// window.
/// </summary>
public enum BackupProgressStep
{
    // SQLite file backup — reported by the SQLite backup strategy.
    FlushingLog,
    CopyingDatabase,
    CreatingArchive,

    // CSV archive export — reported by BackupService. (CreatingArchive above is shared with the CSV path.)
    ExportingBooks,
    ExportingPeople,
    ExportingPublishers,
    ExportingSeries,
    ExportingCollections,
    ExportingCategories,
    ExportingFormats,
    ExportingLanguages,
    ExportingLocations,
    ExportingOwners,
    ExportingRelationships,
    ExportingLookups,
    ExportingLoans,
    ExportingSettings,
    ExportingCoverImages,
    // Reports Current/Total = images written so far / total.
    ExportingCoverImagesCount,

    // Restore.
    SavingSafetyBackup,
    ExtractingArchive,
    ReplacingLibrary,
}
