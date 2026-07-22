using System.Collections.Generic;
using System.Linq;

namespace BookDB.Models;

/// <summary>The stage a library migration is in, for progress reporting.</summary>
public enum MigrationPhase
{
    /// <summary>Emptying the target so copied row counts can be verified against the source.</summary>
    Clearing,

    /// <summary>Copying a table's rows.</summary>
    Copying,

    /// <summary>Resyncing identity sequences after the explicit-key copy.</summary>
    Finalizing,

    /// <summary>Comparing source and target row counts.</summary>
    Verifying,
}

/// <summary>
/// The tables a migration copies, in FK-safe insertion order. The migration layer stays localization-free and
/// emits these enum values; the Desktop layer maps each to a localized label (images are an explicit row).
/// </summary>
public enum MigrationTable
{
    Collection, Person, ContributorRole, Publisher, Series, Category, Condition, Edition, Format,
    Language, Location, Owner, PurchasePlace, Rating, ReadingLevel, Source, Status, BorrowerStatus,
    BookImageType, CategoryCollection, Book, Settings, SavedSearch, BatchQueueItem, BookContributor,
    BookCategory, BookImage, BookVolume, BookChapter, Borrower, Loan, PersonCleanupIgnore,
}

/// <summary>A progress tick during migration: the current phase, the table being copied (if any), and its counts.</summary>
public sealed record MigrationProgress(MigrationPhase Phase, MigrationTable? Table, long Copied, long Total);

/// <summary>Per-table verification: rows in the source vs rows that landed in the target.</summary>
public sealed record MigrationTableResult(MigrationTable Table, long SourceCount, long TargetCount)
{
    public bool Matches => SourceCount == TargetCount;
}

public enum MigrationOutcome { Completed, Failed }

/// <summary>
/// Outcome of a library migration. On failure the target holds partial data (it is never auto-cleaned), so
/// <see cref="FailedTable"/> names where the copy stopped. <see cref="AllCountsMatch"/> gates the
/// "switch active database" option — a mismatch must block it.
/// </summary>
public sealed record MigrationResult(
    MigrationOutcome Outcome,
    IReadOnlyList<MigrationTableResult> Tables,
    MigrationTable? FailedTable,
    string? ErrorMessage)
{
    public bool AllCountsMatch => Outcome == MigrationOutcome.Completed && Tables.All(t => t.Matches);
}
