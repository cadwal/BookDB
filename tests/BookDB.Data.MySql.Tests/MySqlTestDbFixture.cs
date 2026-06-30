using System;
using System.Threading.Tasks;
using DotNet.Testcontainers.Containers;
using Testcontainers.MariaDb;
using Testcontainers.MySql;
using Xunit;

namespace BookDB.Data.MySql.Tests;

/// <summary>
/// Starts a disposable database container for the MySQL provider integration tests. The provider must work on
/// both engines that speak the MySQL protocol, so the suite runs against two pinned images via the concrete
/// fixtures below. When no Docker daemon is reachable (e.g. the WSL Docker Engine is not running), startup fails
/// gracefully and <see cref="IsAvailable"/> stays false so tests skip rather than fail.
/// </summary>
public abstract class MySqlTestDbFixture : IAsyncLifetime
{
    private IContainer? _container;

    public bool IsAvailable { get; private set; }

    public string SkipReason { get; private set; } = string.Empty;

    public string ConnectionString { get; private set; } = string.Empty;

    /// <summary>Engine label for skip messages (e.g. "MySQL", "MariaDB").</summary>
    protected abstract string EngineName { get; }

    /// <summary>Builds the engine-specific container. Build() resolves the Docker endpoint eagerly, so it runs
    /// inside the availability guard.</summary>
    protected abstract IContainer BuildContainer();

    public async ValueTask InitializeAsync()
    {
        SkipReason = $"Docker is not available; skipping {EngineName} tests.";
        try
        {
            _container = BuildContainer();
            await _container.StartAsync();
            ConnectionString = ((IDatabaseContainer)_container).GetConnectionString();
            IsAvailable = true;
        }
        catch (Exception ex)
        {
            // No reachable Docker daemon — record the reason and let tests skip cleanly. Use the exception type,
            // not its message: the Testcontainers message is multi-line and repeats per skipped test, flooding
            // the CI log with identical socket errors on runners that have no Docker.
            IsAvailable = false;
            SkipReason = $"Docker is not available; skipping {EngineName} tests ({ex.GetType().Name}).";
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_container is not null)
            await _container.DisposeAsync();
    }
}

public sealed class MySqlServerFixture : MySqlTestDbFixture
{
    protected override string EngineName => "MySQL";
    protected override IContainer BuildContainer() => new MySqlBuilder("mysql:8.0").Build();
}

public sealed class MariaDbFixture : MySqlTestDbFixture
{
    protected override string EngineName => "MariaDB";
    protected override IContainer BuildContainer() => new MariaDbBuilder("mariadb:11").Build();
}
