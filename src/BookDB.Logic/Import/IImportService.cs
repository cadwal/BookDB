using System;
using System.Threading;
using System.Threading.Tasks;

namespace BookDB.Logic.Import;

/// <summary>
/// Abstraction over ImportService for testability.
/// </summary>
public interface IImportService
{
    Task<ImportPreview> PreviewAsync(string path, int collectionId, CancellationToken ct = default);

    Task<ImportResult> ImportAsync(
        string path,
        int collectionId,
        IProgress<ImportProgress>? progress = null,
        Func<string, CancellationToken, Task<ImportDuplicateResolution>>? askCallback = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// The user's choice when an imported book duplicates an existing ISBN (with "Ask" duplicate handling).
/// The <c>*All</c> variants apply the same choice to every remaining duplicate in the run.
/// </summary>
public enum ImportDuplicateResolution
{
    Overwrite,
    OverwriteAll,
    Skip,
    SkipAll,
    CancelImport,
}
