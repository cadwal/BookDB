using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BookDB.Logic.Import;

/// <summary>
/// Converts a live Readerware database directory (HSQLDB 1.8.x, e.g. <c>MyBooksRW.rw4</c>)
/// into a folder of UTF-16BE backup files that <see cref="ReaderwareBackupParser"/> can consume,
/// driving the user-supplied HSQLDB + Java runtime out of process.
/// </summary>
public interface IReaderwareDbExportService
{
    /// <summary>
    /// Export the tables the importer needs from the database at <paramref name="rw4Dir"/> into
    /// <paramref name="outputDir"/>. The live database is never opened in place — it is copied to a
    /// temporary working directory first. Validation failures are returned as a non-success result
    /// rather than thrown, so the caller can map <see cref="ReaderwareExportResult.Failure"/> to a
    /// localized message.
    /// </summary>
    /// <param name="rw4Dir">The Readerware database directory (contains <c>&lt;base&gt;.script</c> etc.).</param>
    /// <param name="outputDir">Destination folder for the generated backup files.</param>
    /// <param name="toolBinPath">Folder containing <c>jre/bin/java.exe</c> and <c>lib/hsqldb.jar</c>.</param>
    /// <param name="log">Optional sink for progress and raw tool output lines (not localized).</param>
    Task<ReaderwareExportResult> ExportAsync(
        string rw4Dir,
        string outputDir,
        string toolBinPath,
        IProgress<string>? log = null,
        CancellationToken ct = default);
}

/// <summary>Why an export did not complete. <see cref="None"/> means success.</summary>
public enum ReaderwareExportFailure
{
    None,

    /// <summary><c>java.exe</c> or <c>hsqldb.jar</c> was not found under the tool path.</summary>
    ToolPathInvalid,

    /// <summary>The database directory is missing its <c>.properties</c>/<c>.script</c> files.</summary>
    DatabaseInvalid,

    /// <summary>The schema has no READERWARE table — not a Readerware database.</summary>
    MainTableMissing,

    /// <summary>HSQLDB SqlTool exited non-zero or reported an error.</summary>
    ProcessFailed,

    /// <summary>The export was cancelled by the caller.</summary>
    Cancelled,
}

/// <summary>Outcome of <see cref="IReaderwareDbExportService.ExportAsync"/>.</summary>
public sealed record ReaderwareExportResult
{
    public ReaderwareExportFailure Failure { get; init; }

    public bool Success => Failure == ReaderwareExportFailure.None;

    /// <summary>The folder the backup files were written to (the requested output directory).</summary>
    public string OutputDirectory { get; init; } = string.Empty;

    /// <summary>Names of the tables that were exported (one file each).</summary>
    public IReadOnlyList<string> ExportedTables { get; init; } = [];

    /// <summary>Non-fatal notes (e.g. a table present in the importer's list but absent from the schema).</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];

    /// <summary>Raw technical detail for the log/diagnostics — never user-facing, not localized.</summary>
    public string? Detail { get; init; }
}
