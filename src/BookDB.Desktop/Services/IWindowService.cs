using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
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
    void OpenFullDetailsWindow(int bookId);
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
    Task ShowSettingsAsync();
    Task ShowMaintenanceDialogAsync();
    void OpenStatisticsWindow();
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
}
