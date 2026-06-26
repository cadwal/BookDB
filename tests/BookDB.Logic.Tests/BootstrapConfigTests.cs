using System;
using System.IO;
using BookDB.Models;
using Xunit;

namespace BookDB.Logic.Tests;

public class BootstrapConfigTests
{
    private static string NewTempPath()
        => Path.Combine(Path.GetTempPath(), $"bookdb-bootstrap-{Guid.NewGuid():N}.json");

    [Fact]
    public void SaveThenLoad_RoundTripsAllFields()
    {
        string path = NewTempPath();
        try
        {
            var original = new BootstrapConfig
            {
                Version = 1,
                Backend = "PostgreSql",
                Postgres = new PostgresOptions
                {
                    Host = "db.example.lan",
                    Port = 5433,
                    Database = "books",
                    Username = "ulf",
                    SslMode = "VerifyFull",
                },
                Language = "sv",
                UiTheme = "Dark",
                LogLevel = "Verbose",
            };

            original.Save(path);
            BootstrapConfig? loaded = BootstrapConfig.Load(path);

            Assert.NotNull(loaded);
            Assert.Equal(1, loaded!.Version);
            Assert.Equal("PostgreSql", loaded.Backend);
            Assert.Equal("db.example.lan", loaded.Postgres.Host);
            Assert.Equal(5433, loaded.Postgres.Port);
            Assert.Equal("books", loaded.Postgres.Database);
            Assert.Equal("ulf", loaded.Postgres.Username);
            Assert.Equal("VerifyFull", loaded.Postgres.SslMode);
            Assert.Equal("sv", loaded.Language);
            Assert.Equal("Dark", loaded.UiTheme);
            Assert.Equal("Verbose", loaded.LogLevel);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Save_WritesCamelCaseKeys()
    {
        string path = NewTempPath();
        try
        {
            new BootstrapConfig().Save(path);
            string json = File.ReadAllText(path);

            Assert.Contains("\"backend\"", json);
            Assert.Contains("\"sslMode\"", json);
            Assert.Contains("\"host\"", json);
            Assert.DoesNotContain("\"Backend\"", json);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_MissingFile_ReturnsNull()
    {
        string path = NewTempPath();

        Assert.False(File.Exists(path));
        Assert.Null(BootstrapConfig.Load(path));
    }

    [Fact]
    public void Load_MalformedFile_ReturnsDefaultsWithoutThrowing()
    {
        string path = NewTempPath();
        try
        {
            File.WriteAllText(path, "{ this is not valid json ]");

            BootstrapConfig? loaded = BootstrapConfig.Load(path);

            Assert.NotNull(loaded);
            Assert.Equal(1, loaded!.Version);
            Assert.Equal("Sqlite", loaded.Backend);
            Assert.Equal(5432, loaded.Postgres.Port);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_UnknownBackendAndExtraBlocks_AreTolerated()
    {
        string path = NewTempPath();
        try
        {
            File.WriteAllText(path, """
                {
                  "version": 2,
                  "backend": "MySql",
                  "mysql": { "host": "maria.lan", "port": 3306 },
                  "futureField": true,
                  "postgres": { "host": "pg.lan", "port": 5444 }
                }
                """);

            BootstrapConfig? loaded = BootstrapConfig.Load(path);

            Assert.NotNull(loaded);
            Assert.Equal(2, loaded!.Version);
            Assert.Equal("MySql", loaded.Backend);
            Assert.Equal("pg.lan", loaded.Postgres.Host);
            Assert.Equal(5444, loaded.Postgres.Port);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
