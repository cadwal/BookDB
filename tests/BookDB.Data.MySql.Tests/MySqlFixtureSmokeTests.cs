using System.Threading.Tasks;
using MySqlConnector;
using Xunit;

namespace BookDB.Data.MySql.Tests;

/// <summary>
/// Proves the container starts and the driver connects, on each engine the provider must support.
/// </summary>
public abstract class MySqlFixtureSmokeTests
{
    private readonly MySqlTestDbFixture _fixture;

    protected MySqlFixtureSmokeTests(MySqlTestDbFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Container_Connects_AndReportsVersion()
    {
        Assert.SkipUnless(_fixture.IsAvailable, _fixture.SkipReason);

        await using var connection = new MySqlConnection(_fixture.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        await using var command = new MySqlCommand("SELECT VERSION();", connection);
        var version = (string?)await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);

        Assert.False(string.IsNullOrWhiteSpace(version));
    }
}

public sealed class MySqlServerFixtureSmokeTests : MySqlFixtureSmokeTests, IClassFixture<MySqlServerFixture>
{
    public MySqlServerFixtureSmokeTests(MySqlServerFixture fixture) : base(fixture) { }
}

public sealed class MariaDbFixtureSmokeTests : MySqlFixtureSmokeTests, IClassFixture<MariaDbFixture>
{
    public MariaDbFixtureSmokeTests(MariaDbFixture fixture) : base(fixture) { }
}
