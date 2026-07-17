using System;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BookDB.Data.DbContexts;
using BookDB.Data.Interfaces;
using BookDB.Models;
using BookDB.Models.Interfaces;
using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace BookDB.Logic.Services;

public sealed class BackupService : IBackupService
{
    // Setting key holding the ISO-8601 UTC timestamp of the last successful backup.
    private const string LastRunKey = "AutoBackup.LastRun";
    // Auto-backup runs at least this often even when nothing changed (the user's recency threshold).
    private static readonly TimeSpan RecencyThreshold = TimeSpan.FromDays(7);

    // Cover-image rows carry the BLOBs; exporting them in small batches keeps process memory bounded on large
    // catalogs (mirrors the move/restore engines). Loading every image at once would hold the whole image set —
    // each cover > 85 KB lands on the Large Object Heap — resident simultaneously.
    private const int ImageBatchSize = 50;

    private readonly IDbContextFactory<BookDbContext> _factory;
    private readonly AppSettings _appSettings;
    private readonly ISettingsService _settingsService;
    private readonly IDataChangeTracker _changeTracker;
    private readonly IBackupStrategy _backupStrategy;

    public BackupService(
        IDbContextFactory<BookDbContext> factory,
        AppSettings appSettings,
        ISettingsService settingsService,
        IDataChangeTracker changeTracker,
        IBackupStrategy backupStrategy)
    {
        _factory = factory;
        _appSettings = appSettings;
        _settingsService = settingsService;
        _changeTracker = changeTracker;
        _backupStrategy = backupStrategy;
    }

    public bool SupportsFileBackup => _backupStrategy.SupportsFileBackup;

    // config.json is bundled into every backup so a restore can carry the backend and preferences;
    // guarded in case a backup runs before the file has been written.
    private string? ExistingConfigPath =>
        !string.IsNullOrEmpty(_appSettings.ConfigPath) && File.Exists(_appSettings.ConfigPath)
            ? _appSettings.ConfigPath
            : null;

    public string GetCandidateSqlitePath(string destFolder)
    {
        var date = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return Path.Combine(Path.GetFullPath(destFolder), $"bookdb-{date}.zip");
    }

    public string GetCandidateCsvArchivePath(string destFolder)
    {
        var date = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return Path.Combine(Path.GetFullPath(destFolder), $"bookdb-csv-{date}.zip");
    }

    public async Task<string> BackupSqliteAsync(string destFolder, CancellationToken ct = default, string? explicitFileName = null, IProgress<ProgressUpdate<BackupProgressStep>>? progress = null)
    {
        destFolder = Path.GetFullPath(destFolder);
        // Resolve the file name here (provider-neutral naming/collision logic) and let the SQLite strategy do
        // the engine-specific work — the WAL flush, the file copy, and the zip. The strategy reports the same
        // typed progress steps, so its updates flow straight through.
        var fileName = explicitFileName ?? Path.GetFileName(ResolvePath(GetCandidateSqlitePath(destFolder)));

        var zipPath = await _backupStrategy.BackupAsync(destFolder, ct, fileName, progress);
        await RecordBackupCompletedAsync(ct);
        return zipPath;
    }

    public async Task<string> BackupCsvArchiveAsync(string destFolder, CancellationToken ct = default, string? explicitFileName = null, IProgress<ProgressUpdate<BackupProgressStep>>? progress = null)
    {
        destFolder = Path.GetFullPath(destFolder);

        var zipPath = explicitFileName != null
            ? Path.Combine(destFolder, explicitFileName)
            : ResolvePath(GetCandidateCsvArchivePath(destFolder));

        var tempDir = Path.Combine(Path.GetTempPath(), $"bookdb_csvarchive_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            await using var dbContext = await _factory.CreateDbContextAsync(ct);

            progress?.Report(new ProgressUpdate<BackupProgressStep>(BackupProgressStep.ExportingBooks));
            await WriteCsvAsync(tempDir, "Books.csv",
                await dbContext.Books.AsNoTracking().ToListAsync(ct));

            progress?.Report(new ProgressUpdate<BackupProgressStep>(BackupProgressStep.ExportingPeople));
            await WriteCsvAsync(tempDir, "People.csv",
                await dbContext.People.AsNoTracking().ToListAsync(ct));

            progress?.Report(new ProgressUpdate<BackupProgressStep>(BackupProgressStep.ExportingPublishers));
            await WriteCsvAsync(tempDir, "Publishers.csv",
                await dbContext.Publishers.AsNoTracking().ToListAsync(ct));

            progress?.Report(new ProgressUpdate<BackupProgressStep>(BackupProgressStep.ExportingSeries));
            await WriteCsvAsync(tempDir, "Series.csv",
                await dbContext.Series.AsNoTracking().ToListAsync(ct));

            progress?.Report(new ProgressUpdate<BackupProgressStep>(BackupProgressStep.ExportingCollections));
            await WriteCsvAsync(tempDir, "Collections.csv",
                await dbContext.Collections.AsNoTracking().ToListAsync(ct));

            progress?.Report(new ProgressUpdate<BackupProgressStep>(BackupProgressStep.ExportingCategories));
            await WriteCsvAsync(tempDir, "Categories.csv",
                await dbContext.Categories.AsNoTracking().ToListAsync(ct));

            progress?.Report(new ProgressUpdate<BackupProgressStep>(BackupProgressStep.ExportingFormats));
            await WriteCsvAsync(tempDir, "Formats.csv",
                await dbContext.Formats.AsNoTracking().ToListAsync(ct));

            progress?.Report(new ProgressUpdate<BackupProgressStep>(BackupProgressStep.ExportingLanguages));
            await WriteCsvAsync(tempDir, "Languages.csv",
                await dbContext.Languages.AsNoTracking().ToListAsync(ct));

            progress?.Report(new ProgressUpdate<BackupProgressStep>(BackupProgressStep.ExportingLocations));
            await WriteCsvAsync(tempDir, "Locations.csv",
                await dbContext.Locations.AsNoTracking().ToListAsync(ct));

            progress?.Report(new ProgressUpdate<BackupProgressStep>(BackupProgressStep.ExportingOwners));
            await WriteCsvAsync(tempDir, "Owners.csv",
                await dbContext.Owners.AsNoTracking().ToListAsync(ct));

            progress?.Report(new ProgressUpdate<BackupProgressStep>(BackupProgressStep.ExportingRelationships));
            await WriteCsvAsync(tempDir, "BookContributors.csv",
                await dbContext.BookContributors.AsNoTracking().ToListAsync(ct));
            await WriteCsvAsync(tempDir, "BookCategories.csv",
                await dbContext.BookCategories.AsNoTracking().ToListAsync(ct));
            await WriteCsvAsync(tempDir, "CategoryCollections.csv",
                await dbContext.CategoryCollections.AsNoTracking().ToListAsync(ct));
            await WriteCsvAsync(tempDir, "BookVolumes.csv",
                await dbContext.BookVolumes.AsNoTracking().ToListAsync(ct));
            await WriteCsvAsync(tempDir, "BookChapters.csv",
                await dbContext.BookChapters.AsNoTracking().ToListAsync(ct));

            progress?.Report(new ProgressUpdate<BackupProgressStep>(BackupProgressStep.ExportingLookups));
            await WriteCsvAsync(tempDir, "Conditions.csv",
                await dbContext.Conditions.AsNoTracking().ToListAsync(ct));
            await WriteCsvAsync(tempDir, "ContributorRoles.csv",
                await dbContext.ContributorRoles.AsNoTracking().ToListAsync(ct));
            await WriteCsvAsync(tempDir, "Editions.csv",
                await dbContext.Editions.AsNoTracking().ToListAsync(ct));
            await WriteCsvAsync(tempDir, "PurchasePlaces.csv",
                await dbContext.PurchasePlaces.AsNoTracking().ToListAsync(ct));
            await WriteCsvAsync(tempDir, "Ratings.csv",
                await dbContext.Ratings.AsNoTracking().ToListAsync(ct));
            await WriteCsvAsync(tempDir, "ReadingLevels.csv",
                await dbContext.ReadingLevels.AsNoTracking().ToListAsync(ct));
            await WriteCsvAsync(tempDir, "Sources.csv",
                await dbContext.Sources.AsNoTracking().ToListAsync(ct));
            await WriteCsvAsync(tempDir, "Statuses.csv",
                await dbContext.Statuses.AsNoTracking().ToListAsync(ct));
            await WriteCsvAsync(tempDir, "BookImageTypes.csv",
                await dbContext.BookImageTypes.AsNoTracking().ToListAsync(ct));
            await WriteCsvAsync(tempDir, "BorrowerStatuses.csv",
                await dbContext.BorrowerStatuses.AsNoTracking().ToListAsync(ct));

            progress?.Report(new ProgressUpdate<BackupProgressStep>(BackupProgressStep.ExportingLoans));
            await WriteCsvAsync(tempDir, "Borrowers.csv",
                await dbContext.Borrowers.AsNoTracking().ToListAsync(ct));
            await WriteCsvAsync(tempDir, "Loans.csv",
                await dbContext.Loans.AsNoTracking().ToListAsync(ct));

            progress?.Report(new ProgressUpdate<BackupProgressStep>(BackupProgressStep.ExportingSettings));
            await WriteCsvAsync(tempDir, "Settings.csv",
                await dbContext.Settings.AsNoTracking().ToListAsync(ct));
            await WriteCsvAsync(tempDir, "SavedSearches.csv",
                await dbContext.SavedSearches.AsNoTracking().ToListAsync(ct));
            await WriteCsvAsync(tempDir, "BatchQueueItems.csv",
                await dbContext.BatchQueueItems.AsNoTracking().ToListAsync(ct));

            progress?.Report(new ProgressUpdate<BackupProgressStep>(BackupProgressStep.ExportingCoverImages));
            var imagesDir = Path.Combine(tempDir, "images");
            Directory.CreateDirectory(imagesDir);

            // Image metadata (book linkage, type, ordering) — projected without ImageData so the BLOBs aren't
            // pulled here; the bytes are streamed to images/ in batches below.
            var imageMeta = await dbContext.BookImages.AsNoTracking()
                .OrderBy(i => i.BookImageId)
                .Select(i => new
                {
                    i.BookImageId,
                    i.BookId,
                    i.BookImageTypeId,
                    i.MimeType,
                    i.IsPrimary,
                    i.DisplayOrder,
                    i.Added,
                })
                .ToListAsync(ct);
            await WriteCsvAsync(tempDir, "BookImages.csv", imageMeta);

            // Stream the cover bytes in batches so only ImageBatchSize BLOBs are resident at once; a fresh query
            // per batch lets each batch's byte arrays be reclaimed before the next read.
            var exported = 0;
            for (int skip = 0; ; skip += ImageBatchSize)
            {
                var batch = await dbContext.BookImages.AsNoTracking()
                    .OrderBy(i => i.BookImageId).Skip(skip).Take(ImageBatchSize)
                    .Select(i => new { i.BookImageId, i.ImageData })
                    .ToListAsync(ct);
                if (batch.Count == 0)
                    break;

                foreach (var image in batch)
                {
                    if (image.ImageData is { Length: > 0 })
                        await File.WriteAllBytesAsync(
                            Path.Combine(imagesDir, $"{image.BookImageId}.jpg"), image.ImageData, ct);
                }

                exported += batch.Count;
                progress?.Report(new ProgressUpdate<BackupProgressStep>(
                    BackupProgressStep.ExportingCoverImagesCount, exported, imageMeta.Count));
            }

            if (ExistingConfigPath is { } configPath)
                File.Copy(configPath, Path.Combine(tempDir, "config.json"));

            progress?.Report(new ProgressUpdate<BackupProgressStep>(BackupProgressStep.CreatingArchive));
            ZipFile.CreateFromDirectory(tempDir, zipPath);

            Log.Information("BackupService: CSV archive backup written to {ZipPath}", zipPath);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, recursive: true); }
                catch (Exception ex) { Log.Error(ex, "BackupService: failed to delete temp dir {TempDir}", tempDir); }
            }
        }

        await RecordBackupCompletedAsync(ct);
        return zipPath;
    }

    public async Task RestoreAsync(string backupZipPath, string safetyBackupFolder, CancellationToken ct = default, IProgress<ProgressUpdate<BackupProgressStep>>? progress = null)
    {
        if (string.IsNullOrWhiteSpace(safetyBackupFolder))
            throw new ArgumentNullException(nameof(safetyBackupFolder), "Safety backup is required before restore.");

        backupZipPath = Path.GetFullPath(backupZipPath);
        safetyBackupFolder = Path.GetFullPath(safetyBackupFolder);

        progress?.Report(new ProgressUpdate<BackupProgressStep>(BackupProgressStep.SavingSafetyBackup));
        var date = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var safetyPath = ResolvePath(Path.Combine(safetyBackupFolder, $"bookdb-safety-{date}.zip"));
        await BackupSqliteAsync(safetyBackupFolder, ct, explicitFileName: Path.GetFileName(safetyPath));

        progress?.Report(new ProgressUpdate<BackupProgressStep>(BackupProgressStep.ExtractingArchive));
        var tempDir = Path.Combine(Path.GetTempPath(), $"bookdb_restore_{Guid.NewGuid():N}");
        try
        {
            await Task.Run(() => ZipFile.ExtractToDirectory(backupZipPath, tempDir), ct);

            var extractedDb = Path.Combine(tempDir, "library.db");
            if (!File.Exists(extractedDb))
                throw new InvalidOperationException("The backup zip does not contain library.db.");

            progress?.Report(new ProgressUpdate<BackupProgressStep>(BackupProgressStep.ReplacingLibrary));
            await _backupStrategy.RestoreFileAsync(extractedDb, ct);

            Log.Information("BackupService: Restore complete — active library replaced from {BackupZipPath}", backupZipPath);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, recursive: true); }
                catch (Exception ex) { Log.Error(ex, "BackupService: failed to delete temp dir {TempDir}", tempDir); }
            }
        }
    }

    public async Task<bool> IsAutoBackupEnabledAsync(CancellationToken ct = default)
    {
        var enabled = await _settingsService.GetAsync("AutoBackup.Enabled", ct);
        if (enabled != "true")
            return false;

        var folder = await _settingsService.GetAsync("LastBackupFolder", ct);
        return !string.IsNullOrWhiteSpace(folder);
    }

    public async Task<bool> ShouldAutoBackupAsync(CancellationToken ct = default)
    {
        if (!await IsAutoBackupEnabledAsync(ct))
            return false;

        // Library data changed this session — always back up.
        if (_changeTracker.HasChanges)
            return true;

        var lastRunRaw = await _settingsService.GetAsync(LastRunKey, ct);
        if (string.IsNullOrWhiteSpace(lastRunRaw))
            return true; // never backed up

        if (!DateTime.TryParse(lastRunRaw, CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out var lastRun))
            return true; // unparseable timestamp — treat as stale

        return DateTime.UtcNow - lastRun.ToUniversalTime() > RecencyThreshold;
    }

    public async Task AutoBackupIfEnabledAsync(CancellationToken ct = default, IProgress<ProgressUpdate<BackupProgressStep>>? progress = null)
    {
        if (!await ShouldAutoBackupAsync(ct))
            return;

        var folder = await _settingsService.GetAsync("LastBackupFolder", ct);
        if (string.IsNullOrWhiteSpace(folder))
            return;

        var format = await _settingsService.GetAsync("AutoBackup.Format", ct) ?? "SQLite";

        try
        {
            // The SQLite file format is only available when the active backend supports a file backup;
            // a remote backend always falls back to the engine-neutral CSV archive.
            if (format == "CsvArchive" || !_backupStrategy.SupportsFileBackup)
                await BackupCsvArchiveAsync(folder, ct, progress: progress);
            else
                await BackupSqliteAsync(folder, ct, progress: progress);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "BackupService: AutoBackupIfEnabledAsync failed");
        }
    }

    // Records that a backup just succeeded: stamps the last-run time and clears the change flag so the next
    // shutdown only backs up again once data changes or the recency window lapses. Covers manual and auto
    // backups alike. The Settings write is excluded from the change tracker, so it won't re-flag the session.
    private async Task RecordBackupCompletedAsync(CancellationToken ct)
    {
        await _settingsService.SetAsync(
            LastRunKey, DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture), ct);
        _changeTracker.Reset();
    }

    // Generates a suffix path if the candidate already exists: bookdb-2026-04-19-1.zip, -2.zip, etc.
    private static string ResolvePath(string candidatePath)
    {
        if (!File.Exists(candidatePath))
            return candidatePath;

        var dir = Path.GetDirectoryName(candidatePath)!;
        var nameNoExt = Path.GetFileNameWithoutExtension(candidatePath);
        var ext = Path.GetExtension(candidatePath);
        for (var i = 1; i < 1000; i++)
        {
            var suffixed = Path.Combine(dir, $"{nameNoExt}-{i}{ext}");
            if (!File.Exists(suffixed))
                return suffixed;
        }
        return candidatePath; // fallback: overwrite
    }

    private static async Task WriteCsvAsync<T>(string folder, string fileName, System.Collections.Generic.IEnumerable<T> records)
    {
        var path = Path.Combine(folder, fileName);
        await using var writer = new StreamWriter(path, false, System.Text.Encoding.UTF8);
        await using var csv = new CsvWriter(writer, new CsvConfiguration(System.Globalization.CultureInfo.InvariantCulture));
        csv.WriteRecords(records);
    }
}
