using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using BookDB.Data.Interfaces;
using BookDB.Desktop.ViewModels;
using BookDB.Help;
using BookDB.Logic.Services;
using BookDB.Models.Entities;
using BookDB.Models.Metadata;

namespace BookDB.Desktop.Services;

public enum UnsavedChangesResult { Save, Discard, KeepEditing }

public enum DuplicateIsbnResult { UpdateExisting, AddAsNew, Cancel }

public interface IWindowService
{
    Task<bool?> ShowAddBookDialogAsync(int? defaultCollectionId = null, string? prefillIsbn = null);
    Task<bool?> ShowBulkEditDialogAsync(IReadOnlyList<int> bookIds);
    Task<bool?> ShowAdvancedSearchDialogAsync(SavedSearch? searchToEdit = null);
    Task<UnsavedChangesResult> ShowUnsavedChangesDialogAsync(string bookTitle);
    Task<bool?> ShowDeleteConfirmationAsync(string message);
    Task OpenFullDetailsWindowAsync(int bookId);
    Task<bool?> ShowLookupWizardDialogAsync();
    Task<bool?> ShowMergeReviewDialogAsync(
        IReadOnlyList<BookMetadata> sources,
        BookMetadata? currentBook,
        IReadOnlyList<CoverOption> coverOptions,
        int? existingBookId,
        int? collectionId,
        Window? ownerWindow = null);
    Task<DuplicateIsbnResult> ShowDuplicateIsbnDialogAsync(string isbn, string existingTitle);
    void OpenBatchQueueWindow();
    Task StartBatchAsync(IReadOnlyList<string> isbns);
    Task StartBatchRecatalogAsync(IReadOnlyList<int> bookIds);
    void CloseAllSecondaryWindows();
    Task<string?> ShowIsbnPromptDialogAsync();
    Task<bool?> ShowImportWizardAsync(string? initialPath = null);
    Task ShowReaderwareDbImportDialogAsync();
    Task<bool?> ShowBatchShutdownWarningAsync();
    Task<bool?> ShowMainShutdownWarningAsync();
    Task ShowManageLookupsAsync(string? initialTab = null);
    Task ShowSettingsAsync(Window? owner = null);
    Task ShowMaintenanceDialogAsync();
    Task OpenStatisticsWindowAsync();
    void OpenHelpWindow(HelpTab tab);
    Task<IReadOnlyList<string>?> ShowCsvColumnPickerAsync(
        IReadOnlyList<string> allColumns,
        IReadOnlyList<string> defaultSelected);
    Task<int?> ShowMergeTargetPickerAsync(
        string sourceName,
        int sourceId,
        System.Collections.Generic.IReadOnlyList<BookDB.Desktop.ViewModels.LookupEntryRow> candidates,
        Avalonia.Controls.Window? owner = null);
    Task<PrintParameters?> ShowPrintDialogAsync(
        IReadOnlySet<int>? collectionIds,
        IReadOnlyList<int>? searchBookIds,
        Dictionary<string, HashSet<int>>? facetFilters,
        string? sortColumn,
        bool sortAscending,
        int bookCount = 0);
    Task<bool?> ShowCheckOutDialogAsync(int bookId);
    Task ShowManageBorrowersAsync();

    /// <summary>Manual-backup format + destination picker. Returns the chosen format ("SQLite"/"CsvArchive") and
    /// folder, or null if cancelled.</summary>
    Task<(string Format, string Folder)?> ShowBackupFormatDialogAsync(bool supportsFileBackup, string configDefault, string defaultFolder);

    /// <summary>
    /// Startup concurrency gate (remote backend only): if another live client holds the shared database, blocks
    /// on <paramref name="owner"/>. Returns true to proceed (no other client, or the
    /// user chose "Connect anyway"), false to abort startup.
    /// </summary>
    Task<bool> ShowConnectDialogAsync(Window owner);

    /// <summary>
    /// Pre-host modal shown over <paramref name="owner"/> when the remote database is unreachable at startup.
    /// The view model re-probes via <paramref name="connect"/> on Retry; the returned outcome tells the
    /// startup flow whether to proceed, open settings, or quit.
    /// </summary>
    Task<StartupFailureOutcome> ShowStartupFailureDialogAsync(
        ConnectionProbeResult initialResult,
        System.Func<System.Threading.CancellationToken, System.Threading.Tasks.Task<ConnectionProbeResult>> connect,
        Window owner);

    /// <summary>
    /// Mid-session write-failure modal (remote backend): the connection dropped while saving. Returns the user's
    /// choice to retry the save or discard the unsaved changes.
    /// </summary>
    Task<WriteFailureChoice> ShowWriteFailureDialogAsync(string message);

    /// <summary>
    /// Escalation modal shown when the database has stayed unreachable past the retry window. Returns true if the
    /// user chose to quit; false to keep waiting while the monitor continues retrying in the background.
    /// </summary>
    Task<bool> ShowConnectionLostEscalationDialogAsync();
}
