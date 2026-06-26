using System.Threading.Tasks;
using Npgsql;
using Xunit;

namespace BookDB.Data.PostgreSQL.Tests;

public sealed class PostgresFixtureSmokeTests : IClassFixture<PostgresTestDbFixture>
{
    private readonly PostgresTestDbFixture _fixture;

    public PostgresFixtureSmokeTests(PostgresTestDbFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Container_Connects_AndReportsPostgresVersion()
    {
        Assert.SkipUnless(_fixture.IsAvailable, _fixture.SkipReason);

        await using var connection = new NpgsqlConnection(_fixture.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        await using var command = new NpgsqlCommand("SELECT version();", connection);
        var version = (string?)await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(version);
        Assert.Contains("PostgreSQL", version);
    }
}
