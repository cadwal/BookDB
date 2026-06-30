using System;
using System.Threading;
using System.Threading.Tasks;
using BookDB.Data;
using BookDB.Data.DbContexts;
using BookDB.Data.Interfaces;
using BookDB.Data.MySql;
using BookDB.Data.PostgreSQL;
using BookDB.Data.Sqlite;
using BookDB.Logic.Services;
using BookDB.Models;
using BookDB.Models.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace BookDB.Desktop.Services;

/// <summary>
/// A built, schema-ready target backend for a migration or restore: exposes the target's context factory,
/// identity-resync, and a backup service (for a safety export of the target). Disposing tears down the
/// underlying provider's connection pool.
/// </summary>
public interface IMigrationTarget : IAsyncDisposable
{
    IDbContextFactory<BookDbContext> Factory { get; }
    IIdentitySequenceResync Resync { get; }
    IBackupService Backup { get; }
}

/// <summary>
/// Concrete <see cref="IMigrationTarget"/> backed by an isolated provider service-provider, so the operation
/// never touches the running app's active-backend DI.
/// </summary>
public sealed class MigrationTarget : IMigrationTarget
{
    private readonly ServiceProvider _provider;

    internal MigrationTarget(ServiceProvider provider)
    {
        _provider = provider;
        Factory = provider.GetRequiredService<IDbContextFactory<BookDbContext>>();
        Resync = provider.GetRequiredService<IIdentitySequenceResync>();
        // A backup service over this target, only used for the safety export (CSV archive, provider-neutral) before
        // a restore overwrites it; resource strings are never reached because that export runs without progress.
        Backup = new BackupService(
            Factory, provider.GetRequiredService<AppSettings>(), new LookupService(Factory, NoResources.Instance),
            NoResources.Instance, provider.GetRequiredService<IDataChangeTracker>(),
            provider.GetRequiredService<IBackupStrategy>());
    }

    public IDbContextFactory<BookDbContext> Factory { get; }
    public IIdentitySequenceResync Resync { get; }
    public IBackupService Backup { get; }

    public async ValueTask DisposeAsync() => await _provider.DisposeAsync();

    private sealed class NoResources : IResourceProvider
    {
        public static readonly NoResources Instance = new();
        public string? GetString(string key) => key;
    }
}

/// <summary>
/// Builds the target backend for a "Move library" migration: stands up an isolated provider for the chosen
/// backend and connection string and runs its DbUp set so the schema exists before any copy.
/// </summary>
public interface IMigrationTargetBuilder
{
    Task<IMigrationTarget> BuildAsync(DatabaseBackend backend, string connectionString, CancellationToken ct = default);
}

/// <inheritdoc cref="IMigrationTargetBuilder"/>
public sealed class MigrationTargetBuilder : IMigrationTargetBuilder
{
    public async Task<IMigrationTarget> BuildAsync(
        DatabaseBackend backend, string connectionString, CancellationToken ct = default)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDataChangeTracker, DataChangeTracker>();
        // The provider's backup strategy and maintenance provider take AppSettings; the connection string (not
        // AppSettings) configures the context, so Backend alone is enough for this isolated target container.
        services.AddSingleton(new AppSettings { Backend = backend });

        switch (backend)
        {
            case DatabaseBackend.Sqlite:
                services.AddSqliteProvider(connectionString);
                break;
            case DatabaseBackend.PostgreSql:
                services.AddPostgresProvider(connectionString);
                break;
            case DatabaseBackend.MySql:
                services.AddMySqlProvider(connectionString);
                break;
            default:
                throw new NotSupportedException($"Backend '{backend}' cannot be a migration target.");
        }

        var provider = services.BuildServiceProvider();

        // Create the schema on an empty target (idempotent — DbUp's journal skips applied scripts).
        IDbUpRunner runner = backend switch
        {
            DatabaseBackend.PostgreSql => new PostgresDbUpRunner(connectionString, NullLogger<DatabaseStartupService>.Instance),
            DatabaseBackend.MySql => new MySqlDbUpRunner(connectionString, NullLogger<DatabaseStartupService>.Instance),
            _ => new SqliteDbUpRunner(connectionString, NullLogger<DatabaseStartupService>.Instance),
        };
        await runner.RunAsync(new Progress<(int, int)>(), ct);

        return new MigrationTarget(provider);
    }
}
