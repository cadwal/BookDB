using System;
using System.IO;
using BookDB.Desktop;
using BookDB.Models;
using Microsoft.Data.Sqlite;
using Xunit;

namespace BookDB.Desktop.Tests;

public sealed class AppHostBootstrapConfigTests
{
    private static string TempPath(string ext)
        => Path.Combine(Path.GetTempPath(), $"bookdb-bootstrap-{Guid.NewGuid():N}{ext}");

    private static void CreateLegacyDb(string dbPath, string? language, string? uiTheme, string? logLevel)
    {
        using var conn = new SqliteConnection($"Data Source={dbPath}");
        conn.Open();
        using (var create = conn.CreateCommand())
        {
            create.CommandText = "CREATE TABLE Settings (Key TEXT PRIMARY KEY, Value TEXT);";
            create.ExecuteNonQuery();
        }

        void Insert(string key, string? value)
        {
            if (value is null) return;
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO Settings (Key, Value) VALUES (@k, @v)";
            cmd.Parameters.AddWithValue("@k", key);
            cmd.Parameters.AddWithValue("@v", value);
            cmd.ExecuteNonQuery();
        }

        Insert("Language", language);
        Insert("UiTheme", uiTheme);
        Insert("LogLevel", logLevel);
    }

    [Fact]
    public void LoadOrCreate_NoConfigButLegacyDb_SeedsFromSettingsAndWritesFile()
    {
        var configPath = TempPath(".json");
        var dbPath = TempPath(".db");
        try
        {
            CreateLegacyDb(dbPath, "sv", "Vibrant", "Verbose");

            var config = AppHost.LoadOrCreateBootstrapConfig(configPath, dbPath);

            Assert.Equal("sv", config.Language);
            Assert.Equal("Vibrant", config.UiTheme);
            Assert.Equal("Verbose", config.LogLevel);
            Assert.True(File.Exists(configPath));

            // File is authoritative from now on: a reload yields the seeded values.
            var reloaded = BootstrapConfig.Load(configPath);
            Assert.NotNull(reloaded);
            Assert.Equal("sv", reloaded!.Language);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(configPath)) File.Delete(configPath);
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    [Fact]
    public void LoadOrCreate_FreshInstall_NoConfigNoDb_WritesDefaults()
    {
        var configPath = TempPath(".json");
        var dbPath = TempPath(".db");
        try
        {
            var config = AppHost.LoadOrCreateBootstrapConfig(configPath, dbPath);

            Assert.Equal("Sqlite", config.Backend);
            Assert.Null(config.Language);
            Assert.Null(config.UiTheme);
            Assert.Null(config.LogLevel);
            Assert.True(File.Exists(configPath));
        }
        finally
        {
            if (File.Exists(configPath)) File.Delete(configPath);
        }
    }

    [Fact]
    public void LoadOrCreate_ExistingConfig_IsReturnedWithoutSeedingFromDb()
    {
        var configPath = TempPath(".json");
        var dbPath = TempPath(".db");
        try
        {
            new BootstrapConfig { Language = "de" }.Save(configPath);
            CreateLegacyDb(dbPath, "sv", "Vibrant", "Verbose");

            var config = AppHost.LoadOrCreateBootstrapConfig(configPath, dbPath);

            Assert.Equal("de", config.Language);   // config.json wins; the DB Settings are ignored
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(configPath)) File.Delete(configPath);
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    [Theory]
    [InlineData("Sqlite", DatabaseBackend.Sqlite)]
    [InlineData("PostgreSql", DatabaseBackend.PostgreSql)]
    [InlineData("postgresql", DatabaseBackend.PostgreSql)]
    [InlineData("MySql", DatabaseBackend.Sqlite)]   // unknown/unsupported on this build → Sqlite
    [InlineData(null, DatabaseBackend.Sqlite)]
    public void ParseBackend_MapsKnownValuesAndFallsBackForUnknown(string? backend, DatabaseBackend expected)
        => Assert.Equal(expected, AppHost.ParseBackend(backend));
}
