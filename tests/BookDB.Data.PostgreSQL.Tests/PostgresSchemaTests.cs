using System;
using System.Threading;
using System.Threading.Tasks;
using BookDB.Data;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;

namespace BookDB.Data.PostgreSQL.Tests;

public sealed class PostgresSchemaTests : IClassFixture<PostgresTestDbFixture>
{
    private readonly PostgresTestDbFixture _fixture;

    public PostgresSchemaTests(PostgresTestDbFixture fixture) => _fixture = fixture;

    // Applying the schema is idempotent — DbUp's journal skips already-applied scripts — so each test can
    // call this and then assert against the resulting schema regardless of execution order.
    private async Task ApplySchemaAsync(CancellationToken ct)
    {
        var runner = new PostgresDbUpRunner(_fixture.ConnectionString, NullLogger<DatabaseStartupService>.Instance);
        await runner.RunAsync(new Progress<(int applied, int total)>(), ct);
    }

    private async Task<object?> ScalarAsync(string sql, CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(_fixture.ConnectionString);
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand(sql, connection);
        return await command.ExecuteScalarAsync(ct);
    }

    private async Task<string?> ColumnTypeAsync(string table, string column, CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(_fixture.ConnectionString);
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand(
            "SELECT data_type FROM information_schema.columns WHERE table_name = @t AND column_name = @c", connection);
        command.Parameters.AddWithValue("t", table);
        command.Parameters.AddWithValue("c", column);
        return (string?)await command.ExecuteScalarAsync(ct);
    }

    [Fact]
    public async Task Schema_Applies_AndCreatesTables()
    {
        Assert.SkipUnless(_fixture.IsAvailable, _fixture.SkipReason);
        var ct = TestContext.Current.CancellationToken;

        await ApplySchemaAsync(ct);

        Assert.True((bool)(await ScalarAsync("SELECT to_regclass('public.\"Book\"') IS NOT NULL", ct))!);
        Assert.True((bool)(await ScalarAsync("SELECT to_regclass('public.\"ClientSession\"') IS NOT NULL", ct))!);
        Assert.True((bool)(await ScalarAsync("SELECT to_regclass('public.\"PersonCleanupIgnore\"') IS NOT NULL", ct))!);
    }

    [Fact]
    public async Task Schema_UsesNativePostgresColumnTypes()
    {
        Assert.SkipUnless(_fixture.IsAvailable, _fixture.SkipReason);
        var ct = TestContext.Current.CancellationToken;

        await ApplySchemaAsync(ct);

        Assert.Equal("boolean", await ColumnTypeAsync("Book", "Signed", ct));
        Assert.Equal("timestamp without time zone", await ColumnTypeAsync("Book", "Added", ct));
        Assert.Equal("bytea", await ColumnTypeAsync("BookImage", "ImageData", ct));
        Assert.Equal("tsvector", await ColumnTypeAsync("Book", "SearchVector", ct));
        Assert.Equal("boolean", await ColumnTypeAsync("BatchQueueItem", "ForceReview", ct));
        Assert.Equal("text", await ColumnTypeAsync("BatchQueueItem", "FailureCode", ct));
    }

    [Fact]
    public async Task DbUp_AppliesOnlyPostgresScripts()
    {
        Assert.SkipUnless(_fixture.IsAvailable, _fixture.SkipReason);
        var ct = TestContext.Current.CancellationToken;

        await ApplySchemaAsync(ct);

        var count = Convert.ToInt64(await ScalarAsync("SELECT count(*) FROM schemaversions", ct));
        var firstScript = (string?)await ScalarAsync(
            "SELECT scriptname FROM schemaversions ORDER BY schemaversionsid LIMIT 1", ct);
        var foreignScripts = Convert.ToInt64(await ScalarAsync(
            "SELECT count(*) FROM schemaversions WHERE scriptname NOT LIKE '%PostgreSQL%'", ct));

        Assert.Equal(2, count);
        Assert.Equal(0, foreignScripts);
        Assert.NotNull(firstScript);
        Assert.Contains("V001_CreateSchema", firstScript);
    }
}
