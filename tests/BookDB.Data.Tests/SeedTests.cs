using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Xunit;

namespace BookDB.Data.Tests;

public sealed class SeedTests(TestDbFixture fixture) : IClassFixture<TestDbFixture>
{
    private readonly TestDbFixture _fixture = fixture;

    [Fact]
    public async Task AllLookupTablesSeeded()
    {
        var lookupTables = new[]
        {
            "Category", "Condition", "ContributorRole", "Edition", "Format",
            "Language", "Location", "Owner", "Publisher", "PurchasePlace",
            "Rating", "ReadingLevel", "Series", "Source", "Status"
        };

        await using var conn = new SqliteConnection(_fixture.ConnectionString);
        await conn.OpenAsync(TestContext.Current.CancellationToken);

        foreach (var table in lookupTables)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT COUNT(*) FROM \"{table}\"";
            var count = (long)(await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
            Assert.True(count > 0, $"Expected seed data in {table}, but found 0 rows");
        }
    }

    [Fact]
    public async Task DefaultCollectionsExist()
    {
        await using var conn = new SqliteConnection(_fixture.ConnectionString);
        await conn.OpenAsync(TestContext.Current.CancellationToken);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM Collection";
        var count = (long)(await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;

        Assert.True(count > 0, "Expected at least one default Collection, but found 0 rows");
    }

    [Fact]
    public async Task ContributorRolesSeededWithStandardAndComicRoles()
    {
        var requiredCodes = new[]
        {
            "Author", "Editor", "Translator", "Illustrator",
            "Writer", "Penciller", "Inker", "Colorist", "Letterer", "CoverArtist"
        };

        await using var conn = new SqliteConnection(_fixture.ConnectionString);
        await conn.OpenAsync(TestContext.Current.CancellationToken);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Code FROM ContributorRole";

        var foundCodes = new System.Collections.Generic.List<string>();
        await using var reader = await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            foundCodes.Add(reader.GetString(0));
        }

        foreach (var code in requiredCodes)
        {
            Assert.Contains(code, foundCodes);
        }
    }

    [Fact]
    public async Task CategoryCollectionCrossProductSeeded()
    {
        await using var conn = new SqliteConnection(_fixture.ConnectionString);
        await conn.OpenAsync(TestContext.Current.CancellationToken);

        await using var catCmd = conn.CreateCommand();
        catCmd.CommandText = "SELECT COUNT(*) FROM Category";
        var categoryCount = (long)(await catCmd.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;

        await using var colCmd = conn.CreateCommand();
        colCmd.CommandText = "SELECT COUNT(*) FROM Collection";
        var collectionCount = (long)(await colCmd.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;

        await using var ccCmd = conn.CreateCommand();
        ccCmd.CommandText = "SELECT COUNT(*) FROM CategoryCollection";
        var crossProductCount = (long)(await ccCmd.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;

        Assert.Equal(
            categoryCount * collectionCount,
            crossProductCount);
    }
}
