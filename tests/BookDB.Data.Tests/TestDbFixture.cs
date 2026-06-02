using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using BookDB.Data;
using BookDB.Data.DbContexts;
using BookDB.Data.Interceptors;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BookDB.Data.Tests;

public sealed class TestDbFixture : IAsyncLifetime
{
    private string? _tempDbPath;
    private DbContextOptions<BookDbContext>? _options;

    public string ConnectionString { get; private set; } = string.Empty;

    public ValueTask InitializeAsync()
    {
        _tempDbPath = Path.GetTempFileName() + ".db";
        ConnectionString = $"Data Source={_tempDbPath}";

        var upgrader = SqliteExtensions.SqliteDatabase(
                DbUp.DeployChanges.To,
                ConnectionString)
            .WithScriptsEmbeddedInAssembly(
                Assembly.GetAssembly(typeof(DatabaseStartupService))!,
                name => name.Contains(".Migrations."))
            .Build();

        upgrader.PerformUpgrade();

        _options = new DbContextOptionsBuilder<BookDbContext>()
            .UseSqlite(ConnectionString)
            .AddInterceptors(new SqlitePragmaInterceptor())
            .Options;

        return ValueTask.CompletedTask;
    }

    public BookDbContext CreateContext()
    {
        if (_options == null)
        {
            throw new InvalidOperationException("Fixture has not been initialized.");
        }

        return new BookDbContext(_options);
    }

    public ValueTask DisposeAsync()
    {
        if (_tempDbPath != null && File.Exists(_tempDbPath))
        {
            try
            {
                File.Delete(_tempDbPath);
            }
            catch
            {
                // Best effort cleanup
            }
        }

        return ValueTask.CompletedTask;
    }
}
