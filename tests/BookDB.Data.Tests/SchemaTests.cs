using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Xunit;

namespace BookDB.Data.Tests;

public sealed class SchemaTests(TestDbFixture fixture) : IClassFixture<TestDbFixture>
{
    private readonly TestDbFixture _fixture = fixture;

    [Fact]
    public async Task AllBookColumnsExist()
    {
        var expectedColumns = new[]
        {
            "BookId", "CollectionId", "Title", "Subtitle", "PublisherId",
            "PubPlace", "PubDate", "CopyrightDate", "FormatId", "EditionId",
            "Pages", "Copies", "Isbn", "LanguageId", "SeriesId",
            "SeriesNumber", "ReadCount", "RatingId", "ConditionId", "LocationId",
            "OwnerId", "StatusId", "Signed", "OutOfPrint", "Favorite",
            "Keywords", "Comments", "BookInfo", "PurchasePrice", "PurchasePlaceId",
            "ListPrice", "SourceId", "ExternalId", "MediaLink", "Display",
            "ReadingLevelId", "Added", "Updated",
            "Issn", "Lccn", "DeweyDecimal", "CallNumber", "Dimensions", "Weight",
            "ItemValue", "ValuationDate",
            "AmazonNewValue", "AmazonUsedValue", "AmazonCollectibleValue",
            "AmazonNewCount", "AmazonUsedCount", "AmazonCollectibleCount",
            "SalesRank", "LexileLevel"
        };

        var foundColumns = new List<string>();

        await using var conn = new SqliteConnection(_fixture.ConnectionString);
        await conn.OpenAsync(TestContext.Current.CancellationToken);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA table_info(Book)";

        await using var reader = await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            foundColumns.Add(reader.GetString(1));
        }

        Assert.True(
            foundColumns.Count >= 38,
            $"Expected at least 38 columns, found {foundColumns.Count}: {string.Join(", ", foundColumns)}");

        foreach (var expected in expectedColumns)
        {
            Assert.Contains(expected, foundColumns);
        }
    }

    [Fact]
    public async Task JoinTablesExist()
    {
        var requiredTables = new[] { "BookContributor", "BookCategory", "CategoryCollection" };
        var existingTables = new List<string>();

        await using var conn = new SqliteConnection(_fixture.ConnectionString);
        await conn.OpenAsync(TestContext.Current.CancellationToken);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table'";

        await using var reader = await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            existingTables.Add(reader.GetString(0));
        }

        foreach (var table in requiredTables)
        {
            Assert.Contains(table, existingTables);
        }
    }

    [Fact]
    public async Task BookImageTableExistsWithBlobColumn()
    {
        var tables = new List<string>();
        await using var conn = new SqliteConnection(_fixture.ConnectionString);
        await conn.OpenAsync(TestContext.Current.CancellationToken);

        await using var tablesCmd = conn.CreateCommand();
        tablesCmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table'";
        await using var tablesReader = await tablesCmd.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        while (await tablesReader.ReadAsync(TestContext.Current.CancellationToken))
            tables.Add(tablesReader.GetString(0));

        Assert.Contains("BookImage", tables);

        var columns = new List<string>();
        await using var colCmd = conn.CreateCommand();
        colCmd.CommandText = "PRAGMA table_info(BookImage)";
        await using var colReader = await colCmd.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        while (await colReader.ReadAsync(TestContext.Current.CancellationToken))
            columns.Add(colReader.GetString(1));

        Assert.Contains("BookImageId", columns);
        Assert.Contains("BookId", columns);
        Assert.Contains("ImageData", columns);
        Assert.Contains("MimeType", columns);
        Assert.Contains("IsPrimary", columns);
    }

    [Fact]
    public async Task CollectionTableAndBookFkExist()
    {
        var tables = new List<string>();
        var bookColumns = new List<string>();

        await using var conn = new SqliteConnection(_fixture.ConnectionString);
        await conn.OpenAsync(TestContext.Current.CancellationToken);

        await using var tablesCmd = conn.CreateCommand();
        tablesCmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table'";
        await using var tablesReader = await tablesCmd.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        while (await tablesReader.ReadAsync(TestContext.Current.CancellationToken))
        {
            tables.Add(tablesReader.GetString(0));
        }

        await using var colsCmd = conn.CreateCommand();
        colsCmd.CommandText = "PRAGMA table_info(Book)";
        await using var colsReader = await colsCmd.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        while (await colsReader.ReadAsync(TestContext.Current.CancellationToken))
        {
            bookColumns.Add(colsReader.GetString(1));
        }

        Assert.Contains("Collection", tables);
        Assert.Contains("CollectionId", bookColumns);
    }

    [Fact]
    public async Task NamingConventionsFollowed()
    {
        var tables = new List<string>();
        var indexes = new List<string>();

        await using var conn = new SqliteConnection(_fixture.ConnectionString);
        await conn.OpenAsync(TestContext.Current.CancellationToken);

        await using var tablesCmd = conn.CreateCommand();
        tablesCmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table'";
        await using var tablesReader = await tablesCmd.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        while (await tablesReader.ReadAsync(TestContext.Current.CancellationToken))
        {
            tables.Add(tablesReader.GetString(0));
        }

        await using var indexCmd = conn.CreateCommand();
        indexCmd.CommandText = "SELECT name FROM sqlite_master WHERE type='index' AND name LIKE 'IX_%'";
        await using var indexReader = await indexCmd.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        while (await indexReader.ReadAsync(TestContext.Current.CancellationToken))
        {
            indexes.Add(indexReader.GetString(0));
        }

        // Assert no plural table names (Books, People, etc.)
        Assert.DoesNotContain("Books", tables);
        Assert.DoesNotContain("People", tables);

        // Assert at least 5 IX_ prefixed indexes
        Assert.True(
            indexes.Count >= 5,
            $"Expected at least 5 IX_ indexes, found {indexes.Count}");
    }

    [Fact]
    public async Task BookImageTypeTableExists()
    {
        var columns = new List<string>();

        await using var conn = new SqliteConnection(_fixture.ConnectionString);
        await conn.OpenAsync(TestContext.Current.CancellationToken);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA table_info('BookImageType')";

        await using var reader = await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
            columns.Add(reader.GetString(1));

        Assert.Contains("BookImageTypeId", columns);
        Assert.Contains("TypeName", columns);
    }

    [Fact]
    public async Task BookImageTypeSeeded()
    {
        await using var conn = new SqliteConnection(_fixture.ConnectionString);
        await conn.OpenAsync(TestContext.Current.CancellationToken);

        await using var countCmd = conn.CreateCommand();
        countCmd.CommandText = "SELECT COUNT(*) FROM BookImageType";
        var count = (long)(await countCmd.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
        Assert.Equal(5L, count);

        await using var coverCmd = conn.CreateCommand();
        coverCmd.CommandText = "SELECT TypeName FROM BookImageType WHERE BookImageTypeId = 0";
        var coverName = (string)(await coverCmd.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
        Assert.Equal("Cover", coverName);

        await using var thumbCmd = conn.CreateCommand();
        thumbCmd.CommandText = "SELECT TypeName FROM BookImageType WHERE BookImageTypeId = 1";
        var thumbName = (string)(await thumbCmd.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
        Assert.Equal("Thumbnail", thumbName);
    }

    [Fact]
    public async Task BookImageHasBookImageTypeIdColumn()
    {
        await using var conn = new SqliteConnection(_fixture.ConnectionString);
        await conn.OpenAsync(TestContext.Current.CancellationToken);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA table_info('BookImage')";

        string? defaultValue = null;
        await using var reader = await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            if (reader.GetString(1) == "BookImageTypeId")
            {
                defaultValue = reader.IsDBNull(4) ? null : reader.GetString(4);
                break;
            }
        }

        Assert.NotNull(defaultValue);
        Assert.Equal("0", defaultValue);
    }

    [Fact]
    public async Task BookVolumeTableExists()
    {
        var columns = new List<string>();

        await using var conn = new SqliteConnection(_fixture.ConnectionString);
        await conn.OpenAsync(TestContext.Current.CancellationToken);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA table_info('BookVolume')";

        await using var reader = await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
            columns.Add(reader.GetString(1));

        Assert.Contains("BookVolumeId", columns);
        Assert.Contains("BookId", columns);
        Assert.Contains("VolumeNumber", columns);
    }

    [Fact]
    public async Task BookChapterTableExists()
    {
        var columns = new List<string>();

        await using var conn = new SqliteConnection(_fixture.ConnectionString);
        await conn.OpenAsync(TestContext.Current.CancellationToken);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA table_info('BookChapter')";

        await using var reader = await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
            columns.Add(reader.GetString(1));

        Assert.Contains("BookChapterId", columns);
        Assert.Contains("BookVolumeId", columns);
        Assert.Contains("ChapterNumber", columns);
    }

    [Fact]
    public async Task BorrowerStatusTableExists()
    {
        var columns = new List<string>();

        await using var conn = new SqliteConnection(_fixture.ConnectionString);
        await conn.OpenAsync(TestContext.Current.CancellationToken);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA table_info('BorrowerStatus')";

        await using var reader = await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
            columns.Add(reader.GetString(1));

        Assert.True(columns.Count > 0, "BorrowerStatus table should exist");

        await using var countCmd = conn.CreateCommand();
        countCmd.CommandText = "SELECT COUNT(*) FROM BorrowerStatus";
        var count = (long)(await countCmd.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
        Assert.Equal(2L, count);
    }

    [Fact]
    public async Task BorrowerTableExists()
    {
        var columns = new List<string>();

        await using var conn = new SqliteConnection(_fixture.ConnectionString);
        await conn.OpenAsync(TestContext.Current.CancellationToken);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA table_info('Borrower')";

        await using var reader = await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
            columns.Add(reader.GetString(1));

        Assert.Contains("BorrowerId", columns);
        Assert.Contains("StatusId", columns);
        Assert.Contains("FirstName", columns);
        Assert.Contains("LastName", columns);
        Assert.Contains("Email", columns);
    }

    [Fact]
    public async Task LoanTableExists()
    {
        var columns = new List<string>();

        await using var conn = new SqliteConnection(_fixture.ConnectionString);
        await conn.OpenAsync(TestContext.Current.CancellationToken);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA table_info('Loan')";

        await using var reader = await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
            columns.Add(reader.GetString(1));

        Assert.Contains("LoanId", columns);
        Assert.Contains("BookId", columns);
        Assert.Contains("BorrowerId", columns);
        Assert.Contains("LoanedDate", columns);
        Assert.Contains("ReturnedDate", columns);
    }

    [Fact]
    public async Task SettingsTableExists()
    {
        var columns = new List<string>();

        await using var conn = new SqliteConnection(_fixture.ConnectionString);
        await conn.OpenAsync(TestContext.Current.CancellationToken);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA table_info(Settings)";
        await using var reader = await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            columns.Add(reader.GetString(1));
        }

        Assert.Contains("Key", columns);
        Assert.Contains("Value", columns);
    }

    [Fact]
    public async Task BatchQueueItemHasReviewAndFailureColumns()
    {
        var columns = new List<string>();

        await using var conn = new SqliteConnection(_fixture.ConnectionString);
        await conn.OpenAsync(TestContext.Current.CancellationToken);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA table_info(BatchQueueItem)";
        await using var reader = await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
            columns.Add(reader.GetString(1));

        Assert.Contains("ForceReview", columns);
        Assert.Contains("FailureCode", columns);
    }

    [Fact]
    public async Task PersonCleanupIgnoreTableExistsWithCascade()
    {
        var columns = new List<string>();

        await using var conn = new SqliteConnection(_fixture.ConnectionString);
        await conn.OpenAsync(TestContext.Current.CancellationToken);

        await using var colCmd = conn.CreateCommand();
        colCmd.CommandText = "PRAGMA table_info(PersonCleanupIgnore)";
        await using var colReader = await colCmd.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        while (await colReader.ReadAsync(TestContext.Current.CancellationToken))
            columns.Add(colReader.GetString(1));

        Assert.Contains("PersonCleanupIgnoreId", columns);
        Assert.Contains("PersonId", columns);
        Assert.Contains("Kind", columns);
        Assert.Contains("ProposedContent", columns);
        Assert.Contains("CreatedAt", columns);

        string? fkTable = null;
        string? fkOnDelete = null;
        await using var fkCmd = conn.CreateCommand();
        fkCmd.CommandText = "PRAGMA foreign_key_list(PersonCleanupIgnore)";
        await using var fkReader = await fkCmd.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        while (await fkReader.ReadAsync(TestContext.Current.CancellationToken))
        {
            fkTable = fkReader.GetString(2);
            fkOnDelete = fkReader.GetString(6);
        }

        Assert.Equal("Person", fkTable);
        Assert.Equal("CASCADE", fkOnDelete);
    }

    [Fact]
    public async Task PersonAndContributorRoleTablesExist()
    {
        var tables = new List<string>();

        await using var conn = new SqliteConnection(_fixture.ConnectionString);
        await conn.OpenAsync(TestContext.Current.CancellationToken);

        await using var tablesCmd = conn.CreateCommand();
        tablesCmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table'";
        await using var tablesReader = await tablesCmd.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        while (await tablesReader.ReadAsync(TestContext.Current.CancellationToken))
        {
            tables.Add(tablesReader.GetString(0));
        }

        Assert.Contains("Person", tables);
        Assert.Contains("ContributorRole", tables);
        Assert.Contains("BookContributor", tables);

        var roleColumns = new List<string>();
        await using var colsCmd = conn.CreateCommand();
        colsCmd.CommandText = "PRAGMA table_info(ContributorRole)";
        await using var colsReader = await colsCmd.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        while (await colsReader.ReadAsync(TestContext.Current.CancellationToken))
        {
            roleColumns.Add(colsReader.GetString(1));
        }

        Assert.Contains("Code", roleColumns);
        Assert.Contains("DisplayName", roleColumns);
    }
}
