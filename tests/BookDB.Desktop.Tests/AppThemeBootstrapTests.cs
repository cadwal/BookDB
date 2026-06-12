using System;
using System.IO;
using BookDB.Desktop;
using BookDB.Desktop.Theming;
using Microsoft.Data.Sqlite;
using Xunit;

namespace BookDB.Desktop.Tests;

public sealed class AppThemeBootstrapTests
{
    private static void WriteUiTheme(string connectionString, string value)
    {
        using var conn = new SqliteConnection(connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "CREATE TABLE Settings (Key TEXT PRIMARY KEY, Value TEXT); " +
                          "INSERT INTO Settings VALUES ('UiTheme', @v);";
        cmd.Parameters.AddWithValue("@v", value);
        cmd.ExecuteNonQuery();
    }

    [Theory]
    [InlineData("Default", ThemeFlavour.Default)]
    [InlineData("Vibrant", ThemeFlavour.Vibrant)]
    [InlineData("HighContrast", ThemeFlavour.HighContrast)]
    public void ApplyThemeBootstrap_FlavourStoredInDb_ReturnsThatFlavour(string stored, ThemeFlavour expected)
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"uitheme_test_{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={dbPath}";
        try
        {
            WriteUiTheme(connectionString, stored);

            var flavour = AppHost.ApplyThemeBootstrap(dbPath, connectionString);

            Assert.Equal(expected, flavour);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    [Fact]
    public void ApplyThemeBootstrap_KeyAbsentFromDb_ReturnsDefault()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"uitheme_test_{Guid.NewGuid():N}.db");
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

            var flavour = AppHost.ApplyThemeBootstrap(dbPath, connectionString);

            Assert.Equal(ThemeFlavour.Default, flavour);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    [Fact]
    public void ApplyThemeBootstrap_InvalidStoredValue_ReturnsDefaultWithoutCrash()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"uitheme_test_{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={dbPath}";
        try
        {
            WriteUiTheme(connectionString, "DROP TABLE Settings;--");

            var ex = Record.Exception(() =>
            {
                var flavour = AppHost.ApplyThemeBootstrap(dbPath, connectionString);
                Assert.Equal(ThemeFlavour.Default, flavour);
            });

            Assert.Null(ex);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    [Fact]
    public void ApplyThemeBootstrap_DbFileDoesNotExist_ReturnsDefaultWithoutCrash()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"nonexistent_{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={dbPath}";

        var ex = Record.Exception(() =>
        {
            var flavour = AppHost.ApplyThemeBootstrap(dbPath, connectionString);
            Assert.Equal(ThemeFlavour.Default, flavour);
        });

        Assert.Null(ex);
        if (File.Exists(dbPath)) File.Delete(dbPath);
    }
}
