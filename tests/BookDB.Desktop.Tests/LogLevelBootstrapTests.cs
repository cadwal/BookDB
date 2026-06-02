using System;
using System.IO;
using BookDB.Desktop;
using Microsoft.Data.Sqlite;
using Serilog.Events;
using Xunit;

namespace BookDB.Desktop.Tests;

public sealed class LogLevelBootstrapTests
{
    [Fact]
    public void ApplyLogLevelBootstrap_VerboseStoredInDb_ReturnsSwitchWithDebugLevel()
    {
        // "Verbose" stored in DB → switch returns LogEventLevel.Debug
        var dbPath = Path.Combine(Path.GetTempPath(), $"loglevel_test_{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={dbPath}";
        try
        {
            using (var conn = new SqliteConnection(connectionString))
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "CREATE TABLE Settings (Key TEXT PRIMARY KEY, Value TEXT); " +
                                  "INSERT INTO Settings VALUES ('LogLevel', 'Verbose');";
                cmd.ExecuteNonQuery();
            }

            var levelSwitch = AppHost.ApplyLogLevelBootstrap(dbPath, connectionString);

            Assert.Equal(LogEventLevel.Debug, levelSwitch.MinimumLevel);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    [Fact]
    public void ApplyLogLevelBootstrap_NormalStoredInDb_ReturnsSwitchWithWarningLevel()
    {
        // "Normal" stored in DB → switch returns LogEventLevel.Warning (default)
        var dbPath = Path.Combine(Path.GetTempPath(), $"loglevel_test_{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={dbPath}";
        try
        {
            using (var conn = new SqliteConnection(connectionString))
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "CREATE TABLE Settings (Key TEXT PRIMARY KEY, Value TEXT); " +
                                  "INSERT INTO Settings VALUES ('LogLevel', 'Normal');";
                cmd.ExecuteNonQuery();
            }

            var levelSwitch = AppHost.ApplyLogLevelBootstrap(dbPath, connectionString);

            Assert.Equal(LogEventLevel.Warning, levelSwitch.MinimumLevel);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    [Fact]
    public void ApplyLogLevelBootstrap_KeyAbsentFromDb_ReturnsSwitchWithWarningLevel()
    {
        // Key absent from Settings table → switch returns LogEventLevel.Warning
        var dbPath = Path.Combine(Path.GetTempPath(), $"loglevel_test_{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={dbPath}";
        try
        {
            using (var conn = new SqliteConnection(connectionString))
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "CREATE TABLE Settings (Key TEXT PRIMARY KEY, Value TEXT);";
                cmd.ExecuteNonQuery();
            }

            var levelSwitch = AppHost.ApplyLogLevelBootstrap(dbPath, connectionString);

            Assert.Equal(LogEventLevel.Warning, levelSwitch.MinimumLevel);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    [Fact]
    public void ApplyLogLevelBootstrap_DbFileDoesNotExist_ReturnsSwitchWithWarningLevelWithoutCrash()
    {
        // DB file does not yet exist → returns Warning switch, no crash
        var dbPath = Path.Combine(Path.GetTempPath(), $"nonexistent_{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={dbPath}";

        var ex = Record.Exception(() =>
        {
            var levelSwitch = AppHost.ApplyLogLevelBootstrap(dbPath, connectionString);
            Assert.Equal(LogEventLevel.Warning, levelSwitch.MinimumLevel);
        });

        Assert.Null(ex);
        // File should not have been created
        if (File.Exists(dbPath)) File.Delete(dbPath);
    }

    [Fact]
    public void ApplyLogLevelBootstrap_InvalidStoredValue_ReturnsSwitchWithWarningLevelWithoutCrash()
    {
        // Any value other than "Normal"/"Verbose" silently falls back to Warning (malicious/invalid input)
        var dbPath = Path.Combine(Path.GetTempPath(), $"loglevel_test_{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={dbPath}";
        try
        {
            using (var conn = new SqliteConnection(connectionString))
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "CREATE TABLE Settings (Key TEXT PRIMARY KEY, Value TEXT); " +
                                  "INSERT INTO Settings VALUES ('LogLevel', 'DROP TABLE Settings;--');";
                cmd.ExecuteNonQuery();
            }

            var ex = Record.Exception(() =>
            {
                var levelSwitch = AppHost.ApplyLogLevelBootstrap(dbPath, connectionString);
                Assert.Equal(LogEventLevel.Warning, levelSwitch.MinimumLevel);
            });

            Assert.Null(ex);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }
}
