using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using BookDB.Data.Interfaces;
using BookDB.Desktop.Services.UpdateCheck;
using BookDB.Desktop.ViewModels;
using BookDB.Help;
using BookDB.Logic.Import;
using BookDB.Logic.Services;
using BookDB.Models.Entities;
using BookDB.Models.Metadata;

namespace BookDB.Desktop.Services;

public enum UnsavedChangesResult { Save, Discard, KeepEditing }

public enum DuplicateIsbnResult { UpdateExisting, AddAsNew, Cancel }

public enum BackupConflictChoice { Overwrite, AddSuffix, Cancel }

public enum RestoreTargetChoice { Current, Archived, Cancel }

/// <summary>A settings section a caller can request the Settings window open directly to.</summary>
public enum SettingsSection { Database }

public enum ReleaseNotesChoice { Show, Skip, Defer }

/// <summary>
/// Live progress window: report status lines through <see cref="System.IProgress{T}.Report"/> (safe from
/// any thread) and call <see cref="Close"/> when the work finishes. Callers never hold the window itself.
/// </summary>
public interface IProgressWindowHandle : System.IProgress<string>
{
    void Close();
}

public interface IWindowService
{
    Task<bool?> ShowAddBookDialogAsync(int? defaultCollectionId = null, string? prefillIsbn = null);

    /// <summary>
    /// Opens the guided add-book identify dialog (ISBN-first entry). New books default into
    /// <paramref name="collectionId"/> — the caller's currently selected collection.
    /// </summary>
    Task<bool?> ShowAddBookIdentifyDialogAsync(int? collectionId = null);
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
        IReadOnlyList<string>? rateLimitedSources = null,
        IReadOnlyList<string>? noResultSources = null,
        IReadOnlyList<string>? erroredSources = null,
        Window? ownerWindow = null);
    Task<DuplicateIsbnResult> ShowDuplicateIsbnDialogAsync(string isbn, string existingTitle);

    /// <summary>Shows the channel-specific upgrade hint (copyable command for winget/AppMan, or a GitHub
    /// download link) when the user clicks the status-bar update indicator.</summary>
    Task ShowUpdateHintAsync(InstallChannel channel, string latestVersion, string currentVersion);

    /// <summary>
    /// A manual backup would overwrite <paramref name="existingPath"/>. Offers saving under the next free
    /// "name-N" suffix (the Enter default), overwriting, or cancelling; closing the window cancels.
    /// </summary>
    Task<BackupConflictChoice> ShowBackupConflictAsync(string existingPath);
    void OpenBatchQueueWindow();
    Task StartBatchAsync(IReadOnlyList<string> isbns);
    Task StartBatchRecatalogAsync(IReadOnlyList<int> bookIds);

    /// <summary>Single-book re-catalog under an explicitly supplied ISBN — the prompt path for a book
    /// whose record has none; the queue item's BookId routes the result onto the existing book.</summary>
    Task StartBatchRecatalogAsync(int bookId, string isbn);
    void CloseAllSecondaryWindows();
    Task<bool> ConfirmCloseGuardedWindowsAsync();
    /// <summary>
    /// Asks for the ISBN to re-catalog <paramref name="bookTitle"/> under (the record has none).
    /// Returns the entered ISBN, or null when dismissed or left empty — the caller skips the book then.
    /// </summary>
    Task<string?> ShowIsbnPromptDialogAsync(string bookTitle);
    Task<bool?> ShowImportWizardAsync(string? initialPath = null);
    Task ShowReaderwareDbImportDialogAsync();
    Task<bool?> ShowBatchShutdownWarningAsync();
    Task<bool?> ShowMainShutdownWarningAsync();
    Task ShowManageLookupsAsync(string? initialTab = null);
    Task ShowSettingsAsync(Window? owner = null, SettingsSection? section = null);
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

    /// <summary>About box: app name, assembly version and copyright.</summary>
    Task ShowAboutAsync();

    /// <summary>"What's new" viewer: the release-notes markdown for <paramref name="version"/>, rendered in
    /// an owned window. Not registered in the Window menu — modal-lite, closed and gone.</summary>
    Task ShowReleaseNotesAsync(string version, string markdown);

    /// <summary>
    /// First start after an update: offers the "what's new" notes for <paramref name="version"/>.
    /// Enter takes Show; Esc or closing the window defers — the caller records the version only for
    /// Show and Skip, so a deferred prompt returns on the next start.
    /// </summary>
    Task<ReleaseNotesChoice> ShowReleaseNotesPromptAsync(string version);

    /// <summary>
    /// When a CSV backup names the database it came from, asks whether to restore into that database or the
    /// current one. Enter takes the archived server; Esc or closing the window cancels.
    /// </summary>
    Task<RestoreTargetChoice> ShowRestoreTargetAsync(string archivedServerDescription);

    /// <summary>
    /// Informational message with a single OK button. The task completes when the dialog is dismissed;
    /// callers that only need to surface the message may discard it.
    /// </summary>
    Task ShowInfoAsync(string body, string? title = null);

    /// <summary>
    /// Yes/No confirmation. Returns true for Yes, false for No, or null when the window is closed without
    /// an answer. With no <paramref name="owner"/> it falls back to the live main window, or — before one
    /// exists (startup outage recovery) — shows non-modally while still awaiting the answer.
    /// </summary>
    Task<bool?> ShowConfirmAsync(string title, string body, Window? owner = null);

    /// <summary>
    /// Import duplicate-ISBN prompt with per-item and "apply to all" choices plus cancel-import.
    /// Esc or closing the window resolves to Skip (safe, non-destructive); Enter picks nothing
    /// (every choice writes or skips), and cancelling the whole import is a deliberate click only.
    /// </summary>
    Task<ImportDuplicateResolution> ShowDuplicateResolutionAsync(string title, string body);

    /// <summary>
    /// Progress window for a long operation started from the main window. Shown modally over it (blocks
    /// interaction, can never fall behind) but not awaited — the caller keeps running the operation and
    /// closes the returned handle when it finishes.
    /// </summary>
    IProgressWindowHandle ShowProgressWindow(string header);

    /// <summary>
    /// Borderless status card shown while a backup runs during shutdown. Standalone (no owner): by the
    /// time shutdown runs the main window has already closed, so parenting to it would throw. Showing is
    /// best-effort — it must never block or skip the backup itself.
    /// </summary>
    IProgressWindowHandle ShowBackupProgressWindow();
}
