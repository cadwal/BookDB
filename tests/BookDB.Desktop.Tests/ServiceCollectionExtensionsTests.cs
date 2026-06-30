using System.Linq;
using BookDB.Data.Interfaces;
using BookDB.Data.MySql;
using BookDB.Desktop;
using BookDB.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BookDB.Desktop.Tests;

/// <summary>
/// The backend registration switch and the backend-independent prober wiring. MySQL is selectable end-to-end
/// from a hand-written config.json (no UI yet), and a user on any backend can probe a remote server before
/// switching to it — so both remote probers must register regardless of the active backend.
/// </summary>
public sealed class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddBookDbDataServices_MySqlBackend_RegistersTheMySqlProvider()
    {
        var services = new ServiceCollection();
        var appSettings = new AppSettings
        {
            Backend = DatabaseBackend.MySql,
            ConnectionString = "Server=localhost;Database=unused",
        };

        // Before the third case existed this threw NotSupportedException; now it resolves to the MySQL provider.
        services.AddBookDbDataServices(appSettings);

        Assert.Contains(services, d =>
            d.ServiceType == typeof(IBookSearchProvider) &&
            d.ImplementationType == typeof(MySqlBookSearchProvider));
    }

    [Theory]
    [InlineData(DatabaseBackend.Sqlite, "Data Source=:memory:")]
    [InlineData(DatabaseBackend.PostgreSql, "Host=localhost;Database=unused")]
    [InlineData(DatabaseBackend.MySql, "Server=localhost;Database=unused")]
    public void AddBookDbDataServices_RegistersBothRemoteProbers_RegardlessOfBackend(
        DatabaseBackend backend, string connectionString)
    {
        var services = new ServiceCollection();
        services.AddBookDbDataServices(new AppSettings { Backend = backend, ConnectionString = connectionString });

        Assert.Contains(services, d => d.ServiceType == typeof(IMySqlConnectionProber));
        Assert.Contains(services, d => d.ServiceType == typeof(IPostgresConnectionProber));
    }
}
