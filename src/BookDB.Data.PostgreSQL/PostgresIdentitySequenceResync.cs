using System.Data;
using System.Threading;
using System.Threading.Tasks;
using BookDB.Data.DbContexts;
using BookDB.Data.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace BookDB.Data.PostgreSQL;

/// <inheritdoc cref="IIdentitySequenceResync"/>
public sealed class PostgresIdentitySequenceResync : IIdentitySequenceResync
{
    // Every table with an auto-increment identity primary key, with its PK column. Tables with composite keys
    // (BookCategory, CategoryCollection), string keys (Settings), or explicit-value enum keys (BookImageType,
    // BorrowerStatus) have no sequence and are intentionally absent.
    private static readonly (string Table, string Column)[] IdentityTables =
    [
        ("Book", "BookId"), ("Person", "PersonId"), ("Publisher", "PublisherId"), ("Series", "SeriesId"),
        ("Category", "CategoryId"), ("Collection", "CollectionId"), ("ContributorRole", "ContributorRoleId"),
        ("Format", "FormatId"), ("Edition", "EditionId"), ("Language", "LanguageId"), ("Location", "LocationId"),
        ("Owner", "OwnerId"), ("PurchasePlace", "PurchasePlaceId"), ("Rating", "RatingId"),
        ("ReadingLevel", "ReadingLevelId"), ("Source", "SourceId"), ("Status", "StatusId"),
        ("Condition", "ConditionId"), ("SavedSearch", "SavedSearchId"), ("BatchQueueItem", "BatchQueueItemId"),
        ("BookImage", "BookImageId"), ("BookVolume", "BookVolumeId"), ("BookChapter", "BookChapterId"),
        ("Borrower", "BorrowerId"), ("Loan", "LoanId"), ("BookContributor", "BookContributorId"),
    ];

    public async Task ResyncAsync(BookDbContext context, CancellationToken ct = default)
    {
        var connection = context.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await context.Database.OpenConnectionAsync(ct);
        // Enlist in the caller's transaction when there is one (the restore runs truncate+import+resync atomically);
        // null when the migration calls this on a plain connection.
        var transaction = context.Database.CurrentTransaction?.GetDbTransaction();

        foreach (var (table, column) in IdentityTables)
        {
            // value = highest copied id (or 1 if empty); is_called = whether the table has rows, so an empty
            // table's next id is 1 while a populated table's next id is max+1. Identifiers are constants, not
            // user input — quoted to preserve the PascalCase the DDL created.
            await using var cmd = connection.CreateCommand();
            cmd.Transaction = transaction;
            cmd.CommandText =
                $"SELECT setval(pg_get_serial_sequence('\"{table}\"', '{column}'), " +
                $"GREATEST(COALESCE((SELECT MAX(\"{column}\") FROM \"{table}\"), 1), 1), " +
                $"(SELECT COUNT(*) FROM \"{table}\") > 0);";
            await cmd.ExecuteNonQueryAsync(ct);
        }
    }
}
