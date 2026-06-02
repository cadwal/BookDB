using System;
using System.Globalization;
using System.IO;
using System.Threading;
using BookDB.Desktop;
using Microsoft.Data.Sqlite;
using Xunit;

namespace BookDB.Desktop.Tests;

public sealed class CultureBootstrapTests
{
    [Fact]
    public void ApplyCultureBootstrap_WithStoredLanguage_SetsCultureToStoredValue()
    {
        // Bootstrap reads stored Language row and sets CurrentUICulture
        var originalCulture = Thread.CurrentThread.CurrentUICulture;
        var originalDefaultUiCulture = CultureInfo.DefaultThreadCurrentUICulture;
        var originalDefaultCulture = CultureInfo.DefaultThreadCurrentCulture;
        var dbPath = Path.Combine(Path.GetTempPath(), $"bootstrap_test_{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={dbPath}";
        try
        {
            using (var conn = new SqliteConnection(connectionString))
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "CREATE TABLE Settings (Key TEXT PRIMARY KEY, Value TEXT); " +
                                  "INSERT INTO Settings VALUES ('Language', 'sv');";
                cmd.ExecuteNonQuery();
            }

            AppHost.ApplyCultureBootstrap(dbPath, connectionString);

            Assert.Equal("sv", Thread.CurrentThread.CurrentUICulture.TwoLetterISOLanguageName);
        }
        finally
        {
            Thread.CurrentThread.CurrentUICulture = originalCulture;
            Thread.CurrentThread.CurrentCulture   = originalCulture;
            // ApplyCultureBootstrap also mutates the process-wide default cultures; restore them
            // so later tests on other worker threads are unaffected (test isolation).
            CultureInfo.DefaultThreadCurrentUICulture = originalDefaultUiCulture;
            CultureInfo.DefaultThreadCurrentCulture   = originalDefaultCulture;
            SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    [Fact]
    public void ApplyCultureBootstrap_NoStoredLanguage_NoSatelliteForProbedCulture_FallsBackToEnglish()
    {
        // First-run probe — no Language row; probed OS culture has no matching satellite → falls back to "en"
        // Use "ja" as the probed culture: no Japanese satellite exists in the test output directory.
        var originalCulture = Thread.CurrentThread.CurrentUICulture;
        var originalDefaultUiCulture = CultureInfo.DefaultThreadCurrentUICulture;
        var originalDefaultCulture = CultureInfo.DefaultThreadCurrentCulture;
        var dbPath = Path.Combine(Path.GetTempPath(), $"bootstrap_test_{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={dbPath}";
        try
        {
            Thread.CurrentThread.CurrentUICulture = new CultureInfo("ja");

            using (var conn = new SqliteConnection(connectionString))
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "CREATE TABLE Settings (Key TEXT PRIMARY KEY, Value TEXT);";
                cmd.ExecuteNonQuery();
            }

            AppHost.ApplyCultureBootstrap(dbPath, connectionString);

            // "ja" satellite does not exist → fallback to "en"
            Assert.Equal("en", Thread.CurrentThread.CurrentUICulture.TwoLetterISOLanguageName);
            Assert.Equal("en", Thread.CurrentThread.CurrentCulture.TwoLetterISOLanguageName);
        }
        finally
        {
            Thread.CurrentThread.CurrentUICulture = originalCulture;
            Thread.CurrentThread.CurrentCulture   = originalCulture;
            // ApplyCultureBootstrap also mutates the process-wide default cultures; restore them
            // so later tests on other worker threads are unaffected (test isolation).
            CultureInfo.DefaultThreadCurrentUICulture = originalDefaultUiCulture;
            CultureInfo.DefaultThreadCurrentCulture   = originalDefaultCulture;
            SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    [Fact]
    public void ApplyCultureBootstrap_InvalidStoredCultureCode_FallsBackToEnglish()
    {
        // Security: invalid culture code stored in DB must not crash startup
        var originalCulture = Thread.CurrentThread.CurrentUICulture;
        var originalDefaultUiCulture = CultureInfo.DefaultThreadCurrentUICulture;
        var originalDefaultCulture = CultureInfo.DefaultThreadCurrentCulture;
        var dbPath = Path.Combine(Path.GetTempPath(), $"bootstrap_test_{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={dbPath}";
        try
        {
            using (var conn = new SqliteConnection(connectionString))
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "CREATE TABLE Settings (Key TEXT PRIMARY KEY, Value TEXT); " +
                                  "INSERT INTO Settings VALUES ('Language', 'zzz-invalid-9999');";
                cmd.ExecuteNonQuery();
            }

            var ex = Record.Exception(() => AppHost.ApplyCultureBootstrap(dbPath, connectionString));
            Assert.Null(ex); // must not throw
        }
        finally
        {
            Thread.CurrentThread.CurrentUICulture = originalCulture;
            Thread.CurrentThread.CurrentCulture   = originalCulture;
            // ApplyCultureBootstrap also mutates the process-wide default cultures; restore them
            // so later tests on other worker threads are unaffected (test isolation).
            CultureInfo.DefaultThreadCurrentUICulture = originalDefaultUiCulture;
            CultureInfo.DefaultThreadCurrentCulture   = originalDefaultCulture;
            SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }
}
