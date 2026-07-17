using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using CsvHelper;
using CsvHelper.Configuration;

namespace BookDB.Logic.Import;

/// <summary>
/// Drives a user-supplied HSQLDB 1.8.x + Java runtime to dump the tables the importer needs from a
/// live Readerware database into UTF-16BE backup files that <see cref="ReaderwareBackupParser"/> reads.
/// </summary>
/// <remarks>
/// HSQLDB 1.8's text-table writer is broken in the shipped build (it emits correct field widths but
/// blanks every character), so this uses SqlTool's <c>\x</c> DSV export instead, which formats the
/// result set itself. DSV has no quoting/escaping, so rare multi-character sentinel delimiters are used
/// to keep embedded newlines/commas/pipes in free-text fields from breaking rows. The UTF-8 DSV is then
/// re-emitted in C# as the comma-separated UTF-16BE form the parser expects. The live database is copied
/// to a temporary directory first; it is never opened in place.
/// </remarks>
public sealed class ReaderwareDbExportService : IReaderwareDbExportService
{
    // Sentinel delimiters: printable ASCII (so Java's String.trim() won't eat them) yet effectively
    // impossible to find in book metadata, HTML, or hex image data.
    public const string ColumnDelimiter = "~|RWCOL|~";
    public const string RowDelimiter = "~|RWROW|~";
    public const string NullToken = "~|RWNULL|~";

    // Data tables the parser consumes by name, in addition to the *_LIST lookups.
    private static readonly string[] DataTables =
    {
        "READERWARE", "CONTRIBUTOR", "FULL_IMAGES", "THUMB_IMAGES",
        "READERWARE_VOLUMES", "READERWARE_CHAPTERS", "LOANS", "BORROWER", "DBCATALOG40"
    };

    /// <summary>Every table the importer reads: the data tables plus the lookup lists.</summary>
    public static IReadOnlyList<string> TargetTables { get; } =
        DataTables.Concat(ImportLookupCache.ListFileNames).ToArray();

    private const string MainTable = "READERWARE";

    /// <summary>The bundled JRE launcher: <c>java.exe</c> on Windows, <c>java</c> elsewhere.</summary>
    public static string JavaExecutableName => OperatingSystem.IsWindows() ? "java.exe" : "java";

    // UTF-16 Big Endian, BOM-free — matches the on-disk format the parser decodes.
    private static readonly Encoding Utf16BeNoBom = new UnicodeEncoding(bigEndian: true, byteOrderMark: false);

    public async Task<ReaderwareExportResult> ExportAsync(
        string rw4Dir,
        string outputDir,
        string toolBinPath,
        IProgress<string>? log = null,
        CancellationToken ct = default)
    {
        // 1. Validate the tool path.
        var javaExe = Path.Combine(toolBinPath, "jre", "bin", JavaExecutableName);
        var hsqldbJar = Path.Combine(toolBinPath, "lib", "hsqldb.jar");
        if (!File.Exists(javaExe) || !File.Exists(hsqldbJar))
        {
            return new ReaderwareExportResult
            {
                Failure = ReaderwareExportFailure.ToolPathInvalid,
                Detail = $"Expected '{javaExe}' and '{hsqldbJar}'.",
            };
        }

        // 2. Validate the database directory and resolve its base name.
        if (!Directory.Exists(rw4Dir) || !TryResolveDbBase(rw4Dir, out var dbBase))
        {
            return new ReaderwareExportResult
            {
                Failure = ReaderwareExportFailure.DatabaseInvalid,
                Detail = $"No '<base>.properties' + '<base>.script' pair found in '{rw4Dir}'.",
            };
        }

        // 3. Decide which target tables actually exist in this schema.
        var scriptText = await File.ReadAllTextAsync(Path.Combine(rw4Dir, dbBase + ".script"), ct);
        var present = ParseTableNames(scriptText);
        var tablesToExport = TargetTables.Where(present.Contains).ToList();
        var warnings = TargetTables.Where(t => !present.Contains(t))
            .Select(t => $"Table '{t}' is not present in the schema — skipping.")
            .ToList();

        if (!present.Contains(MainTable))
        {
            return new ReaderwareExportResult
            {
                Failure = ReaderwareExportFailure.MainTableMissing,
                Detail = $"No '{MainTable}' table in the schema.",
            };
        }

        var tempDir = Path.Combine(Path.GetTempPath(), $"bookdb_rwexport_{Guid.NewGuid():N}");
        try
        {
            // 4. Copy the database to a throwaway working directory (never touch the live DB).
            log?.Report($"Copying database '{dbBase}' to a temporary working folder…");
            Directory.CreateDirectory(tempDir);
            CopyDatabase(rw4Dir, tempDir, dbBase);

            Directory.CreateDirectory(outputDir);

            // 5. Generate and run the DSV export script against the copy.
            var dsvDir = Path.Combine(tempDir, "dsv");
            Directory.CreateDirectory(dsvDir);
            var script = BuildExportScript(tablesToExport, dsvDir);
            var scriptPath = Path.Combine(tempDir, "export.sqltool");
            await File.WriteAllTextAsync(scriptPath, script, ct);

            log?.Report("Running HSQLDB export…");
            var (exitCode, output) = await RunSqlToolAsync(
                javaExe, hsqldbJar, tempDir, dbBase, scriptPath, log, ct);

            if (ct.IsCancellationRequested)
                return new ReaderwareExportResult { Failure = ReaderwareExportFailure.Cancelled, Detail = output };

            if (exitCode != 0)
            {
                return new ReaderwareExportResult
                {
                    Failure = ReaderwareExportFailure.ProcessFailed,
                    Detail = $"SqlTool exited with code {exitCode}.\n{output}",
                };
            }

            // 6. Convert each UTF-8 DSV to a UTF-16BE comma-CSV backup file.
            log?.Report("Converting exported tables to backup format…");
            var exported = new List<string>();
            foreach (var table in tablesToExport)
            {
                ct.ThrowIfCancellationRequested();
                var dsvPath = Path.Combine(dsvDir, table + ".dsv");
                if (!File.Exists(dsvPath))
                {
                    warnings.Add($"Table '{table}' produced no export file — skipping.");
                    continue;
                }

                // Stream rather than ReadAllText: FULL_IMAGES.dsv holds the whole catalog's
                // covers as hex and would otherwise land in memory as one multi-GB string.
                using (var dsvReader = new StreamReader(dsvPath, Encoding.UTF8))
                    ConvertDsvToBackupFile(dsvReader, Path.Combine(outputDir, table));
                exported.Add(table);
            }

            log?.Report($"Export complete: {exported.Count} table(s) written to '{outputDir}'.");
            return new ReaderwareExportResult
            {
                Failure = ReaderwareExportFailure.None,
                OutputDirectory = outputDir,
                ExportedTables = exported,
                Warnings = warnings,
            };
        }
        catch (OperationCanceledException)
        {
            return new ReaderwareExportResult { Failure = ReaderwareExportFailure.Cancelled };
        }
        catch (Exception ex)
        {
            return new ReaderwareExportResult { Failure = ReaderwareExportFailure.ProcessFailed, Detail = ex.ToString() };
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    // ----- Pure, testable helpers -------------------------------------------------------------

    /// <summary>Return the set of table names defined by <c>CREATE … TABLE</c> in a HSQLDB script.</summary>
    public static ISet<string> ParseTableNames(string scriptText)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        var matches = Regex.Matches(
            scriptText,
            @"CREATE\s+(?:CACHED\s+|MEMORY\s+|TEMP\s+|TEXT\s+)?TABLE\s+""?(\w+)""?\s*\(",
            RegexOptions.IgnoreCase);
        foreach (Match m in matches)
            names.Add(m.Groups[1].Value);
        return names;
    }

    /// <summary>
    /// Build the SqlTool command script that exports each table to a sentinel-delimited UTF-8 DSV file
    /// in <paramref name="dsvDir"/> (one <c>&lt;table&gt;.dsv</c> per table).
    /// </summary>
    public static string BuildExportScript(IEnumerable<string> tables, string dsvDir)
    {
        var sb = new StringBuilder();
        sb.Append("* *DSV_COL_DELIM = ").Append(ColumnDelimiter).Append('\n');
        sb.Append("* *DSV_ROW_DELIM = ").Append(RowDelimiter).Append('\n');
        sb.Append("* *NULL_REP_TOKEN = ").Append(NullToken).Append('\n');

        foreach (var table in tables)
        {
            var target = Path.Combine(dsvDir, table + ".dsv").Replace('\\', '/');
            sb.Append("* *DSV_TARGET_FILE = ").Append(target).Append('\n');
            sb.Append("\\x ").Append(table).Append('\n');
        }

        sb.Append("\\q\n");
        return sb.ToString();
    }

    /// <summary>
    /// Convert a sentinel-delimited DSV (the first row is the header) into the comma-separated,
    /// UTF-16BE, BOM-free CSV the parser reads. Null tokens become empty fields.
    /// </summary>
    public static void ConvertDsvToBackupFile(string dsvContent, string destPath)
    {
        using var reader = new StringReader(dsvContent);
        ConvertDsvToBackupFile(reader, destPath);
    }

    /// <summary>
    /// Streaming form of <see cref="ConvertDsvToBackupFile(string,string)"/>: holds one row in
    /// memory at a time, so a multi-GB image table never materializes as a single string.
    /// </summary>
    public static void ConvertDsvToBackupFile(TextReader dsvReader, string destPath)
    {
        using var stream = new FileStream(destPath, FileMode.Create, FileAccess.Write);
        using var writer = new StreamWriter(stream, Utf16BeNoBom);
        using var csv = new CsvWriter(writer, new CsvConfiguration(CultureInfo.InvariantCulture));

        var row = new StringBuilder();
        var buffer = new char[64 * 1024];
        int matched = 0;  // chars of RowDelimiter matched so far at the current position
        int read;
        while ((read = dsvReader.Read(buffer, 0, buffer.Length)) > 0)
        {
            for (int i = 0; i < read; i++)
            {
                var c = buffer[i];
            retry:
                if (c == RowDelimiter[matched])
                {
                    if (++matched == RowDelimiter.Length)
                    {
                        WriteBackupRow(csv, row);
                        matched = 0;
                    }
                }
                else if (matched > 0)
                {
                    // False start: the matched chars were row content. No proper prefix of the
                    // delimiter is also a suffix of a longer prefix, so re-testing the current
                    // char from position 0 is sufficient.
                    row.Append(RowDelimiter, 0, matched);
                    matched = 0;
                    goto retry;
                }
                else
                {
                    row.Append(c);
                }
            }
        }

        if (matched > 0)
            row.Append(RowDelimiter, 0, matched);
        WriteBackupRow(csv, row);
    }

    private static void WriteBackupRow(CsvWriter csv, StringBuilder row)
    {
        // The final element after a trailing row delimiter is empty — skip those blanks.
        if (row.Length == 0)
            return;

        foreach (var field in row.ToString().Split(ColumnDelimiter, StringSplitOptions.None))
            csv.WriteField(field == NullToken ? string.Empty : field);

        csv.NextRecord();
        row.Clear();
    }

    // ----- Internals --------------------------------------------------------------------------

    private static bool TryResolveDbBase(string rw4Dir, out string dbBase)
    {
        foreach (var props in Directory.EnumerateFiles(rw4Dir, "*.properties"))
        {
            var candidate = Path.GetFileNameWithoutExtension(props);
            if (File.Exists(Path.Combine(rw4Dir, candidate + ".script")))
            {
                dbBase = candidate;
                return true;
            }
        }

        dbBase = string.Empty;
        return false;
    }

    private static void CopyDatabase(string sourceDir, string destDir, string dbBase)
    {
        // The small operational files plus the bulk CACHED-table data. Deliberately skip the live
        // lock file (.lck) and the large rolling .bkup.script* exports.
        foreach (var ext in new[] { ".properties", ".script", ".data", ".backup", ".log" })
        {
            var src = Path.Combine(sourceDir, dbBase + ext);
            if (File.Exists(src))
                File.Copy(src, Path.Combine(destDir, dbBase + ext), overwrite: true);
        }
    }

    private static async Task<(int ExitCode, string Output)> RunSqlToolAsync(
        string javaExe, string hsqldbJar, string tempDbDir, string dbBase, string scriptPath,
        IProgress<string>? log, CancellationToken ct)
    {
        var dbUrlPath = Path.Combine(tempDbDir, dbBase).Replace('\\', '/');

        var psi = new ProcessStartInfo
        {
            FileName = javaExe,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = tempDbDir,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        psi.ArgumentList.Add("-Dfile.encoding=UTF-8");
        psi.ArgumentList.Add("-cp");
        psi.ArgumentList.Add(hsqldbJar);
        psi.ArgumentList.Add("org.hsqldb.util.SqlTool");
        psi.ArgumentList.Add("--inlineRc");
        psi.ArgumentList.Add($"url=jdbc:hsqldb:file:{dbUrlPath},user=SA,password=");
        psi.ArgumentList.Add(scriptPath);

        using var process = new Process { StartInfo = psi };
        var output = new StringBuilder();
        void Capture(string? line)
        {
            if (line is null) return;
            lock (output) output.AppendLine(line);
            log?.Report(line);
        }

        process.OutputDataReceived += (_, e) => Capture(e.Data);
        process.ErrorDataReceived += (_, e) => Capture(e.Data);

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }

        return (process.ExitCode, output.ToString());
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch { /* best effort */ }
    }

    private static void TryDeleteDirectory(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
        catch { /* a transient lock on a temp file should not fail the export */ }
    }
}
