using System.Collections.Generic;
using System.IO;
using BookDB.Contracts;
using ProtoBuf;
using Xunit;

namespace BookDB.Companion.Host.Tests;

/// <summary>
/// Every wire type survives a protobuf round-trip with its values intact. These are the compatibility
/// tests for the contract: a member renumbered or a type made unserializable fails here rather than on a
/// phone that can no longer talk to the desktop.
/// </summary>
public sealed class ContractSerializationTests
{
    private static T RoundTrip<T>(T value)
    {
        using var buffer = new MemoryStream();
        Serializer.Serialize(buffer, value);
        buffer.Position = 0;
        return Serializer.Deserialize<T>(buffer);
    }

    [Fact]
    public void ServerInfo_RoundTripsIncludingCaptureSettings()
    {
        var clone = RoundTrip(new ServerInfo
        {
            AppVersion = "4.0.0",
            ContractVersion = ContractVersion.Current,
            LibraryName = "Böckerna",
            InstanceId = "6f5c1a2e-0b1d-4f7a-9f43-2b0f0f8a1c33",
            Capture = new CaptureSettings { MaxLongEdgePx = 1600, JpegQuality = 80 },
        });

        Assert.Equal("4.0.0", clone.AppVersion);
        Assert.Equal(1, clone.ContractVersion);
        Assert.Equal("Böckerna", clone.LibraryName);
        Assert.Equal("6f5c1a2e-0b1d-4f7a-9f43-2b0f0f8a1c33", clone.InstanceId);
        Assert.Equal(1600, clone.Capture.MaxLongEdgePx);
        Assert.Equal(80, clone.Capture.JpegQuality);
    }

    [Fact]
    public void CaptureSettings_ZeroSurvivesAsZero()
    {
        var clone = RoundTrip(new CaptureSettings());

        Assert.Equal(0, clone.MaxLongEdgePx);
        Assert.Equal(0, clone.JpegQuality);
    }

    [Fact]
    public void IsbnQuery_RoundTrips()
    {
        Assert.Equal("9780306406157", RoundTrip(new IsbnQuery { Isbn = "9780306406157" }).Isbn);
    }

    [Fact]
    public void IsbnPreview_RoundTripsALibraryHit()
    {
        var clone = RoundTrip(new IsbnPreview
        {
            InLibrary = true,
            BookId = 42,
            Title = "Gödel, Escher, Bach",
            Authors = "Douglas Hofstadter",
            Source = IsbnPreviewSource.Library,
        });

        Assert.True(clone.InLibrary);
        Assert.Equal(42, clone.BookId);
        Assert.Equal("Gödel, Escher, Bach", clone.Title);
        Assert.Equal("Douglas Hofstadter", clone.Authors);
        Assert.Equal(IsbnPreviewSource.Library, clone.Source);
    }

    [Fact]
    public void IsbnPreview_RoundTripsAMissWithNoOptionalValues()
    {
        var clone = RoundTrip(new IsbnPreview { Source = IsbnPreviewSource.None });

        Assert.False(clone.InLibrary);
        Assert.Null(clone.BookId);
        Assert.Null(clone.Title);
        Assert.Null(clone.Authors);
    }

    [Fact]
    public void ClaimDeviceRequest_RoundTrips()
    {
        Assert.Equal("Ulf's phone", RoundTrip(new ClaimDeviceRequest { DeviceName = "Ulf's phone" }).DeviceName);
    }

    [Fact]
    public void ClaimDeviceResult_RoundTripsTheOutcomeAndBothCounts()
    {
        var clone = RoundTrip(new ClaimDeviceResult
        {
            Outcome = ClaimOutcome.DeviceLimitReached,
            RegisteredDeviceCount = 5,
            MaxDevices = 5,
        });

        Assert.Equal(ClaimOutcome.DeviceLimitReached, clone.Outcome);
        Assert.Equal(5, clone.RegisteredDeviceCount);
        Assert.Equal(5, clone.MaxDevices);
    }

    [Fact]
    public void ScanUpload_HeaderCarriesTheIsbnWithoutAnImage()
    {
        var clone = RoundTrip(new ScanUpload
        {
            ClientItemId = "item-1",
            Header = new ScanItemHeader { Isbn = "0306406152" },
        });

        Assert.Equal("item-1", clone.ClientItemId);
        Assert.NotNull(clone.Header);
        Assert.Equal("0306406152", clone.Header!.Isbn);
        Assert.Null(clone.Image);
    }

    [Fact]
    public void ScanUpload_ImageCarriesJpegBytesWithoutAHeader()
    {
        var clone = RoundTrip(new ScanUpload
        {
            ClientItemId = "item-1",
            Image = new ScanImage { Type = ScanImageType.DustJacket, Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00] },
        });

        Assert.Null(clone.Header);
        Assert.NotNull(clone.Image);
        Assert.Equal(ScanImageType.DustJacket, clone.Image!.Type);
        Assert.Equal(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00 }, clone.Image.Jpeg);
    }

    [Fact]
    public void SubmitBatchResult_RoundTrips()
    {
        var clone = RoundTrip(new SubmitBatchResult { BatchId = "batch-7", AcceptedItems = 12 });

        Assert.Equal("batch-7", clone.BatchId);
        Assert.Equal(12, clone.AcceptedItems);
    }

    [Fact]
    public void BatchRef_RoundTrips()
    {
        Assert.Equal("batch-7", RoundTrip(new BatchRef { BatchId = "batch-7" }).BatchId);
    }

    [Fact]
    public void BatchItemStatus_RoundTripsASavedItem()
    {
        var clone = RoundTrip(new BatchItemStatus
        {
            ClientItemId = "item-3",
            Isbn = "080442957X",
            State = BatchItemState.Saved,
            BookId = 99,
            Title = "Kallocain",
        });

        Assert.Equal("item-3", clone.ClientItemId);
        Assert.Equal("080442957X", clone.Isbn);
        Assert.Equal(BatchItemState.Saved, clone.State);
        Assert.Equal(99, clone.BookId);
        Assert.Equal("Kallocain", clone.Title);
        Assert.Equal(BatchItemFailure.None, clone.Failure);
        Assert.Null(clone.FailureDetail);
    }

    [Fact]
    public void BatchItemStatus_RoundTripsAFailureCodeAndDetail()
    {
        var clone = RoundTrip(new BatchItemStatus
        {
            ClientItemId = "item-4",
            State = BatchItemState.Failed,
            Failure = BatchItemFailure.ImageRejected,
            FailureDetail = "frame exceeds 8 MB",
        });

        Assert.Equal(BatchItemState.Failed, clone.State);
        Assert.Equal(BatchItemFailure.ImageRejected, clone.Failure);
        Assert.Equal("frame exceeds 8 MB", clone.FailureDetail);
        Assert.Null(clone.BookId);
    }

    [Fact]
    public void BookQuery_RoundTripsSearchPagingAndFilters()
    {
        var clone = RoundTrip(new BookQuery
        {
            Search = "hofstadter",
            CollectionId = 5,
            Skip = 100,
            Take = 25,
            Isbn = "9780306406157",
        });

        Assert.Equal("hofstadter", clone.Search);
        Assert.Equal(5, clone.CollectionId);
        Assert.Equal(100, clone.Skip);
        Assert.Equal(25, clone.Take);
        Assert.Equal("9780306406157", clone.Isbn);
    }

    [Fact]
    public void BookQuery_UnsetTakeStaysZeroSoTheServerPicksThePageSize()
    {
        Assert.Equal(0, RoundTrip(new BookQuery()).Take);
    }

    [Fact]
    public void BookPage_RoundTripsItsSummaries()
    {
        var clone = RoundTrip(new BookPage
        {
            TotalCount = 2,
            Books =
            [
                new BookSummary { BookId = 1, Title = "Kallocain", Authors = "Karin Boye", Isbn = "080442957X", Year = 1940, Thumbnail = [1, 2, 3] },
                new BookSummary { BookId = 2, Title = "Aniara" },
            ],
        });

        Assert.Equal(2, clone.TotalCount);
        Assert.Equal(2, clone.Books.Count);
        Assert.Equal("Kallocain", clone.Books[0].Title);
        Assert.Equal("Karin Boye", clone.Books[0].Authors);
        Assert.Equal(1940, clone.Books[0].Year);
        Assert.Equal(new byte[] { 1, 2, 3 }, clone.Books[0].Thumbnail);
        Assert.Equal("Aniara", clone.Books[1].Title);
        Assert.Null(clone.Books[1].Year);
        Assert.Null(clone.Books[1].Thumbnail);
    }

    [Fact]
    public void BookPage_EmptyPageDeserializesToAnEmptyList()
    {
        var clone = RoundTrip(new BookPage());

        Assert.NotNull(clone.Books);
        Assert.Empty(clone.Books);
        Assert.Equal(0, clone.TotalCount);
    }

    [Fact]
    public void BookImageRef_RoundTrips()
    {
        var clone = RoundTrip(new BookImageRef { BookId = 7, Type = ScanImageType.Spine, MaxLongEdgePx = 900 });

        Assert.Equal(7, clone.BookId);
        Assert.Equal(ScanImageType.Spine, clone.Type);
        Assert.Equal(900, clone.MaxLongEdgePx);
    }

    [Fact]
    public void ImageBytes_RoundTripsBothBytesAndAbsence()
    {
        Assert.Equal(new byte[] { 9, 8, 7 }, RoundTrip(new ImageBytes { Jpeg = [9, 8, 7] }).Jpeg);
        Assert.Null(RoundTrip(new ImageBytes()).Jpeg);
    }

    [Fact]
    public void ScanItemHeader_RoundTrips()
    {
        Assert.Equal("0306406152", RoundTrip(new ScanItemHeader { Isbn = "0306406152" }).Isbn);
    }

    [Fact]
    public void ScanImage_EmptyPayloadRoundTripsAsEmpty()
    {
        var clone = RoundTrip(new ScanImage { Type = ScanImageType.FrontCover });

        Assert.Equal(ScanImageType.FrontCover, clone.Type);
        Assert.Equal<IEnumerable<byte>>([], clone.Jpeg);
    }

    [Fact]
    public void CollectionList_RoundTripsEachCollectionWithItsCount()
    {
        var clone = RoundTrip(new CollectionList
        {
            Collections =
            [
                new CollectionRef { CollectionId = 3, Name = "Skönlitteratur", BookCount = 412 },
                new CollectionRef { CollectionId = 4, Name = "Reference", BookCount = 0 },
            ],
        });

        Assert.Equal(2, clone.Collections.Count);
        Assert.Equal(3, clone.Collections[0].CollectionId);
        Assert.Equal("Skönlitteratur", clone.Collections[0].Name);
        Assert.Equal(412, clone.Collections[0].BookCount);
        Assert.Equal(0, clone.Collections[1].BookCount);
    }

    [Fact]
    public void BookRef_RoundTrips()
    {
        Assert.Equal(7, RoundTrip(new BookRef { BookId = 7 }).BookId);
    }

    [Fact]
    public void BookDetail_RoundTripsEveryFieldAndItsImageList()
    {
        var clone = RoundTrip(new BookDetail
        {
            Found = true,
            BookId = 7,
            Title = "Kallocain",
            Subtitle = "En roman om framtiden",
            Authors = "Karin Boye",
            Series = "Framtidsserien #2",
            Publisher = "Bonniers",
            PubDate = "1940",
            Format = "Inbunden",
            Language = "Svenska",
            Pages = 191,
            Isbn = "9780306406157",
            Collection = "Skönlitteratur",
            Comments = "Signerad",
            Images = [ScanImageType.FrontCover, ScanImageType.DustJacket],
        });

        Assert.True(clone.Found);
        Assert.Equal(7, clone.BookId);
        Assert.Equal("Kallocain", clone.Title);
        Assert.Equal("En roman om framtiden", clone.Subtitle);
        Assert.Equal("Karin Boye", clone.Authors);
        Assert.Equal("Framtidsserien #2", clone.Series);
        Assert.Equal("Bonniers", clone.Publisher);
        Assert.Equal("1940", clone.PubDate);
        Assert.Equal("Inbunden", clone.Format);
        Assert.Equal("Svenska", clone.Language);
        Assert.Equal(191, clone.Pages);
        Assert.Equal("9780306406157", clone.Isbn);
        Assert.Equal("Skönlitteratur", clone.Collection);
        Assert.Equal("Signerad", clone.Comments);
        Assert.Equal(
            new[] { ScanImageType.FrontCover, ScanImageType.DustJacket }, clone.Images);
    }

    [Fact]
    public void BookDetail_AnAbsentFieldRoundTripsAsAbsentRatherThanAsAnEmptyString()
    {
        var clone = RoundTrip(new BookDetail { Found = false, BookId = 7 });

        Assert.False(clone.Found);
        Assert.Null(clone.Authors);
        Assert.Null(clone.Pages);
        Assert.Empty(clone.Images);
    }
}
