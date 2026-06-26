using System.IO;
using System.IO.Compression;
using System.Linq;
using BookDB.Models;

namespace BookDB.Desktop.Helpers;

/// <summary>The kind of backup a restore zip holds, which decides how it is restored.</summary>
public enum RestoreArchiveKind
{
    /// <summary>A SQLite file backup (bundled <c>library.db</c>) — restored by replacing the local database file.</summary>
    SqliteFile,

    /// <summary>An engine-neutral CSV archive (per-table CSVs) — restored by the import engine into any backend.</summary>
    CsvArchive,

    /// <summary>Neither marker present — not a recognised BookDB backup.</summary>
    Unknown,
}

/// <summary>Detects which backup format a zip is by the marker entries the two backup paths write.</summary>
public static class RestoreArchiveInspector
{
    public static RestoreArchiveKind Detect(string zipPath)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        if (archive.Entries.Any(e => e.FullName == "library.db"))
            return RestoreArchiveKind.SqliteFile;
        if (archive.Entries.Any(e => e.FullName == "Books.csv"))
            return RestoreArchiveKind.CsvArchive;
        return RestoreArchiveKind.Unknown;
    }

    /// <summary>Reads the bundled config.json (the backend/connection the backup came from); null if absent.</summary>
    public static BootstrapConfig? ReadConfig(string zipPath)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        var entry = archive.GetEntry("config.json");
        if (entry is null)
            return null;

        var temp = Path.GetTempFileName();
        try
        {
            entry.ExtractToFile(temp, overwrite: true);
            return BootstrapConfig.Load(temp);
        }
        finally
        {
            try { File.Delete(temp); } catch { /* best effort */ }
        }
    }
}
