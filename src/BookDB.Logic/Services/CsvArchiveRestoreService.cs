using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BookDB.Data.DbContexts;
using BookDB.Data.Interfaces;
using BookDB.Models;
using BookDB.Models.Entities;
using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace BookDB.Logic.Services;

/// <summary>
/// Restores a CSV archive (the engine-neutral backup format) into the active backend. Takes a safety export of
/// the live database first, then truncates and re-imports inside a single transaction so a mid-restore failure
/// rolls back and leaves the live database untouched. Primary keys and identity sequences are preserved exactly
/// as the migration copy does.
/// </summary>
/// <summary>
/// Outcome of a CSV restore: the per-table data result plus the bootstrap config bundled in the archive (null if
/// the archive predates config bundling). The caller applies the preference keys and confirms the backend/connection
/// keys — the engine never auto-applies config.json.
/// </summary>
public sealed record RestoreResult(MigrationResult Data, BootstrapConfig? ArchivedConfig);

public interface ICsvArchiveRestoreService
{
    /// <param name="target">
    /// An alternative target to restore into (with its own resync and safety-backup service) — e.g. the backend the
    /// backup came from, taken from its config.json. When null the active backend (the engine's own services) is used.
    /// </param>
    /// <remarks>
    /// A restore always replaces: the target's catalog tables are emptied before import (after a safety export),
    /// because the archive preserves primary keys exactly, so importing on top of existing rows would collide.
    /// Combining libraries is the Import feature's job, not restore.
    /// </remarks>
    Task<RestoreResult> RestoreAsync(
        string archivePath, string safetyBackupFolder,
        IProgress<MigrationProgress>? progress = null, RestoreTargetServices? target = null,
        CancellationToken ct = default);
}

/// <summary>An explicit restore target's services (its context factory, identity resync, and safety-backup service).</summary>
public sealed record RestoreTargetServices(
    IDbContextFactory<BookDbContext> Factory, IIdentitySequenceResync Resync, IBackupService Backup);

/// <inheritdoc cref="ICsvArchiveRestoreService"/>
public sealed class CsvArchiveRestoreService : ICsvArchiveRestoreService
{
    private const int ImageBatchSize = 50;

    private readonly IDbContextFactory<BookDbContext> _factory;
    private readonly IIdentitySequenceResync _resync;
    private readonly IBackupService _backupService;

    public CsvArchiveRestoreService(
        IDbContextFactory<BookDbContext> factory, IIdentitySequenceResync resync, IBackupService backupService)
    {
        _factory = factory;
        _resync = resync;
        _backupService = backupService;
    }

    public async Task<RestoreResult> RestoreAsync(
        string archivePath, string safetyBackupFolder,
        IProgress<MigrationProgress>? progress = null, RestoreTargetServices? target = null,
        CancellationToken ct = default)
    {
        // Restore into the given target, or the active backend (the engine's own services) when none is supplied.
        var factory = target?.Factory ?? _factory;
        var resync = target?.Resync ?? _resync;
        var backup = target?.Backup ?? _backupService;

        // Capture the target database before touching it, so the restore can never lose its current data.
        progress?.Report(new MigrationProgress(MigrationPhase.Clearing, null, 0, 0));
        await backup.BackupCsvArchiveAsync(safetyBackupFolder, ct);

        var tempDir = Path.Combine(Path.GetTempPath(), $"bookdb_restore_{Guid.NewGuid():N}");
        var results = new List<MigrationTableResult>();
        MigrationTable? current = null;
        BootstrapConfig? archivedConfig = null;

        try
        {
            await Task.Run(() => ZipFile.ExtractToDirectory(archivePath, tempDir), ct);

            // The archive's config.json carries the backend/connection + preference keys; the caller decides what
            // to apply. Read it before the temp dir is cleaned up.
            var configPath = Path.Combine(tempDir, "config.json");
            if (File.Exists(configPath))
                archivedConfig = BootstrapConfig.Load(configPath);

            await using var ctx = await factory.CreateDbContextAsync(ct);

            // The whole truncate+import+resync runs as one retriable transactional unit — required because the
            // Postgres provider's retrying execution strategy forbids a free-standing user transaction. On a
            // transient failure the strategy re-runs the delegate from scratch (the rolled-back transaction left
            // the database untouched), so the per-table results and failure marker are reset each attempt.
            try
            {
                var strategy = ctx.Database.CreateExecutionStrategy();
                await strategy.ExecuteAsync(async () =>
                {
                    results.Clear();
                    current = null;
                    ctx.ChangeTracker.Clear();

                    await using var tx = await ctx.Database.BeginTransactionAsync(ct);

                    // A restore always replaces — the archive's preserved keys would collide with existing rows.
                    progress?.Report(new MigrationProgress(MigrationPhase.Clearing, null, 0, 0));
                    await ClearAsync(ctx, ct);

                    async Task Import<T>(MigrationTable table, string file) where T : class
                    {
                        current = table;
                        await ImportTableAsync<T>(table, tempDir, file, ctx, results, ct, progress);
                    }

                    // FK-safe order — lookups, then cross-reference, then core, book-dependent, volume, borrower/loan.
                    // BorrowerStatus and BookImageType (fixed schema-seeded enums with an id-0 row EF can't insert)
                    // and the Settings preference table (deferred to the restore-settings step) are not imported.
                    await Import<Collection>(MigrationTable.Collection, "Collections.csv");
                    await Import<Person>(MigrationTable.Person, "People.csv");
                    await Import<ContributorRole>(MigrationTable.ContributorRole, "ContributorRoles.csv");
                    await Import<Publisher>(MigrationTable.Publisher, "Publishers.csv");
                    await Import<Series>(MigrationTable.Series, "Series.csv");
                    await Import<Category>(MigrationTable.Category, "Categories.csv");
                    await Import<Condition>(MigrationTable.Condition, "Conditions.csv");
                    await Import<Edition>(MigrationTable.Edition, "Editions.csv");
                    await Import<Format>(MigrationTable.Format, "Formats.csv");
                    await Import<Language>(MigrationTable.Language, "Languages.csv");
                    await Import<Location>(MigrationTable.Location, "Locations.csv");
                    await Import<Owner>(MigrationTable.Owner, "Owners.csv");
                    await Import<PurchasePlace>(MigrationTable.PurchasePlace, "PurchasePlaces.csv");
                    await Import<Rating>(MigrationTable.Rating, "Ratings.csv");
                    await Import<ReadingLevel>(MigrationTable.ReadingLevel, "ReadingLevels.csv");
                    await Import<Source>(MigrationTable.Source, "Sources.csv");
                    await Import<Status>(MigrationTable.Status, "Statuses.csv");

                    await Import<CategoryCollection>(MigrationTable.CategoryCollection, "CategoryCollections.csv");
                    await Import<Book>(MigrationTable.Book, "Books.csv");
                    await Import<SavedSearch>(MigrationTable.SavedSearch, "SavedSearches.csv");
                    await Import<BatchQueueItem>(MigrationTable.BatchQueueItem, "BatchQueueItems.csv");

                    await Import<BookContributor>(MigrationTable.BookContributor, "BookContributors.csv");
                    await Import<BookCategory>(MigrationTable.BookCategory, "BookCategories.csv");
                    current = MigrationTable.BookImage;
                    await ImportImagesAsync(tempDir, ctx, results, ct, progress);
                    await Import<BookVolume>(MigrationTable.BookVolume, "BookVolumes.csv");
                    await Import<BookChapter>(MigrationTable.BookChapter, "BookChapters.csv");
                    await Import<Borrower>(MigrationTable.Borrower, "Borrowers.csv");
                    await Import<Loan>(MigrationTable.Loan, "Loans.csv");

                    // Settings is upserted (not truncated): only user-preference rows are applied, so the live
                    // machine-specific rows (window geometry, column layout, local paths) survive the restore.
                    current = MigrationTable.Settings;
                    await ImportSettingsAsync(tempDir, ctx, ct, progress);

                    progress?.Report(new MigrationProgress(MigrationPhase.Finalizing, null, 0, 0));
                    await resync.ResyncAsync(ctx, ct);

                    progress?.Report(new MigrationProgress(MigrationPhase.Verifying, null, 0, 0));
                    await tx.CommitAsync(ct);
                });

                return new RestoreResult(new MigrationResult(MigrationOutcome.Completed, results, null, null), archivedConfig);
            }
            catch (Exception ex)
            {
                // The transaction was rolled back by its dispose; the live database is untouched.
                Log.Error(ex, "CsvArchiveRestoreService: restore failed at {Table} — rolled back", current);
                var cause = ex;
                while (cause.InnerException is not null)
                    cause = cause.InnerException;
                return new RestoreResult(new MigrationResult(MigrationOutcome.Failed, results, current, cause.Message), archivedConfig);
            }
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, recursive: true); }
                catch (Exception ex) { Log.Error(ex, "CsvArchiveRestoreService: failed to delete temp dir {TempDir}", tempDir); }
            }
        }
    }

    private static async Task ImportTableAsync<T>(
        MigrationTable table, string dir, string file, BookDbContext ctx,
        List<MigrationTableResult> results, CancellationToken ct, IProgress<MigrationProgress>? progress)
        where T : class
    {
        var rows = ReadCsv<T>(dir, file);
        progress?.Report(new MigrationProgress(MigrationPhase.Copying, table, 0, rows.Count));

        if (rows.Count > 0)
        {
            DateTimeKindNormalizer.NormalizeToUtc(rows);
            ctx.Set<T>().AddRange(rows);
            LibraryMigrationService.ForceExplicitKeys(ctx);
            await ctx.SaveChangesAsync(ct);
            ctx.ChangeTracker.Clear();
        }

        var restored = await ctx.Set<T>().LongCountAsync(ct);
        results.Add(new MigrationTableResult(table, rows.Count, restored));
        progress?.Report(new MigrationProgress(MigrationPhase.Copying, table, rows.Count, rows.Count));
    }

    private static async Task ImportImagesAsync(
        string dir, BookDbContext ctx, List<MigrationTableResult> results,
        CancellationToken ct, IProgress<MigrationProgress>? progress)
    {
        var rows = ReadCsv<BookImage>(dir, "BookImages.csv");
        var imagesDir = Path.Combine(dir, "images");
        progress?.Report(new MigrationProgress(MigrationPhase.Copying, MigrationTable.BookImage, 0, rows.Count));

        long done = 0;
        foreach (var batch in rows.Chunk(ImageBatchSize))
        {
            foreach (var image in batch)
            {
                var bytesPath = Path.Combine(imagesDir, $"{image.BookImageId}.jpg");
                image.ImageData = File.Exists(bytesPath) ? await File.ReadAllBytesAsync(bytesPath, ct) : [];
            }
            DateTimeKindNormalizer.NormalizeToUtc(batch);
            ctx.Set<BookImage>().AddRange(batch);
            LibraryMigrationService.ForceExplicitKeys(ctx);
            await ctx.SaveChangesAsync(ct);
            ctx.ChangeTracker.Clear();

            done += batch.Length;
            progress?.Report(new MigrationProgress(MigrationPhase.Copying, MigrationTable.BookImage, done, rows.Count));
        }

        var restored = await ctx.Set<BookImage>().LongCountAsync(ct);
        results.Add(new MigrationTableResult(MigrationTable.BookImage, rows.Count, restored));
    }

    // Upserts only the user-preference Settings rows: machine-specific keys (geometry, column layout,
    // local paths) in the archive are skipped, and the live Settings table is never truncated, so the current
    // machine's layout and paths survive. Not added to the verified-count results — it is a selective merge.
    private static async Task ImportSettingsAsync(
        string dir, BookDbContext ctx, CancellationToken ct, IProgress<MigrationProgress>? progress)
    {
        var rows = ReadCsv<Settings>(dir, "Settings.csv")
            .Where(s => RestoreSettingsClassifier.ShouldApply(s.Key))
            .ToList();
        progress?.Report(new MigrationProgress(MigrationPhase.Copying, MigrationTable.Settings, 0, rows.Count));
        if (rows.Count == 0)
            return;

        var keys = rows.Select(s => s.Key).ToList();
        var existing = await ctx.Settings.Where(s => keys.Contains(s.Key)).ToDictionaryAsync(s => s.Key, ct);
        foreach (var row in rows)
        {
            if (existing.TryGetValue(row.Key, out var live))
                live.Value = row.Value;
            else
                ctx.Settings.Add(row);
        }
        await ctx.SaveChangesAsync(ct);
        ctx.ChangeTracker.Clear();
        progress?.Report(new MigrationProgress(MigrationPhase.Copying, MigrationTable.Settings, rows.Count, rows.Count));
    }

    // Reverse of the import order; the fixed enum tables, the Settings table, and ClientSession are left intact.
    private static async Task ClearAsync(BookDbContext ctx, CancellationToken ct)
    {
        await ctx.Set<Loan>().ExecuteDeleteAsync(ct);
        await ctx.Set<Borrower>().ExecuteDeleteAsync(ct);
        await ctx.Set<BookChapter>().ExecuteDeleteAsync(ct);
        await ctx.Set<BookVolume>().ExecuteDeleteAsync(ct);
        await ctx.Set<BookImage>().ExecuteDeleteAsync(ct);
        await ctx.Set<BookCategory>().ExecuteDeleteAsync(ct);
        await ctx.Set<BookContributor>().ExecuteDeleteAsync(ct);
        await ctx.Set<BatchQueueItem>().ExecuteDeleteAsync(ct);
        await ctx.Set<SavedSearch>().ExecuteDeleteAsync(ct);
        await ctx.Set<Book>().ExecuteDeleteAsync(ct);
        await ctx.Set<CategoryCollection>().ExecuteDeleteAsync(ct);
        await ctx.Set<Collection>().ExecuteDeleteAsync(ct);
        await ctx.Set<Person>().ExecuteDeleteAsync(ct);
        await ctx.Set<ContributorRole>().ExecuteDeleteAsync(ct);
        await ctx.Set<Publisher>().ExecuteDeleteAsync(ct);
        await ctx.Set<Series>().ExecuteDeleteAsync(ct);
        await ctx.Set<Category>().ExecuteDeleteAsync(ct);
        await ctx.Set<Condition>().ExecuteDeleteAsync(ct);
        await ctx.Set<Edition>().ExecuteDeleteAsync(ct);
        await ctx.Set<Format>().ExecuteDeleteAsync(ct);
        await ctx.Set<Language>().ExecuteDeleteAsync(ct);
        await ctx.Set<Location>().ExecuteDeleteAsync(ct);
        await ctx.Set<Owner>().ExecuteDeleteAsync(ct);
        await ctx.Set<PurchasePlace>().ExecuteDeleteAsync(ct);
        await ctx.Set<Rating>().ExecuteDeleteAsync(ct);
        await ctx.Set<ReadingLevel>().ExecuteDeleteAsync(ct);
        await ctx.Set<Source>().ExecuteDeleteAsync(ct);
        await ctx.Set<Status>().ExecuteDeleteAsync(ct);
    }

    private static List<T> ReadCsv<T>(string dir, string file) where T : class
    {
        var path = Path.Combine(dir, file);
        if (!File.Exists(path))
            return [];

        using var reader = new StreamReader(path, Encoding.UTF8);
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HeaderValidated = null,   // the archive carries extra navigation columns we intentionally ignore
            MissingFieldFound = null,
        };
        using var csv = new CsvReader(reader, config);
        csv.Context.RegisterClassMap(new ScalarOnlyClassMap<T>());
        return csv.GetRecords<T>().ToList();
    }
}

/// <summary>
/// Maps only the scalar columns of an entity, by name. The CSV archive was written with CsvHelper's auto-map,
/// which also emits navigation/reference columns; binding those back would wrongly materialise related entities,
/// so they are left unmapped (ignored on read). Cover-image bytes are loaded from the archive's images folder, not the CSV.
/// </summary>
public sealed class ScalarOnlyClassMap<T> : ClassMap<T>
{
    public ScalarOnlyClassMap()
    {
        foreach (var property in typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.GetIndexParameters().Length > 0 || !property.CanRead || !property.CanWrite)
                continue;
            if (IsScalar(property.PropertyType))
                Map(typeof(T), property);
        }
    }

    private static bool IsScalar(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        return type.IsPrimitive || type.IsEnum
            || type == typeof(string) || type == typeof(decimal)
            || type == typeof(DateTime) || type == typeof(DateTimeOffset) || type == typeof(Guid);
    }
}
