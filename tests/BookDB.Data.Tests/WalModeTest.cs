using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Xunit;

namespace BookDB.Data.Tests;

public sealed class WalModeTest(TestDbFixture fixture) : IClassFixture<TestDbFixture>
{
    private readonly TestDbFixture _fixture = fixture;

    [Fact]
    public async Task WalModeIsEnabled()
    {
        await using var conn = new SqliteConnection(_fixture.ConnectionString);
        await conn.OpenAsync(TestContext.Current.CancellationToken);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode";

        var result = await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken);

        Assert.Equal("wal", result?.ToString()?.ToLowerInvariant());
    }
}
