using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using BookDB.Data.DbContexts;
using BookDB.Data.Interfaces;
using BookDB.Models;
using BookDB.Models.Entities;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace BookDB.Logic.Services;

/// <summary>
/// Copies an entire library from one backend's database to another (SQLite ↔ Postgres) as a direct EF
/// context→context transfer that preserves primary keys.
/// </summary>
public interface ILibraryMigrationService
{
    /// <summary>
    /// Empties the target, copies every table from <paramref name="source"/> in FK-safe order (images batched),
    /// resyncs identity sequences via <paramref name="targetResync"/>, and verifies per-table row counts. On a
    /// mid-copy failure the target keeps its partial data (never auto-cleaned) and the result names the failing table.
    /// <paramref name="targetResync"/> must be the target backend's implementation (the running app's DI is the source backend).
    /// </summary>
    Task<MigrationResult> MigrateAsync(
        IDbContextFactory<BookDbContext> source,
        IDbContextFactory<BookDbContext> target,
        IIdentitySequenceResync targetResync,
        IProgress<MigrationProgress>? progress = null,
        CancellationToken ct = default);
}

/// <inheritdoc cref="ILibraryMigrationService"/>
public sealed class LibraryMigrationService : ILibraryMigrationService
{
    // Cover-image rows carry the BLOBs; copying them in small batches keeps process memory bounded on large catalogs.
    private const int ImageBatchSize = 50;

    // Non-image tables copy in batches of this size so the UI can report incremental progress
    // (one log row per batch) rather than a single line once the whole table is done.
    private const int CopyBatchSize = 100;

    public async Task<MigrationResult> MigrateAsync(
        IDbContextFactory<BookDbContext> source,
        IDbContextFactory<BookDbContext> target,
        IIdentitySequenceResync targetResync,
        IProgress<MigrationProgress>? progress = null,
        CancellationToken ct = default)
    {
        var results = new List<MigrationTableResult>();
        MigrationTable? current = null;

        await using var src = await source.CreateDbContextAsync(ct);

        try
        {
            // Start from an empty target so seeded lookups can't collide with the copy and the per-table counts
            // verify against the source. Reverse FK order keeps every delete legal under Postgres FK enforcement.
            progress?.Report(new MigrationProgress(MigrationPhase.Clearing, null, 0, 0));
            await using (var clearCtx = await target.CreateDbContextAsync(ct))
                await ClearTargetAsync(clearCtx, ct);

            async Task Copy<T>(MigrationTable table) where T : class
            {
                current = table;
                await CopyTableAsync<T>(table, src, target, results, progress, ct);
            }

            // 1. Pure lookups (no FKs).
            await Copy<Collection>(MigrationTable.Collection);
            await Copy<Person>(MigrationTable.Person);
            await Copy<ContributorRole>(MigrationTable.ContributorRole);
            await Copy<Publisher>(MigrationTable.Publisher);
            await Copy<Series>(MigrationTable.Series);
            await Copy<Category>(MigrationTable.Category);
            await Copy<Condition>(MigrationTable.Condition);
            await Copy<Edition>(MigrationTable.Edition);
            await Copy<Format>(MigrationTable.Format);
            await Copy<Language>(MigrationTable.Language);
            await Copy<Location>(MigrationTable.Location);
            await Copy<Owner>(MigrationTable.Owner);
            await Copy<PurchasePlace>(MigrationTable.PurchasePlace);
            await Copy<Rating>(MigrationTable.Rating);
            await Copy<ReadingLevel>(MigrationTable.ReadingLevel);
            await Copy<Source>(MigrationTable.Source);
            await Copy<Status>(MigrationTable.Status);

            // BorrowerStatus and BookImageType are fixed schema-seeded enums (their 'Active'/'Cover' rows have a
            // primary key of 0, which EF treats as unset and cannot insert). They are identical on every BookDB
            // database, so the target keeps its own DDL seeds; we only verify the counts match.
            current = MigrationTable.BorrowerStatus;
            await VerifyOnlyAsync<BorrowerStatus>(MigrationTable.BorrowerStatus, src, target, results, progress, ct);
            current = MigrationTable.BookImageType;
            await VerifyOnlyAsync<BookImageType>(MigrationTable.BookImageType, src, target, results, progress, ct);

            // 2. Lookup cross-reference, then core entities.
            await Copy<CategoryCollection>(MigrationTable.CategoryCollection);
            await Copy<Book>(MigrationTable.Book);
            await Copy<Settings>(MigrationTable.Settings);
            await Copy<SavedSearch>(MigrationTable.SavedSearch);
            await Copy<BatchQueueItem>(MigrationTable.BatchQueueItem);

            // 3. Book-dependent (images batched), volume-dependent, then borrowers/loans.
            await Copy<BookContributor>(MigrationTable.BookContributor);
            await Copy<BookCategory>(MigrationTable.BookCategory);
            current = MigrationTable.BookImage;
            await CopyImagesAsync(src, target, results, progress, ct);
            await Copy<BookVolume>(MigrationTable.BookVolume);
            await Copy<BookChapter>(MigrationTable.BookChapter);
            await Copy<Borrower>(MigrationTable.Borrower);
            await Copy<Loan>(MigrationTable.Loan);

            // ClientSession is intentionally NOT copied — it is live presence, not library data.

            progress?.Report(new MigrationProgress(MigrationPhase.Finalizing, null, 0, 0));
            await using (var resyncCtx = await target.CreateDbContextAsync(ct))
                await targetResync.ResyncAsync(resyncCtx, ct);

            progress?.Report(new MigrationProgress(MigrationPhase.Verifying, null, 0, 0));
            return new MigrationResult(MigrationOutcome.Completed, results, null, null);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Library migration failed at table {Table}", current);
            // Surface the innermost cause (e.g. the provider's constraint message) — the failure dialog needs it.
            var cause = ex;
            while (cause.InnerException is not null)
                cause = cause.InnerException;
            return new MigrationResult(MigrationOutcome.Failed, results, current, cause.Message);
        }
    }

    private static async Task CopyTableAsync<T>(
        MigrationTable table, BookDbContext src, IDbContextFactory<BookDbContext> target,
        List<MigrationTableResult> results, IProgress<MigrationProgress>? progress, CancellationToken ct)
        where T : class
    {
        var rows = await src.Set<T>().AsNoTracking().ToListAsync(ct);
        progress?.Report(new MigrationProgress(MigrationPhase.Copying, table, 0, rows.Count));
        DateTimeKindNormalizer.NormalizeToUtc(rows);

        for (int copied = 0; copied < rows.Count;)
        {
            var batch = rows.GetRange(copied, Math.Min(CopyBatchSize, rows.Count - copied));
            // A fresh context per batch keeps the change tracker (and tracked rows) from growing unbounded.
            await using (var dst = await target.CreateDbContextAsync(ct))
            {
                dst.Set<T>().AddRange(batch);
                ForceExplicitKeys(dst);
                await dst.SaveChangesAsync(ct);
            }

            copied += batch.Count;
            progress?.Report(new MigrationProgress(MigrationPhase.Copying, table, copied, rows.Count));
        }

        var targetCount = await CountAsync<T>(target, ct);
        results.Add(new MigrationTableResult(table, rows.Count, targetCount));
    }

    private static async Task CopyImagesAsync(
        BookDbContext src, IDbContextFactory<BookDbContext> target,
        List<MigrationTableResult> results, IProgress<MigrationProgress>? progress, CancellationToken ct)
    {
        var total = await src.Set<BookImage>().LongCountAsync(ct);
        progress?.Report(new MigrationProgress(MigrationPhase.Copying, MigrationTable.BookImage, 0, total));

        long copied = 0;
        for (int skip = 0; ; skip += ImageBatchSize)
        {
            var batch = await src.Set<BookImage>().AsNoTracking()
                .OrderBy(bi => bi.BookImageId).Skip(skip).Take(ImageBatchSize).ToListAsync(ct);
            if (batch.Count == 0)
                break;

            DateTimeKindNormalizer.NormalizeToUtc(batch);
            // A fresh context per batch lets the batch's BLOBs be reclaimed before the next read, keeping memory bounded.
            await using (var dst = await target.CreateDbContextAsync(ct))
            {
                dst.Set<BookImage>().AddRange(batch);
                ForceExplicitKeys(dst);
                await dst.SaveChangesAsync(ct);
            }

            copied += batch.Count;
            // Physical batches stay small (BLOB memory) but progress is reported per 100 to match the other tables.
            if (copied % CopyBatchSize == 0 || copied == total)
                progress?.Report(new MigrationProgress(MigrationPhase.Copying, MigrationTable.BookImage, copied, total));
        }

        var targetCount = await CountAsync<BookImage>(target, ct);
        results.Add(new MigrationTableResult(MigrationTable.BookImage, total, targetCount));
    }

    // Records source/target counts for a table that is not copied (a fixed schema-seeded enum), so a discrepancy
    // would still surface in verification even though the rows come from each database's own DDL seed.
    private static async Task VerifyOnlyAsync<T>(
        MigrationTable table, BookDbContext src, IDbContextFactory<BookDbContext> target,
        List<MigrationTableResult> results, IProgress<MigrationProgress>? progress, CancellationToken ct)
        where T : class
    {
        var sourceCount = await src.Set<T>().LongCountAsync(ct);
        var targetCount = await CountAsync<T>(target, ct);
        results.Add(new MigrationTableResult(table, sourceCount, targetCount));
        progress?.Report(new MigrationProgress(MigrationPhase.Copying, table, sourceCount, sourceCount));
    }

    private static async Task<long> CountAsync<T>(IDbContextFactory<BookDbContext> target, CancellationToken ct)
        where T : class
    {
        await using var ctx = await target.CreateDbContextAsync(ct);
        return await ctx.Set<T>().LongCountAsync(ct);
    }

    // The copy preserves primary keys verbatim. EF treats a key whose value is the CLR default (e.g. the
    // seeded BorrowerStatus/BookImageType rows with id 0) as unset and would generate a new one; marking every
    // primary-key property non-temporary forces the explicit value — including 0 — into the INSERT. Shared with
    // the CSV restore engine, which preserves keys the same way.
    internal static void ForceExplicitKeys(BookDbContext ctx)
    {
        foreach (var entry in ctx.ChangeTracker.Entries())
        {
            var key = entry.Metadata.FindPrimaryKey();
            if (key is null)
                continue;
            foreach (var property in key.Properties)
                entry.Property(property.Name).IsTemporary = false;
        }
    }

    // Delete order is the exact reverse of the copy order so every FK child is gone before its parent.
    private static async Task ClearTargetAsync(BookDbContext dst, CancellationToken ct)
    {
        await dst.Set<Loan>().ExecuteDeleteAsync(ct);
        await dst.Set<Borrower>().ExecuteDeleteAsync(ct);
        await dst.Set<BookChapter>().ExecuteDeleteAsync(ct);
        await dst.Set<BookVolume>().ExecuteDeleteAsync(ct);
        await dst.Set<BookImage>().ExecuteDeleteAsync(ct);
        await dst.Set<BookCategory>().ExecuteDeleteAsync(ct);
        await dst.Set<BookContributor>().ExecuteDeleteAsync(ct);
        await dst.Set<BatchQueueItem>().ExecuteDeleteAsync(ct);
        await dst.Set<SavedSearch>().ExecuteDeleteAsync(ct);
        await dst.Set<Settings>().ExecuteDeleteAsync(ct);
        await dst.Set<Book>().ExecuteDeleteAsync(ct);
        await dst.Set<CategoryCollection>().ExecuteDeleteAsync(ct);
        // BookImageType and BorrowerStatus are intentionally NOT cleared — they are fixed schema-seeded enums
        // (with id-0 rows EF can't re-insert) that stay as the target's own DDL seeds.
        await dst.Set<Collection>().ExecuteDeleteAsync(ct);
        await dst.Set<Person>().ExecuteDeleteAsync(ct);
        await dst.Set<ContributorRole>().ExecuteDeleteAsync(ct);
        await dst.Set<Publisher>().ExecuteDeleteAsync(ct);
        await dst.Set<Series>().ExecuteDeleteAsync(ct);
        await dst.Set<Category>().ExecuteDeleteAsync(ct);
        await dst.Set<Condition>().ExecuteDeleteAsync(ct);
        await dst.Set<Edition>().ExecuteDeleteAsync(ct);
        await dst.Set<Format>().ExecuteDeleteAsync(ct);
        await dst.Set<Language>().ExecuteDeleteAsync(ct);
        await dst.Set<Location>().ExecuteDeleteAsync(ct);
        await dst.Set<Owner>().ExecuteDeleteAsync(ct);
        await dst.Set<PurchasePlace>().ExecuteDeleteAsync(ct);
        await dst.Set<Rating>().ExecuteDeleteAsync(ct);
        await dst.Set<ReadingLevel>().ExecuteDeleteAsync(ct);
        await dst.Set<Source>().ExecuteDeleteAsync(ct);
        await dst.Set<Status>().ExecuteDeleteAsync(ct);
    }
}

/// <summary>
/// SQLite materialises DateTime as <see cref="DateTimeKind.Unspecified"/>; Postgres' <c>timestamp without time
/// zone</c> + the model's Utc value converter reject that. Every stored date is UTC (the SQLite DDL writes
/// <c>…Z</c>), so re-stamping Unspecified values as Utc before the copy is correct, not a hack.
/// </summary>
internal static class DateTimeKindNormalizer
{
    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> Cache = new();

    public static void NormalizeToUtc<T>(IEnumerable<T> rows)
    {
        var props = Cache.GetOrAdd(typeof(T), static t => t
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.CanWrite
                && (p.PropertyType == typeof(DateTime) || p.PropertyType == typeof(DateTime?)))
            .ToArray());
        if (props.Length == 0)
            return;

        foreach (var row in rows)
        {
            if (row is null)
                continue;
            foreach (var p in props)
            {
                if (p.GetValue(row) is DateTime { Kind: DateTimeKind.Unspecified } dt)
                    p.SetValue(row, DateTime.SpecifyKind(dt, DateTimeKind.Utc));
            }
        }
    }
}
