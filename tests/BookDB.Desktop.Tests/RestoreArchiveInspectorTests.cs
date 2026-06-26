using System;
using System.IO;
using System.IO.Compression;
using BookDB.Desktop.Helpers;
using Xunit;

namespace BookDB.Desktop.Tests;

public sealed class RestoreArchiveInspectorTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"bookdb_inspect_{Guid.NewGuid():N}");

    public RestoreArchiveInspectorTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    private string MakeZip(string name, params string[] entries)
    {
        var path = Path.Combine(_dir, name);
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var entry in entries)
            archive.CreateEntry(entry);
        return path;
    }

    [Fact]
    public void Detect_LibraryDb_IsSqliteFile()
        => Assert.Equal(RestoreArchiveKind.SqliteFile, RestoreArchiveInspector.Detect(MakeZip("s.zip", "library.db", "config.json")));

    [Fact]
    public void Detect_BooksCsv_IsCsvArchive()
        => Assert.Equal(RestoreArchiveKind.CsvArchive, RestoreArchiveInspector.Detect(MakeZip("c.zip", "Books.csv", "People.csv", "config.json")));

    [Fact]
    public void Detect_NeitherMarker_IsUnknown()
        => Assert.Equal(RestoreArchiveKind.Unknown, RestoreArchiveInspector.Detect(MakeZip("u.zip", "random.txt")));

    private string MakeZipWithConfig(string name, string configJson)
    {
        var path = Path.Combine(_dir, name);
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        archive.CreateEntry("Books.csv");
        var entry = archive.CreateEntry("config.json");
        using var writer = new StreamWriter(entry.Open());
        writer.Write(configJson);
        return path;
    }

    [Fact]
    public void ReadConfig_ParsesBundledConfig()
    {
        var zip = MakeZipWithConfig("cfg.zip",
            "{\"version\":1,\"backend\":\"PostgreSql\",\"postgres\":{\"host\":\"db.example.com\",\"database\":\"bookdb\"}}");

        var config = RestoreArchiveInspector.ReadConfig(zip);

        Assert.NotNull(config);
        Assert.Equal("PostgreSql", config!.Backend);
        Assert.Equal("db.example.com", config.Postgres.Host);
    }

    [Fact]
    public void ReadConfig_NoConfigEntry_ReturnsNull()
        => Assert.Null(RestoreArchiveInspector.ReadConfig(MakeZip("noconfig.zip", "Books.csv")));
}
