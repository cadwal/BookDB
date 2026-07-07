using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BookDB.Data.Interfaces;
using BookDB.Data.Sqlite;
using BookDB.Desktop.Services;
using BookDB.Logic;
using BookDB.Models;
using DbUp;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace BookDB.Desktop.UITests;

/// <summary>
/// A per-test DI host over a fresh temp SQLite database (migrated with DbUp), mirroring the app's composition
/// (<see cref="ServiceCollectionExtensions"/> + Logic registrations) but with the destructive/external edges
/// replaced by fakes. Dispose deletes the temp database.
/// </summary>
public sealed class TestHost : IDisposable
{
    public IServiceProvider Services { get; }

    public T Resolve<T>() where T : notnull => Services.GetRequiredService<T>();

    private readonly ServiceProvider _provider;
    private readonly string _dir;

    private TestHost(ServiceProvider provider, string dir)
    {
        _provider = provider;
        Services = provider;
        _dir = dir;
    }

    /// <param name="configureOverrides">
    /// Optional per-test service overrides applied last, so a flow can replace a lazy edge (file picker, cover/
    /// metadata HTTP, print) with a fake — the container resolves the last registration for a service.
    /// </param>
    public static TestHost Create(Action<IServiceCollection>? configureOverrides = null)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"bookdb_ui_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var dbPath = Path.Combine(dir, "library.db");
        var configPath = Path.Combine(dir, "config.json");
        var connectionString = $"Data Source={dbPath}";

        var upgrader = SqliteExtensions.SqliteDatabase(DeployChanges.To, connectionString)
            .WithScriptsEmbeddedInAssembly(
                Assembly.GetAssembly(typeof(SqliteDbUpRunner))!, name => name.Contains(".Migrations."))
            .LogToNowhere()
            .Build();
        var result = upgrader.PerformUpgrade();
        if (!result.Successful)
            throw new InvalidOperationException($"DbUp migration failed: {result.Error}");

        var appSettings = new AppSettings
        {
            Backend = DatabaseBackend.Sqlite,
            ConnectionString = connectionString,
            SqliteLibraryPath = dbPath,
            ConfigPath = configPath,
        };

        var services = new ServiceCollection();
        services.AddSingleton<IBootstrapConfigService>(_ => new BootstrapConfigService(configPath));
        services.AddBookDbDataServices(appSettings);
        services.AddBookDbLogicServices();
        services.AddBookDbDesktopServices();
        services.AddBookDbViewModels();
        services.AddBookDbViews();

        // Replace the destructive edge: a flow that triggers a restart must never kill the test process.
        // (External edges — file picker, cover/metadata HTTP, print — are lazy, so they're faked per-flow as needed.)
        services.AddSingleton<IApplicationRestartService>(Substitute.For<IApplicationRestartService>());

        // Secrets must never touch the machine's real OS credential manager (which AddBookDbDesktopServices
        // registers), and keyring availability must not depend on the runner (headless CI has none).
        services.AddSingleton<ISecretStore>(new InMemorySecretStore());
        services.AddSingleton(SecretStoreAvailability.Available);

        configureOverrides?.Invoke(services);

        var provider = services.BuildServiceProvider();
        return new TestHost(provider, dir);
    }

    public void Dispose()
    {
        _provider.Dispose();
        SqliteConnection.ClearAllPools(); // release the file handle before deleting
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }
}

/// <summary>Per-host secret store, so saved-password behaviour is testable without the OS credential manager.</summary>
public sealed class InMemorySecretStore : ISecretStore
{
    private readonly Dictionary<string, string> _secrets = [];

    public string? Get(string account) => _secrets.GetValueOrDefault(account);

    public void Set(string account, string secret) => _secrets[account] = secret;

    public void Delete(string account) => _secrets.Remove(account);
}
