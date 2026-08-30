using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BookDB.Companion.Host;
using BookDB.Contracts;
using BookDB.Logic.Services;
using SkiaSharp;
using Xunit;

namespace BookDB.Companion.Host.Tests;

/// <summary>
/// The read calls over the wire: what the phone asks for reaches the library unchanged, and what comes
/// back is shrunk to something a phone can hold.
/// </summary>
public sealed class ReadSurfaceTests
{
    /// <summary>A real JPEG, since the point of these tests is that it is decoded and re-encoded.</summary>
    private static byte[] Jpeg(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.CornflowerBlue);
        using var encoded = bitmap.Encode(SKEncodedImageFormat.Jpeg, 90);
        return encoded.ToArray();
    }

    private static (int Width, int Height) SizeOf(byte[] jpeg)
    {
        using var decoded = SKBitmap.Decode(jpeg);
        return (decoded.Width, decoded.Height);
    }

    private static async Task<IBookScannerService> ConnectedAsync(CompanionHostHarness harness)
    {
        await harness.StartAsync();
        return harness.ConnectWith(harness.CreatePayload()) is var client
            && (await client.ClaimDeviceAsync(new ClaimDeviceRequest { DeviceName = "Phone" })).Outcome
                == ClaimOutcome.Registered
            ? client
            : throw new InvalidOperationException("The test device could not pair.");
    }

    [Fact]
    public async Task AScannedIsbnComesBackAsALibraryHit()
    {
        await using var harness = new CompanionHostHarness();
        harness.Preview.Answer = new IsbnPreviewResult(true, 42, "Kallocain", "Karin Boye", IsbnPreviewOrigin.Library);
        var client = await ConnectedAsync(harness);

        var preview = await client.CheckIsbnAsync(new IsbnQuery { Isbn = "9780306406157" });

        Assert.True(preview.InLibrary);
        Assert.Equal(42, preview.BookId);
        Assert.Equal("Kallocain", preview.Title);
        Assert.Equal("Karin Boye", preview.Authors);
        Assert.Equal(IsbnPreviewSource.Library, preview.Source);
        Assert.Equal("9780306406157", Assert.Single(harness.Preview.Queried));
    }

    [Fact]
    public async Task AnIsbnOnlyTheSourcesKnowIsReportedAsALookup()
    {
        await using var harness = new CompanionHostHarness();
        harness.Preview.Answer = new IsbnPreviewResult(false, null, "Aniara", null, IsbnPreviewOrigin.Lookup);
        var client = await ConnectedAsync(harness);

        var preview = await client.CheckIsbnAsync(new IsbnQuery { Isbn = "9789100120115" });

        Assert.False(preview.InLibrary);
        Assert.Null(preview.BookId);
        Assert.Equal("Aniara", preview.Title);
        Assert.Equal(IsbnPreviewSource.Lookup, preview.Source);
    }

    [Fact]
    public async Task AnIsbnNobodyKnowsComesBackEmptyRatherThanAsAnError()
    {
        await using var harness = new CompanionHostHarness();
        var client = await ConnectedAsync(harness);

        var preview = await client.CheckIsbnAsync(new IsbnQuery { Isbn = "9780306406157" });

        Assert.False(preview.InLibrary);
        Assert.Null(preview.Title);
        Assert.Equal(IsbnPreviewSource.None, preview.Source);
    }

    [Fact]
    public async Task ABrowseQueryReachesTheLibraryAsTheUserPhrasedIt()
    {
        await using var harness = new CompanionHostHarness();
        var client = await ConnectedAsync(harness);

        await client.ListBooksAsync(new BookQuery
        {
            Search = "boye",
            CollectionId = 3,
            Isbn = "9780306406157",
            Skip = 20,
            Take = 10,
        });

        Assert.Equal(("boye", 3, "9780306406157", 20, 10), Assert.Single(harness.Browse.Queries));
    }

    [Fact]
    public async Task ABrowsePageCarriesTheRowsAndTheTotal()
    {
        await using var harness = new CompanionHostHarness();
        harness.Browse.Page = new BrowsePage(
            [
                new BrowseBook(1, "Kallocain", "Karin Boye", "9789100120115", 1940, false),
                new BrowseBook(2, "Aniara", null, null, null, false),
            ],
            57);
        var client = await ConnectedAsync(harness);

        var page = await client.ListBooksAsync(new BookQuery());

        Assert.Equal(57, page.TotalCount);
        Assert.Equal(2, page.Books.Count);
        Assert.Equal("Kallocain", page.Books[0].Title);
        Assert.Equal("Karin Boye", page.Books[0].Authors);
        Assert.Equal("9789100120115", page.Books[0].Isbn);
        Assert.Equal(1940, page.Books[0].Year);
        Assert.Null(page.Books[0].Thumbnail);
        Assert.Null(page.Books[1].Authors);
        Assert.Null(page.Books[1].Year);
    }

    [Fact]
    public async Task ABrowseThumbnailIsShrunkBeforeItCrossesTheWire()
    {
        await using var harness = new CompanionHostHarness();
        byte[] full = Jpeg(1200, 1800);
        harness.Browse.Page = new BrowsePage([new BrowseBook(1, "Kallocain", null, null, null, true)], 1);
        harness.Browse.Images[(1, 0)] = full;
        var client = await ConnectedAsync(harness);

        var thumbnail = (await client.ListBooksAsync(new BookQuery())).Books[0].Thumbnail;

        Assert.NotNull(thumbnail);
        Assert.Equal(ImageDownscaler.ThumbnailLongEdgePx, SizeOf(thumbnail!).Height);
        Assert.True(thumbnail!.Length < full.Length / 4);
    }

    [Fact]
    public async Task ABookWithoutACoverIsNotReadFromTheLibraryAtAll()
    {
        await using var harness = new CompanionHostHarness();
        harness.Browse.Page = new BrowsePage([new BrowseBook(1, "Kallocain", null, null, null, false)], 1);
        var client = await ConnectedAsync(harness);

        Assert.Null((await client.ListBooksAsync(new BookQuery())).Books[0].Thumbnail);
        Assert.Empty(harness.Browse.ImageReads);
    }

    [Fact]
    public async Task PagingBackOverTheSameBooksDoesNotReReadTheirCovers()
    {
        await using var harness = new CompanionHostHarness();
        harness.Browse.Page = new BrowsePage([new BrowseBook(1, "Kallocain", null, null, null, true)], 1);
        harness.Browse.Images[(1, 0)] = Jpeg(800, 1200);
        var client = await ConnectedAsync(harness);

        await client.ListBooksAsync(new BookQuery());
        await client.ListBooksAsync(new BookQuery());

        Assert.Single(harness.Browse.ImageReads);
    }

    [Fact]
    public async Task AnImageIsFetchedByItsTypeAndDownscaledOnRequest()
    {
        await using var harness = new CompanionHostHarness();
        harness.Browse.Images[(7, 3)] = Jpeg(1000, 1000);
        var client = await ConnectedAsync(harness);

        var image = await client.GetBookImageAsync(new BookImageRef
        {
            BookId = 7,
            Type = ScanImageType.Spine,
            MaxLongEdgePx = 300,
        });

        Assert.NotNull(image.Jpeg);
        Assert.Equal((300, 300), SizeOf(image.Jpeg!));
        Assert.Equal((7, 3), Assert.Single(harness.Browse.ImageReads));
    }

    [Fact]
    public async Task AnImageAskedForWholeArrivesUntouched()
    {
        await using var harness = new CompanionHostHarness();
        byte[] original = Jpeg(400, 500);
        harness.Browse.Images[(7, 0)] = original;
        var client = await ConnectedAsync(harness);

        var image = await client.GetBookImageAsync(new BookImageRef { BookId = 7, Type = ScanImageType.FrontCover });

        Assert.Equal(original, image.Jpeg);
    }

    [Fact]
    public async Task AMissingImageIsAbsenceRatherThanAnError()
    {
        await using var harness = new CompanionHostHarness();
        var client = await ConnectedAsync(harness);

        var image = await client.GetBookImageAsync(new BookImageRef { BookId = 7, Type = ScanImageType.BackCover });

        Assert.Null(image.Jpeg);
    }

    [Fact]
    public async Task TheCollectionsTheFilterOffersCrossTheWireWithTheirCounts()
    {
        await using var harness = new CompanionHostHarness();
        harness.Browse.Collections.Add(new BrowseCollection(3, "Fiction", 412));
        harness.Browse.Collections.Add(new BrowseCollection(4, "Reference", 27));
        var client = await ConnectedAsync(harness);

        var list = await client.ListCollectionsAsync();

        Assert.Equal(2, list.Collections.Count);
        Assert.Equal(3, list.Collections[0].CollectionId);
        Assert.Equal("Fiction", list.Collections[0].Name);
        Assert.Equal(412, list.Collections[0].BookCount);
        Assert.Equal("Reference", list.Collections[1].Name);
    }

    [Fact]
    public async Task ALibraryWithNoCollectionsAnswersAnEmptyListRatherThanAnError()
    {
        await using var harness = new CompanionHostHarness();
        var client = await ConnectedAsync(harness);

        Assert.Empty((await client.ListCollectionsAsync()).Collections);
    }

    [Fact]
    public async Task ABooksDetailArrivesWithEveryLookupAlreadyResolved()
    {
        await using var harness = new CompanionHostHarness();
        harness.Browse.Details[7] = new BrowseDetail(
            7, "Kallocain", "En roman om framtiden", "Karin Boye", "Framtidsserien #2", "Bonniers",
            "1940", "Hardcover", "Swedish", 191, "9780306406157", "Fiction", "Signed", [0, 3]);
        var client = await ConnectedAsync(harness);

        var detail = await client.GetBookDetailAsync(new BookRef { BookId = 7 });

        Assert.True(detail.Found);
        Assert.Equal(7, detail.BookId);
        Assert.Equal("Kallocain", detail.Title);
        Assert.Equal("En roman om framtiden", detail.Subtitle);
        Assert.Equal("Karin Boye", detail.Authors);
        Assert.Equal("Framtidsserien #2", detail.Series);
        Assert.Equal("Bonniers", detail.Publisher);
        Assert.Equal("1940", detail.PubDate);
        Assert.Equal("Hardcover", detail.Format);
        Assert.Equal("Swedish", detail.Language);
        Assert.Equal(191, detail.Pages);
        Assert.Equal("9780306406157", detail.Isbn);
        Assert.Equal("Fiction", detail.Collection);
        Assert.Equal("Signed", detail.Comments);
        Assert.Equal(7, Assert.Single(harness.Browse.DetailReads));
    }

    [Fact]
    public async Task TheImagesADetailNamesCarryTheDesktopsOwnTypeNumbers()
    {
        await using var harness = new CompanionHostHarness();
        harness.Browse.Details[7] = new BrowseDetail(
            7, "Kallocain", null, null, null, null, null, null, null, null, null, null, null, [0, 2, 3, 4]);
        var client = await ConnectedAsync(harness);

        var detail = await client.GetBookDetailAsync(new BookRef { BookId = 7 });

        Assert.Equal(
            new[]
            {
                ScanImageType.FrontCover, ScanImageType.BackCover, ScanImageType.Spine, ScanImageType.DustJacket,
            },
            detail.Images);
    }

    [Fact]
    public async Task ABookDeletedSinceItWasListedComesBackAsNotFoundRatherThanAsAnError()
    {
        await using var harness = new CompanionHostHarness();
        var client = await ConnectedAsync(harness);

        var detail = await client.GetBookDetailAsync(new BookRef { BookId = 7 });

        Assert.False(detail.Found);
        Assert.Equal(7, detail.BookId);
        Assert.Empty(detail.Images);
    }

    [Fact]
    public async Task AFieldTheBookDoesNotHaveArrivesAsNothingRatherThanAsAnEmptyString()
    {
        await using var harness = new CompanionHostHarness();
        harness.Browse.Details[7] = new BrowseDetail(
            7, "Kallocain", null, null, null, null, null, null, null, null, null, null, null, []);
        var client = await ConnectedAsync(harness);

        var detail = await client.GetBookDetailAsync(new BookRef { BookId = 7 });

        Assert.Null(detail.Authors);
        Assert.Null(detail.Publisher);
        Assert.Null(detail.Pages);
    }
}
