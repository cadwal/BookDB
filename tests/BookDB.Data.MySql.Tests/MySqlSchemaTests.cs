using System;
using System.Threading;
using System.Threading.Tasks;
using BookDB.Data;
using Microsoft.Extensions.Logging.Abstractions;
using MySqlConnector;
using Xunit;

namespace BookDB.Data.MySql.Tests;

/// <summary>
/// Runs the DbUp schema scripts against a real container and verifies the create-from-scratch schema lands with
/// native MySQL types. Exercised on both engines via the concrete subclasses at the bottom.
/// </summary>
public abstract class MySqlSchemaTests
{
    private readonly MySqlTestDbFixture _fixture;

    protected MySqlSchemaTests(MySqlTestDbFixture fixture) => _fixture = fixture;

    // Applying the schema is idempotent — DbUp's journal skips already-applied scripts — so each test can call
    // this and then assert against the resulting schema regardless of execution order.
    private async Task ApplySchemaAsync(CancellationToken ct)
    {
        var runner = new MySqlDbUpRunner(_fixture.ConnectionString, NullLogger<DatabaseStartupService>.Instance);
        await runner.RunAsync(new Progress<(int applied, int total)>(), ct);
    }

    private async Task<object?> ScalarAsync(string sql, CancellationToken ct)
    {
        await using var connection = new MySqlConnection(_fixture.ConnectionString);
        await connection.OpenAsync(ct);
        await using var command = new MySqlCommand(sql, connection);
        return await command.ExecuteScalarAsync(ct);
    }

    private Task<object?> ColumnTypeAsync(string table, string column, CancellationToken ct) =>
        ScalarAsync(
            $"SELECT COLUMN_TYPE FROM information_schema.columns " +
            $"WHERE table_schema = DATABASE() AND table_name = '{table}' AND column_name = '{column}'", ct);

    [Fact]
    public async Task Schema_Applies_AndCreatesTables()
    {
        Assert.SkipUnless(_fixture.IsAvailable, _fixture.SkipReason);
        var ct = TestContext.Current.CancellationToken;

        await ApplySchemaAsync(ct);

        Assert.Equal(1L, Convert.ToInt64(await ScalarAsync(
            "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = DATABASE() AND table_name = 'Book'", ct)));
        Assert.Equal(1L, Convert.ToInt64(await ScalarAsync(
            "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = DATABASE() AND table_name = 'ClientSession'", ct)));
    }

    [Fact]
    public async Task Schema_UsesNativeMySqlColumnTypes()
    {
        Assert.SkipUnless(_fixture.IsAvailable, _fixture.SkipReason);
        var ct = TestContext.Current.CancellationToken;

        await ApplySchemaAsync(ct);

        Assert.Equal("tinyint(1)", (string?)await ColumnTypeAsync("Book", "Signed", ct));
        Assert.Equal("datetime(6)", (string?)await ColumnTypeAsync("Book", "Added", ct));
        Assert.Equal("longblob", (string?)await ColumnTypeAsync("BookImage", "ImageData", ct));
        Assert.Equal("tinyint(1)", (string?)await ColumnTypeAsync("BatchQueueItem", "ForceReview", ct));
        Assert.Equal(1L, Convert.ToInt64(await ScalarAsync(
            "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = DATABASE() AND table_name = 'PersonCleanupIgnore'", ct)));
    }

    [Fact]
    public async Task Schema_CreatesFullTextSearchIndex()
    {
        Assert.SkipUnless(_fixture.IsAvailable, _fixture.SkipReason);
        var ct = TestContext.Current.CancellationToken;

        await ApplySchemaAsync(ct);

        var fulltextColumns = Convert.ToInt64(await ScalarAsync(
            "SELECT COUNT(*) FROM information_schema.statistics " +
            "WHERE table_schema = DATABASE() AND table_name = 'Book' AND index_name = 'IX_Book_SearchVector' " +
            "AND index_type = 'FULLTEXT'", ct));

        // One row per indexed column: Title, Subtitle, Keywords, Comments, BookInfo, ExternalId.
        Assert.Equal(6L, fulltextColumns);
    }

    [Fact]
    public async Task DbUp_AppliesAllMySqlScripts_AndIsIdempotent()
    {
        Assert.SkipUnless(_fixture.IsAvailable, _fixture.SkipReason);
        var ct = TestContext.Current.CancellationToken;

        await ApplySchemaAsync(ct);
        // A second run must be a no-op: DbUp's journal already records every script.
        await ApplySchemaAsync(ct);

        // DbUp's journal table name casing varies by engine on Linux; resolve it case-insensitively.
        var journal = (string)(await ScalarAsync(
            "SELECT table_name FROM information_schema.tables " +
            "WHERE table_schema = DATABASE() AND LOWER(table_name) = 'schemaversions'", ct))!;

        var applied = Convert.ToInt64(await ScalarAsync($"SELECT COUNT(*) FROM `{journal}`", ct));
        Assert.Equal(3L, applied);

        var firstScript = (string?)await ScalarAsync(
            $"SELECT scriptname FROM `{journal}` ORDER BY scriptname LIMIT 1", ct);
        Assert.NotNull(firstScript);
        Assert.Contains("MySql", firstScript);
        Assert.Contains("V001_CreateSchema", firstScript);
    }
}

public sealed class MySqlServerSchemaTests : MySqlSchemaTests, IClassFixture<MySqlServerFixture>
{
    public MySqlServerSchemaTests(MySqlServerFixture fixture) : base(fixture) { }
}

public sealed class MariaDbSchemaTests : MySqlSchemaTests, IClassFixture<MariaDbFixture>
{
    public MariaDbSchemaTests(MariaDbFixture fixture) : base(fixture) { }
}
