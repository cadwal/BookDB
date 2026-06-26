using System;
using System.Threading.Tasks;
using Testcontainers.PostgreSql;
using Xunit;

namespace BookDB.Data.PostgreSQL.Tests;

/// <summary>
/// Starts a disposable PostgreSQL container (pinned <c>postgres:16-alpine</c>) for the provider
/// integration tests. When no Docker daemon is reachable (e.g. the WSL Docker Engine is not running),
/// startup fails gracefully and <see cref="IsAvailable"/> stays false so tests skip rather than fail.
/// </summary>
public sealed class PostgresTestDbFixture : IAsyncLifetime
{
    private PostgreSqlContainer? _container;

    public bool IsAvailable { get; private set; }

    public string SkipReason { get; private set; } = "Docker is not available; skipping PostgreSQL tests.";

    public string ConnectionString { get; private set; } = string.Empty;

    public async ValueTask InitializeAsync()
    {
        try
        {
            // Build() resolves the Docker endpoint eagerly, so it must be inside the guard too — it throws
            // DockerUnavailableException when no daemon is reachable.
            _container = new PostgreSqlBuilder("postgres:16-alpine").Build();
            await _container.StartAsync();
            ConnectionString = _container.GetConnectionString();
            IsAvailable = true;
        }
        catch (Exception ex)
        {
            // No reachable Docker daemon — record the reason and let tests skip cleanly. Use the exception
            // type, not its message: the Testcontainers message is multi-line and repeats per skipped test,
            // flooding the CI log with identical socket errors on runners that have no Docker (Windows/macOS).
            IsAvailable = false;
            SkipReason = $"Docker is not available; skipping PostgreSQL tests ({ex.GetType().Name}).";
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_container is not null)
            await _container.DisposeAsync();
    }
}
