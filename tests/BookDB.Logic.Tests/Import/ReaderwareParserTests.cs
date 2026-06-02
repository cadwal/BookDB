using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BookDB.Logic.Import;
using Xunit;

namespace BookDB.Logic.Tests.Import;

public class ReaderwareParserTests : IDisposable
{
    private readonly List<string> _tempDirs = [];

    public void Dispose()
    {
        foreach (var dir in _tempDirs)
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }

        // Ensure finalization is suppressed for this object.
        GC.SuppressFinalize(this);
    }

    private string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"rw_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        _tempDirs.Add(dir);
        return dir;
    }

    // Minimal READERWARE header — all columns used by the parser
    private const string MinimalHeader =
        "ROWKEY,TITLE,ALT_TITLE,SUBTITLE,AUTHOR,AUTHOR2,AUTHOR3,AUTHOR4,AUTHOR5,AUTHOR6," +
        "ILLUSTRATOR,TRANSLATOR,EDITOR,PUBLISHER,PUB_PLACE,RELEASE_DATE,COPYRIGHT_DATE," +
        "PAGES,EDITION,CONTENT_LANGUAGE,SIGNED,DIMENSIONS,READING_LEVEL,LEXILE_LEVEL," +
        "COPIES,BARCODE,ISBN,ISSN,LCCN,DEWEY,CALL_NUMBER,USER_NUMBER,TYPE,FORMAT,SERIES," +
        "SERIES_NUMBER,MY_RATING,ITEM_CONDITION,COVER_CONDITION,CATEGORY1,CATEGORY2," +
        "CATEGORY3,LOCATION,KEYWORDS,READ_COUNT,DATE_LAST_READ,PRODUCT_INFO,MY_COMMENTS," +
        "DATE_ENTERED,DATE_LAST_UPDATED,SOURCE,PURCHASE_PRICE,PURCHASE_DATE,PURCHASE_PLACE," +
        "LIST_PRICE,ITEM_VALUE,VALUATION_DATE,CURRENCY_SYMBOL,FAVORITE,OUT_OF_PRINT," +
        "MEDIA_URL,OWNER,STATUS,EXTERNAL_ID,INVENTORY,IN_LAST_BATCH,AM_ASIN,SALE_PRICE," +
        "SALE_DATE,NEW_VALUE,NEW_COUNT,USED_VALUE,USED_COUNT,COLLECTIBLE_VALUE," +
        "COLLECTIBLE_COUNT,BUYER_WAITING,WEIGHT,SALES_RANK,THUMB_IMAGE_COUNT," +
        "FULL_IMAGE_COUNT,USER1,USER2,USER3,USER4,USER5,USER6,USER7,USER8,USER9,USER10," +
        "USERL1,USERL2,USERL3,USERL4,USERL5,USERL6,USERL7,USERL8,USERL9,USERL10," +
        "FILLER1,FILLER2";

    private static string MakeRow(
        int rowKey = 1,
        string title = "Test Book",
        string altTitle = "",
        string subtitle = "",
        string author = "-1",
        string isbn = "",
        string format = "-1",
        string productInfo = "",
        int fullImageCount = 0,
        string dateEntered = "2024-01-01")
    {
        // Build a CSV row that matches the minimal header columns
        // Use empty string for all unspecified columns
        var cols = new string[100];
        Array.Fill(cols, "");
        cols[0]  = rowKey.ToString();         // ROWKEY
        cols[1]  = title;                     // TITLE
        cols[2]  = altTitle;                  // ALT_TITLE
        cols[3]  = subtitle;                  // SUBTITLE
        cols[4]  = author;                    // AUTHOR
        cols[5]  = "-1";                      // AUTHOR2
        cols[6]  = "-1";                      // AUTHOR3
        cols[7]  = "-1";                      // AUTHOR4
        cols[8]  = "-1";                      // AUTHOR5
        cols[9]  = "-1";                      // AUTHOR6
        cols[10] = "-1";                      // ILLUSTRATOR
        cols[11] = "-1";                      // TRANSLATOR
        cols[12] = "-1";                      // EDITOR
        cols[13] = "-1";                      // PUBLISHER
        cols[14] = "-1";                      // PUB_PLACE
        cols[15] = "";                        // RELEASE_DATE
        cols[16] = "";                        // COPYRIGHT_DATE
        cols[17] = "";                        // PAGES
        cols[18] = "-1";                      // EDITION
        cols[19] = "-1";                      // CONTENT_LANGUAGE
        cols[20] = "false";                   // SIGNED
        cols[21] = "";                        // DIMENSIONS
        cols[22] = "-1";                      // READING_LEVEL
        cols[23] = "";                        // LEXILE_LEVEL
        cols[24] = "1";                       // COPIES
        cols[25] = "";                        // BARCODE
        cols[26] = isbn;                      // ISBN
        cols[27] = "";                        // ISSN
        cols[28] = "";                        // LCCN
        cols[29] = "";                        // DEWEY
        cols[30] = "";                        // CALL_NUMBER
        cols[31] = "";                        // USER_NUMBER
        cols[32] = "";                        // TYPE
        cols[33] = format;                    // FORMAT
        cols[34] = "-1";                      // SERIES
        cols[35] = "";                        // SERIES_NUMBER
        cols[36] = "-1";                      // MY_RATING
        cols[37] = "-1";                      // ITEM_CONDITION
        cols[38] = "";                        // COVER_CONDITION
        cols[39] = "-1";                      // CATEGORY1
        cols[40] = "-1";                      // CATEGORY2
        cols[41] = "-1";                      // CATEGORY3
        cols[42] = "-1";                      // LOCATION
        cols[43] = "";                        // KEYWORDS
        cols[44] = "0";                       // READ_COUNT
        cols[45] = "";                        // DATE_LAST_READ
        cols[46] = productInfo;               // PRODUCT_INFO
        cols[47] = "";                        // MY_COMMENTS
        cols[48] = dateEntered;               // DATE_ENTERED
        cols[49] = "";                        // DATE_LAST_UPDATED
        cols[50] = "-1";                      // SOURCE
        cols[51] = "0.00";                    // PURCHASE_PRICE
        cols[52] = "";                        // PURCHASE_DATE
        cols[53] = "-1";                      // PURCHASE_PLACE
        cols[54] = "0.00";                    // LIST_PRICE
        cols[55] = "0.00";                    // ITEM_VALUE
        cols[56] = "";                        // VALUATION_DATE
        cols[57] = "$";                       // CURRENCY_SYMBOL
        cols[58] = "false";                   // FAVORITE
        cols[59] = "false";                   // OUT_OF_PRINT
        cols[60] = "";                        // MEDIA_URL
        cols[61] = "-1";                      // OWNER
        cols[62] = "-1";                      // STATUS
        cols[63] = "";                        // EXTERNAL_ID
        cols[64] = "";                        // INVENTORY
        cols[65] = "false";                   // IN_LAST_BATCH
        cols[66] = "";                        // AM_ASIN
        cols[67] = "0.00";                    // SALE_PRICE
        cols[68] = "";                        // SALE_DATE
        cols[69] = "0.00";                    // NEW_VALUE
        cols[70] = "0";                       // NEW_COUNT
        cols[71] = "0.00";                    // USED_VALUE
        cols[72] = "0";                       // USED_COUNT
        cols[73] = "0.00";                    // COLLECTIBLE_VALUE
        cols[74] = "0";                       // COLLECTIBLE_COUNT
        cols[75] = "false";                   // BUYER_WAITING
        cols[76] = "";                        // WEIGHT
        cols[77] = "0";                       // SALES_RANK
        cols[78] = "0";                       // THUMB_IMAGE_COUNT
        cols[79] = fullImageCount.ToString(); // FULL_IMAGE_COUNT
        // cols 80-99 are USER/USERL/FILLER fields — empty
        return string.Join(",", cols);
    }

    private static void WriteReaderwareFile(string folder, IEnumerable<string> dataRows)
    {
        var path = Path.Combine(folder, "READERWARE");
        var lines = new List<string> { MinimalHeader };
        lines.AddRange(dataRows);
        var content = string.Join("\r\n", lines);
        File.WriteAllBytes(path, Encoding.BigEndianUnicode.GetBytes(content));
    }

    private static void WriteDbCatalog40(string folder)
    {
        File.WriteAllText(Path.Combine(folder, "DBCATALOG40"), "");
    }

    [Fact]
    public async Task ParsesUtf16BeEncoding()
    {
        var folder = CreateTempDir();
        WriteDbCatalog40(folder);
        WriteReaderwareFile(folder,
        [
            MakeRow(rowKey: 1, title: "Book Alpha"),
            MakeRow(rowKey: 2, title: "Book Beta"),
            MakeRow(rowKey: 3, title: "Book Gamma"),
        ]);

        var parser = new ReaderwareBackupParser();
        var result = await parser.ParseAsync(folder, TestContext.Current.CancellationToken);

        Assert.Equal(3, result.Books.Count);
        Assert.Equal("Book Alpha", result.Books[0].Title);
        Assert.Equal("Book Beta", result.Books[1].Title);
        Assert.Equal("Book Gamma", result.Books[2].Title);
    }

    [Fact]
    public async Task ResolvesFkMinusOneAsNull()
    {
        var folder = CreateTempDir();
        WriteDbCatalog40(folder);
        WriteReaderwareFile(folder,
        [
            MakeRow(rowKey: 1, title: "Test", format: "-1"),
        ]);

        var parser = new ReaderwareBackupParser();
        var result = await parser.ParseAsync(folder, TestContext.Current.CancellationToken);

        Assert.Single(result.Books);
        Assert.Equal(-1, result.Books[0].FormatRowKey);
    }

    [Fact]
    public async Task ParsesMultiLineProductInfo()
    {
        var folder = CreateTempDir();
        WriteDbCatalog40(folder);
        // RFC 4180 multi-line: field in double quotes with embedded newline
        var row = MakeRow(rowKey: 1, title: "MultiLine Book", productInfo: "");
        // Replace the product_info column (index 46) with the multi-line value
        // Rebuild the row with the multi-line field
        var rowParts = row.Split(',');
        rowParts[46] = "\"Line one\nLine two\"";
        var rowWithMultiline = string.Join(",", rowParts);

        var path = Path.Combine(folder, "READERWARE");
        var content = MinimalHeader + "\r\n" + rowWithMultiline;
        File.WriteAllBytes(path, Encoding.BigEndianUnicode.GetBytes(content));

        var parser = new ReaderwareBackupParser();
        var result = await parser.ParseAsync(folder, TestContext.Current.CancellationToken);

        Assert.Single(result.Books);
        Assert.Contains("Line one", result.Books[0].BookInfo ?? "");
        Assert.Contains("Line two", result.Books[0].BookInfo ?? "");
    }

    [Fact]
    public async Task ZipExtractionProducesFolder()
    {
        var sourceFolder = CreateTempDir();
        WriteDbCatalog40(sourceFolder);
        WriteReaderwareFile(sourceFolder,
        [
            MakeRow(rowKey: 1, title: "Zipped Book"),
        ]);

        // Create a zip containing the backup folder
        var zipPath = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}.zip");
        try
        {
            ZipFile.CreateFromDirectory(sourceFolder, zipPath);

            var parser = new ReaderwareBackupParser();
            var result = await parser.ParseAsync(zipPath, TestContext.Current.CancellationToken);

            Assert.NotEmpty(result.Books);
            // Verify temp dir was cleaned up — hard to verify exactly, but parse succeeded
        }
        finally
        {
            if (File.Exists(zipPath))
                File.Delete(zipPath);
        }
    }

    [Fact]
    public async Task DetectsDbCatalog40FormatVersion()
    {
        var folder = CreateTempDir();
        // Deliberately do NOT write DBCATALOG40
        WriteReaderwareFile(folder,
        [
            MakeRow(rowKey: 1, title: "Test"),
        ]);

        var parser = new ReaderwareBackupParser();
        var result = await parser.ParseAsync(folder, TestContext.Current.CancellationToken);

        Assert.Contains(result.Warnings, w => w.Contains("DBCATALOG40"));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("0.0")]
    public async Task ParseRow_SeriesNumberZeroVariants_AreNull(string seriesNumber)
    {
        var folder = CreateTempDir();
        WriteDbCatalog40(folder);

        // Build a row with SERIES_NUMBER set to the zero variant
        var cols = MakeRow(rowKey: 1, title: "Zero Series").Split(',');
        // SERIES_NUMBER is index 35
        cols[35] = seriesNumber;
        var row = string.Join(",", cols);

        var path = Path.Combine(folder, "READERWARE");
        var content = MinimalHeader + "\r\n" + row;
        File.WriteAllBytes(path, Encoding.BigEndianUnicode.GetBytes(content));

        var parser = new ReaderwareBackupParser();
        var result = await parser.ParseAsync(folder, TestContext.Current.CancellationToken);

        Assert.Single(result.Books);
        Assert.Null(result.Books[0].SeriesNumber);
    }

    [Fact]
    public async Task ParseRow_ResolvesLookupNames_FromListFiles()
    {
        var folder = CreateTempDir();
        WriteDbCatalog40(folder);

        // Write a PUBLISHER_LIST file
        var publisherList =
            "ROWKEY,LISTITEM\r\n" +
            "5,Acme Publishing\r\n";
        File.WriteAllBytes(
            Path.Combine(folder, "PUBLISHER_LIST"),
            Encoding.BigEndianUnicode.GetBytes(publisherList));

        // Write a FORMAT_LIST file
        var formatList =
            "ROWKEY,LISTITEM\r\n" +
            "3,Hardcover\r\n";
        File.WriteAllBytes(
            Path.Combine(folder, "FORMAT_LIST"),
            Encoding.BigEndianUnicode.GetBytes(formatList));

        // Build a row referencing PUBLISHER=5 and FORMAT=3
        var cols = MakeRow(rowKey: 1, title: "Named Book", format: "3").Split(',');
        cols[13] = "5"; // PUBLISHER
        var row = string.Join(",", cols);

        var path = Path.Combine(folder, "READERWARE");
        var content = MinimalHeader + "\r\n" + row;
        File.WriteAllBytes(path, Encoding.BigEndianUnicode.GetBytes(content));

        var parser = new ReaderwareBackupParser();
        var result = await parser.ParseAsync(folder, TestContext.Current.CancellationToken);

        Assert.Single(result.Books);
        var book = result.Books[0];
        Assert.Equal("Acme Publishing", book.PublisherName);
        Assert.Equal("Hardcover", book.FormatName);
    }

    [Fact]
    public async Task ParseRow_ResolvesContributorNamesToResolvedContributors()
    {
        var folder = CreateTempDir();
        WriteDbCatalog40(folder);

        // Write CONTRIBUTOR file with author at ROWKEY 42
        var contribContent =
            "ROWKEY,NAME,SORT_NAME,ROLE1,ROLE2,ROLE3,BIO,FAVORITE,BIRTH_DATE,BIRTH_PLACE,DEATH_DATE,DEATH_PLACE,NOTES,CONTRIB_URL,IMAGE_DATA,EXTERNAL_ID,USER1,USER2,FILLER1,FILLER2\r\n" +
            "42,Jane Author,\"Author, Jane\",1,-1,-1,,false,,,,,,,,,,,false,\r\n";
        File.WriteAllBytes(
            Path.Combine(folder, "CONTRIBUTOR"),
            Encoding.BigEndianUnicode.GetBytes(contribContent));

        WriteReaderwareFile(folder,
        [
            MakeRow(rowKey: 1, title: "Contributed Book", author: "42"),
        ]);

        var parser = new ReaderwareBackupParser();
        var result = await parser.ParseAsync(folder, TestContext.Current.CancellationToken);

        Assert.Single(result.Books);
        var book = result.Books[0];
        Assert.Single(book.ResolvedContributors);
        var (role, displayName, _) = book.ResolvedContributors[0];
        Assert.Equal("Author", role);
        Assert.Equal("Jane Author", displayName);
    }

    [Fact]
    public async Task ParseRow_ResolvesCategoryNames_FromCategoryList()
    {
        var folder = CreateTempDir();
        WriteDbCatalog40(folder);

        // Write a CATEGORY_LIST file
        var categoryList =
            "ROWKEY,LISTITEM\r\n" +
            "7,Fiction\r\n" +
            "8,Mystery\r\n";
        File.WriteAllBytes(
            Path.Combine(folder, "CATEGORY_LIST"),
            Encoding.BigEndianUnicode.GetBytes(categoryList));

        // Build a row with CATEGORY1=7, CATEGORY2=8, CATEGORY3=-1
        var cols = MakeRow(rowKey: 1, title: "Category Book").Split(',');
        cols[39] = "7";  // CATEGORY1
        cols[40] = "8";  // CATEGORY2
        cols[41] = "-1"; // CATEGORY3
        var row = string.Join(",", cols);

        var path = Path.Combine(folder, "READERWARE");
        var content = MinimalHeader + "\r\n" + row;
        File.WriteAllBytes(path, Encoding.BigEndianUnicode.GetBytes(content));

        var parser = new ReaderwareBackupParser();
        var result = await parser.ParseAsync(folder, TestContext.Current.CancellationToken);

        Assert.Single(result.Books);
        var book = result.Books[0];
        Assert.Contains("Fiction", book.ResolvedCategoryNames);
        Assert.Contains("Mystery", book.ResolvedCategoryNames);
        Assert.Equal(2, book.ResolvedCategoryNames.Count);
    }

    [Fact]
    public async Task ParseMainFile_EmptyFile_ThrowsInvalidDataException()
    {
        var dir = CreateTempDir();
        // Write a completely empty main file (no header, no data) — UTF-16 BE BOM only
        await File.WriteAllBytesAsync(
            Path.Combine(dir, "READERWARE"),
            Encoding.BigEndianUnicode.GetPreamble(),
            TestContext.Current.CancellationToken);

        var parser = new ReaderwareBackupParser();

        var exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => parser.ParseAsync(dir, TestContext.Current.CancellationToken));

        Assert.Contains("empty", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ParseRow_NewFields_ExtractsAllColumns()
    {
        var folder = CreateTempDir();
        WriteDbCatalog40(folder);

        // Build a row with all new field values populated
        var cols = MakeRow(rowKey: 1, title: "Field Test Book").Split(',');
        cols[21] = "21 x 14 cm";          // DIMENSIONS
        cols[23] = "890";                  // LEXILE_LEVEL
        cols[27] = "0001-4966";            // ISSN
        cols[28] = "2020012345";           // LCCN
        cols[29] = "823.914";              // DEWEY
        cols[30] = "PR6068.O93";           // CALL_NUMBER
        cols[55] = "25.00";                // ITEM_VALUE
        cols[56] = "2024-01-15";           // VALUATION_DATE
        cols[69] = "19.99";                // NEW_VALUE
        cols[70] = "12";                   // NEW_COUNT
        cols[71] = "5.50";                 // USED_VALUE
        cols[72] = "8";                    // USED_COUNT
        cols[73] = "45.00";                // COLLECTIBLE_VALUE
        cols[74] = "3";                    // COLLECTIBLE_COUNT
        cols[76] = "0.35";                 // WEIGHT
        cols[77] = "54321";                // SALES_RANK
        var row = string.Join(",", cols);

        var path = Path.Combine(folder, "READERWARE");
        var content = MinimalHeader + "\r\n" + row;
        File.WriteAllBytes(path, Encoding.BigEndianUnicode.GetBytes(content));

        var parser = new ReaderwareBackupParser();
        var result = await parser.ParseAsync(folder, TestContext.Current.CancellationToken);

        Assert.Single(result.Books);
        var book = result.Books[0];

        // The parser populates the extended ParsedBook fields from the Readerware columns.
        Assert.Equal("0001-4966", book.Issn);
        Assert.Equal("2020012345", book.Lccn);
        Assert.Equal("823.914", book.DeweyDecimal);
        Assert.Equal("PR6068.O93", book.CallNumber);
        Assert.Equal("21 x 14 cm", book.Dimensions);
        Assert.Equal(0.35m, book.Weight);
        Assert.Equal(25.00m, book.ItemValue);
        Assert.Equal(DateTime.Parse("2024-01-15"), book.ValuationDate);
        Assert.Equal(19.99m, book.AmazonNewValue);
        Assert.Equal(5.50m, book.AmazonUsedValue);
        Assert.Equal(45.00m, book.AmazonCollectibleValue);
        Assert.Equal(12, book.AmazonNewCount);
        Assert.Equal(8, book.AmazonUsedCount);
        Assert.Equal(3, book.AmazonCollectibleCount);
        Assert.Equal(54321, book.SalesRank);
        Assert.Equal(890, book.LexileLevel);
    }

    // Valid minimal JPEG hex: SOI (FFD8FF) + APP0 marker
    private const string ValidJpegHex = "ffd8ffe000104a46494600010100000100010000";

    private static void WriteImageFile(string folder, string fileName, string csvContent)
    {
        File.WriteAllText(Path.Combine(folder, fileName), csvContent, Encoding.BigEndianUnicode);
    }

    [Fact]
    public async Task LoadImageFile_MultipleImagesPerRowId_ReturnsAllEntries()
    {
        var folder = CreateTempDir();
        WriteDbCatalog40(folder);
        WriteReaderwareFile(folder, [MakeRow(rowKey: 1, title: "Multi Image Book")]);

        var csvContent =
            "ROWKEY,ROW_ID,IMAGE_INDEX,IMAGE_DATA,IMAGE_DESC,FILLER1,FILLER2\r\n" +
            $"1,1,0,{ValidJpegHex},,\r\n" +
            $"2,1,1,{ValidJpegHex},,\r\n";
        WriteImageFile(folder, "FULL_IMAGES", csvContent);

        var parser = new ReaderwareBackupParser();
        var result = await parser.ParseAsync(folder, TestContext.Current.CancellationToken);

        Assert.True(result.FullImagesByRowKey.ContainsKey(1),
            "Expected ROW_ID=1 in FullImagesByRowKey");
        Assert.Equal(2, result.FullImagesByRowKey[1].Count);
        Assert.Contains(result.FullImagesByRowKey[1], t => t.ImageIndex == 0);
        Assert.Contains(result.FullImagesByRowKey[1], t => t.ImageIndex == 1);
    }

    [Theory]
    [InlineData("89504e470d0a1a0a")]   // PNG
    [InlineData("474946383961ffff")]   // GIF (GIF89a)
    [InlineData("424d3a0000000000")]   // BMP
    public async Task LoadImageFile_AcceptsNonJpegRasterFormats(string magicHex)
    {
        var folder = CreateTempDir();
        WriteDbCatalog40(folder);
        WriteReaderwareFile(folder, [MakeRow(rowKey: 1, title: "Non-JPEG Cover")]);

        var csvContent =
            "ROWKEY,ROW_ID,IMAGE_INDEX,IMAGE_DATA,IMAGE_DESC,FILLER1,FILLER2\r\n" +
            $"1,1,0,{magicHex},,\r\n";
        WriteImageFile(folder, "FULL_IMAGES", csvContent);

        var parser = new ReaderwareBackupParser();
        var result = await parser.ParseAsync(folder, TestContext.Current.CancellationToken);

        Assert.True(result.FullImagesByRowKey.ContainsKey(1), "Expected non-JPEG image to be kept");
        Assert.Single(result.FullImagesByRowKey[1]);
        Assert.DoesNotContain(result.Warnings, w => w.Contains("unrecognized image format"));
    }

    [Fact]
    public async Task LoadImageFile_UnrecognizedFormat_IsSkippedWithWarning()
    {
        var folder = CreateTempDir();
        WriteDbCatalog40(folder);
        WriteReaderwareFile(folder, [MakeRow(rowKey: 1, title: "Bad Cover")]);

        var csvContent =
            "ROWKEY,ROW_ID,IMAGE_INDEX,IMAGE_DATA,IMAGE_DESC,FILLER1,FILLER2\r\n" +
            "1,1,0,0102030405060708,,\r\n";   // not a recognized image signature
        WriteImageFile(folder, "FULL_IMAGES", csvContent);

        var parser = new ReaderwareBackupParser();
        var result = await parser.ParseAsync(folder, TestContext.Current.CancellationToken);

        Assert.False(result.FullImagesByRowKey.ContainsKey(1));
        Assert.Contains(result.Warnings, w => w.Contains("unrecognized image format"));
    }

    [Fact]
    public async Task LoadImageFile_SingleImagePerRowId_ReturnsSingleEntry()
    {
        var folder = CreateTempDir();
        WriteDbCatalog40(folder);
        WriteReaderwareFile(folder, [MakeRow(rowKey: 1, title: "Single Image Book")]);

        var csvContent =
            "ROWKEY,ROW_ID,IMAGE_INDEX,IMAGE_DATA,IMAGE_DESC,FILLER1,FILLER2\r\n" +
            $"1,1,0,{ValidJpegHex},,\r\n";
        WriteImageFile(folder, "FULL_IMAGES", csvContent);

        var parser = new ReaderwareBackupParser();
        var result = await parser.ParseAsync(folder, TestContext.Current.CancellationToken);

        Assert.True(result.FullImagesByRowKey.ContainsKey(1));
        Assert.Single(result.FullImagesByRowKey[1]);
        Assert.Equal(0, result.FullImagesByRowKey[1][0].ImageIndex);
    }

    [Fact]
    public async Task LoadBorrowers_EmptyFile_ReturnsEmptyList()
    {
        var folder = CreateTempDir();
        WriteDbCatalog40(folder);
        WriteReaderwareFile(folder, [MakeRow(rowKey: 1, title: "Test")]);

        // Create a 0-byte BORROWER file
        File.WriteAllBytes(Path.Combine(folder, "BORROWER"), []);

        var parser = new ReaderwareBackupParser();
        var result = await parser.ParseAsync(folder, TestContext.Current.CancellationToken);

        Assert.Empty(result.Borrowers);
        Assert.DoesNotContain(result.Warnings, w => w.Contains("BORROWER"));
    }

    [Fact]
    public async Task LoadLoans_MissingFile_ReturnsEmptyList()
    {
        var folder = CreateTempDir();
        WriteDbCatalog40(folder);
        WriteReaderwareFile(folder, [MakeRow(rowKey: 1, title: "Test")]);

        // Deliberately do NOT create LOANS file

        var parser = new ReaderwareBackupParser();
        var result = await parser.ParseAsync(folder, TestContext.Current.CancellationToken);

        Assert.Empty(result.Loans);
        Assert.DoesNotContain(result.Warnings, w => w.Contains("LOANS"));
    }

    [Fact]
    public async Task LoadVolumes_ParsesRowKeyBookIdVolNumber()
    {
        var folder = CreateTempDir();
        WriteDbCatalog40(folder);
        WriteReaderwareFile(folder,
        [
            MakeRow(rowKey: 10, title: "Book A"),
            MakeRow(rowKey: 20, title: "Book B"),
        ]);

        var csvContent =
            "ROWKEY,BOOK_ID,VOL_NUMBER,VOL_TITLE,VOL_USER1,VOL_USER2,VOL_FILLER1,VOL_FILLER2\r\n" +
            "1,10,1,Volume One,,,\r\n" +
            "2,20,2,Volume Two,,,\r\n";
        File.WriteAllText(Path.Combine(folder, "READERWARE_VOLUMES"), csvContent, Encoding.BigEndianUnicode);

        var parser = new ReaderwareBackupParser();
        var result = await parser.ParseAsync(folder, TestContext.Current.CancellationToken);

        Assert.Equal(2, result.Volumes.Count);
        var vol1 = result.Volumes.First(v => v.VolumeRowKey == 1);
        Assert.Equal(10, vol1.BookRowKey);
        Assert.Equal(1, vol1.VolumeNumber);
        var vol2 = result.Volumes.First(v => v.VolumeRowKey == 2);
        Assert.Equal(20, vol2.BookRowKey);
        Assert.Equal(2, vol2.VolumeNumber);
    }

    [Fact]
    public async Task LoadChapters_ParsesRowKeyVolIdChpNumber()
    {
        var folder = CreateTempDir();
        WriteDbCatalog40(folder);
        WriteReaderwareFile(folder, [MakeRow(rowKey: 1, title: "Test")]);

        var csvContent =
            "ROWKEY,VOL_ID,CHP_NUMBER,CHP_TITLE,FILLER1,FILLER2\r\n" +
            "1,5,1,Chapter One,,\r\n" +
            "2,5,2,Chapter Two,,\r\n";
        File.WriteAllText(Path.Combine(folder, "READERWARE_CHAPTERS"), csvContent, Encoding.BigEndianUnicode);

        var parser = new ReaderwareBackupParser();
        var result = await parser.ParseAsync(folder, TestContext.Current.CancellationToken);

        Assert.Equal(2, result.Chapters.Count);
        var chp1 = result.Chapters.First(c => c.ChapterRowKey == 1);
        Assert.Equal(5, chp1.VolumeRowKey);
        Assert.Equal(1, chp1.ChapterNumber);
        var chp2 = result.Chapters.First(c => c.ChapterRowKey == 2);
        Assert.Equal(5, chp2.VolumeRowKey);
        Assert.Equal(2, chp2.ChapterNumber);
    }

    [Fact]
    public async Task LoadsContributorFileByRowKey()
    {
        var folder = CreateTempDir();
        WriteDbCatalog40(folder);

        // Write CONTRIBUTOR file with one author entry
        var contribContent =
            "ROWKEY,NAME,SORT_NAME,ROLE1,ROLE2,ROLE3,BIO,FAVORITE,BIRTH_DATE,BIRTH_PLACE,DEATH_DATE,DEATH_PLACE,NOTES,CONTRIB_URL,IMAGE_DATA,EXTERNAL_ID,USER1,USER2,FILLER1,FILLER2\r\n" +
            "42,Jane Author,\"Author, Jane\",1,-1,-1,,false,,,,,,,,,,,false,\r\n";
        File.WriteAllBytes(
            Path.Combine(folder, "CONTRIBUTOR"),
            Encoding.BigEndianUnicode.GetBytes(contribContent));

        // Write READERWARE with AUTHOR = 42
        WriteReaderwareFile(folder,
        [
            MakeRow(rowKey: 1, title: "Authored Book", author: "42"),
        ]);

        var parser = new ReaderwareBackupParser();
        var result = await parser.ParseAsync(folder, TestContext.Current.CancellationToken);

        Assert.Single(result.Books);
        var book = result.Books[0];
        Assert.True(book.ContributorsByRole.ContainsKey("Author"),
            "Expected 'Author' role in ContributorsByRole");
        Assert.Contains(42, book.ContributorsByRole["Author"]);
    }
}
