using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using BookDB.Data.DbContexts;
using BookDB.Desktop.Services;
using BookDB.Desktop.ViewModels;
using BookDB.Help;
using BookDB.Logic.Services;
using BookDB.Models;
using BookDB.Models.Metadata;
using BookDB.Models.Entities;
using BookDB.Models.Interfaces;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System.Threading;

namespace BookDB.Desktop.Tests.Helpers;

public sealed class TestLookupServiceFactory : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly TestDbContextFactory _factory;

    public ILookupService LookupService { get; }
    public BookService BookService { get; }
    public BookSearchService BookSearchService { get; }
    public BookImageService BookImageService { get; }
    public BookMetadataService BookMetadataService { get; }
    public IDbContextFactory<BookDbContext> DbContextFactory => _factory;

    public TestLookupServiceFactory()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<BookDbContext>()
            .UseSqlite(_connection)
            .Options;

        _factory = new TestDbContextFactory(options);

        using (var db = new BookDbContext(options))
        {
            db.Database.EnsureCreated();
        }

        LookupService = new LookupService(_factory);
        BookService = new BookService(_factory);
        BookSearchService = new BookSearchService(_factory, new BookDB.Data.Sqlite.SqliteBookSearchProvider(_factory));
        BookImageService = new BookImageService(_factory);
        BookMetadataService = new BookMetadataService(_factory);
    }

    public async Task SeedCollectionsAsync(params (int id, string name, int sortOrder)[] collections)
    {
        using var db = new BookDbContext(_factory.Options);
        foreach (var (id, name, sortOrder) in collections)
        {
            db.Set<Collection>().Add(new Collection
            {
                CollectionId = id,
                Name = name,
                SortOrder = sortOrder
            });
        }
        await db.SaveChangesAsync();
    }

    public void Dispose()
    {
        _connection.Dispose();
    }

    public sealed class NullWindowService : IWindowService
    {
        public Task<bool?> ShowAddBookDialogAsync(int? defaultCollectionId = null, string? prefillIsbn = null) => Task.FromResult<bool?>(null);
        public Task<bool?> ShowBulkEditDialogAsync(IReadOnlyList<int> bookIds) => Task.FromResult<bool?>(null);
        public Task<bool?> ShowAdvancedSearchDialogAsync(SavedSearch? searchToEdit = null) => Task.FromResult<bool?>(null);
        public Task<UnsavedChangesResult> ShowUnsavedChangesDialogAsync(string bookTitle) => Task.FromResult(UnsavedChangesResult.Discard);
        public Task<bool?> ShowDeleteConfirmationAsync(string message) => Task.FromResult<bool?>(null);
        public Task OpenFullDetailsWindowAsync(int bookId) => Task.CompletedTask;
        public Task<bool?> ShowLookupWizardDialogAsync() => Task.FromResult<bool?>(null);
        public Task<bool?> ShowMergeReviewDialogAsync(
            IReadOnlyList<BookMetadata> sources,
            BookMetadata? currentBook,
            IReadOnlyList<CoverOption> coverOptions,
            int? existingBookId,
            int? collectionId,
            Window? ownerWindow = null) => Task.FromResult<bool?>(null);
        public Task<DuplicateIsbnResult> ShowDuplicateIsbnDialogAsync(string isbn, string existingTitle) => Task.FromResult(DuplicateIsbnResult.Cancel);
        public void OpenBatchQueueWindow() { }
        public BatchQueueWindowViewModel? GetBatchQueueWindowViewModel() => null;
        public Task<string?> ShowIsbnPromptDialogAsync(string bookTitle) => Task.FromResult<string?>(null);
        public Task<bool?> ShowImportWizardAsync(string? initialPath = null) => Task.FromResult<bool?>(null);
        public Task ShowReaderwareDbImportDialogAsync() => Task.CompletedTask;
        public Task<bool?> ShowBatchShutdownWarningAsync() => Task.FromResult<bool?>(null);
        public Task<bool?> ShowMainShutdownWarningAsync() => Task.FromResult<bool?>(null);
        public Task StartBatchAsync(IReadOnlyList<string> isbns) => Task.CompletedTask;
        public Task StartBatchRecatalogAsync(IReadOnlyList<int> bookIds) => Task.CompletedTask;
        public Task StartBatchRecatalogAsync(int bookId, string isbn) => Task.CompletedTask;
        public void CloseAllSecondaryWindows() { }
        public Task<bool> ConfirmCloseGuardedWindowsAsync() => Task.FromResult(true);
        public Task ShowManageLookupsAsync(string? initialTab = null) => Task.CompletedTask;
        public Task ShowSettingsAsync(Avalonia.Controls.Window? owner = null) => Task.CompletedTask;
        public Task ShowMaintenanceDialogAsync() => Task.CompletedTask;
        public Task OpenStatisticsWindowAsync() => Task.CompletedTask;
        public void OpenHelpWindow(HelpTab tab) { }
        public Task<IReadOnlyList<string>?> ShowCsvColumnPickerAsync(
            IReadOnlyList<string> allColumns,
            IReadOnlyList<string> defaultSelected) => Task.FromResult<IReadOnlyList<string>?>(null);
        public Task<int?> ShowMergeTargetPickerAsync(
            string sourceName,
            int sourceId,
            IReadOnlyList<LookupEntryRow> candidates,
            Window? owner = null) => Task.FromResult<int?>(null);
        public Task<PrintParameters?> ShowPrintDialogAsync(
            IReadOnlySet<int>? collectionIds,
            IReadOnlyList<int>? searchBookIds,
            Dictionary<string, HashSet<int>>? facetFilters,
            string? sortColumn,
            bool sortAscending,
            int bookCount = 0) => Task.FromResult<PrintParameters?>(null);
        public Task<bool?> ShowCheckOutDialogAsync(int bookId) => Task.FromResult<bool?>(null);
        public Task ShowManageBorrowersAsync() => Task.CompletedTask;
        public Task<(string Format, string Folder)?> ShowBackupFormatDialogAsync(bool supportsFileBackup, string configDefault, string defaultFolder) => Task.FromResult<(string, string)?>(null);
        public Task<bool> ShowConnectDialogAsync(Avalonia.Controls.Window owner) => Task.FromResult(true);
        public Task<StartupFailureOutcome> ShowStartupFailureDialogAsync(
            BookDB.Data.Interfaces.ConnectionProbeResult initialResult,
            System.Func<System.Threading.CancellationToken, Task<BookDB.Data.Interfaces.ConnectionProbeResult>> connect,
            Avalonia.Controls.Window owner) => Task.FromResult(StartupFailureOutcome.Proceed);
        public Task<WriteFailureChoice> ShowWriteFailureDialogAsync(string message) => Task.FromResult(WriteFailureChoice.Discard);
        public Task<bool> ShowConnectionLostEscalationDialogAsync() => Task.FromResult(false);
        public Task ShowAboutAsync() => Task.CompletedTask;
        public Task ShowReleaseNotesAsync(string version, string markdown) => Task.CompletedTask;
        public Task<ReleaseNotesChoice> ShowReleaseNotesPromptAsync(string version) => Task.FromResult(ReleaseNotesChoice.Defer);
        public Task<RestoreTargetChoice> ShowRestoreTargetAsync(string archivedServerDescription) => Task.FromResult(RestoreTargetChoice.Cancel);
        public Task ShowInfoAsync(string body, string? title = null) => Task.CompletedTask;
        public Task<bool?> ShowConfirmAsync(string title, string body, Window? owner = null) => Task.FromResult<bool?>(null);
        public Task<BackupConflictChoice> ShowBackupConflictAsync(string existingPath) => Task.FromResult(BackupConflictChoice.Cancel);
        public Task<BookDB.Logic.Import.ImportDuplicateResolution> ShowDuplicateResolutionAsync(string title, string body) => Task.FromResult(BookDB.Logic.Import.ImportDuplicateResolution.Skip);
        public IProgressWindowHandle ShowProgressWindow(string header) => new NullProgressHandle();
        public IProgressWindowHandle ShowBackupProgressWindow() => new NullProgressHandle();

        private sealed class NullProgressHandle : IProgressWindowHandle
        {
            public void Report(string value) { }
            public void Close() { }
        }
    }

    public sealed class NullFilePickerService : IFilePickerService
    {
        public Task<string?> PickFileAsync(string title, IReadOnlyList<string> extensions) => Task.FromResult<string?>(null);
        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);
        public Task<string?> SaveFileAsync(string title, string suggestedName, IReadOnlyList<string> extensions) => Task.FromResult<string?>(null);
    }

    public sealed class NullFileSystemService : IFileSystemService
    {
        public void EnsureDirectoryExists(string path) { }
        public string CombinePath(params string[] parts) => System.IO.Path.Combine(parts);
        public bool FileExists(string path) => false;
        public void DeleteFile(string path) { }
        public Task CopyFileAsync(string source, string destination, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    public sealed class NullBackupService : IBackupService
    {
        public Task<string> BackupSqliteAsync(string destFolder, CancellationToken ct = default, string? explicitFileName = null, IProgress<ProgressUpdate<BackupProgressStep>>? progress = null) => Task.FromResult(string.Empty);
        public Task<string> BackupCsvArchiveAsync(string destFolder, CancellationToken ct = default, string? explicitFileName = null, IProgress<ProgressUpdate<BackupProgressStep>>? progress = null) => Task.FromResult(string.Empty);
        public bool SupportsFileBackup => true;
        public Task RestoreAsync(string backupZipPath, string safetyBackupPath, CancellationToken ct = default, IProgress<ProgressUpdate<BackupProgressStep>>? progress = null) => Task.CompletedTask;
        public Task AutoBackupIfEnabledAsync(CancellationToken ct = default, IProgress<ProgressUpdate<BackupProgressStep>>? progress = null) => Task.CompletedTask;
        public Task<bool> IsAutoBackupEnabledAsync(CancellationToken ct = default) => Task.FromResult(false);
        public Task<bool> ShouldAutoBackupAsync(CancellationToken ct = default) => Task.FromResult(false);
        public string GetCandidateSqlitePath(string destFolder) => string.Empty;
        public string GetCandidateCsvArchivePath(string destFolder) => string.Empty;
    }

    public sealed class NullCsvExportService : ICsvExportService
    {
        public IReadOnlyList<string> AllColumnNames => [];
        public IReadOnlyList<string> DefaultColumnNames => [];
        public Task ExportAsync(CsvExportParameters parameters, CancellationToken ct = default, IProgress<ProgressUpdate<CsvExportProgressStep>>? progress = null) => Task.CompletedTask;
    }

    public sealed class NullSettingsService : ISettingsService
    {
        public Task<string?> GetAsync(string key, CancellationToken ct = default) => Task.FromResult<string?>(null);
        public Task SetAsync(string key, string? value, CancellationToken ct = default) => Task.CompletedTask;
    }

    public sealed class NullPrintService : IPrintService
    {
        public IReadOnlyList<string> AllColumnNames => [];
        public IReadOnlyList<string> DefaultColumnNames => [];
        public Task GenerateAsync(PrintParameters parameters, CancellationToken ct = default, IProgress<ProgressUpdate<PrintProgressStep>>? progress = null) => Task.CompletedTask;
        public void InitializeLicense() { }
    }

    public sealed class NullClipboardService : IClipboardService
    {
        public Task SetTextAsync(string text) => Task.CompletedTask;
    }

    private sealed class TestDbContextFactory(DbContextOptions<BookDbContext> options) : IDbContextFactory<BookDbContext>
    {
        public DbContextOptions<BookDbContext> Options { get; } = options;

        public BookDbContext CreateDbContext()
        {
            return new BookDbContext(Options);
        }
    }
}
