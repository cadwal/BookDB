using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BookDB.Contracts;
using BookDB.Mobile.Localization;
using BookDB.Mobile.Services;
using BookDB.Mobile.Staging;
using BookDB.Mobile.ViewModels;
using SkiaSharp;
using Xunit;

namespace BookDB.Mobile.Tests;

/// <summary>The book builder: what the checklist offers, what a capture has to survive before it is kept, what
/// ends up in a staged set, and what a device that cannot capture is told.</summary>
public class BookBuilderViewModelTests
{
    private const string Dune = "9780441013593";

    private sealed record Staged(StagedBook Book, StageAction Action);

    private static BookBuilderViewModel New(
        out FakeDocumentScanner scanner,
        out FakeCaptureProbe probe,
        out List<Staged> staged,
        CaptureSettings? capture = null)
    {
        scanner = new FakeDocumentScanner();
        probe = new FakeCaptureProbe();
        var recorded = new List<Staged>();
        staged = recorded;

        var connection = new FakeConnectionService();
        if (capture is not null)
            connection.ServerInfo = new ServerInfo { Capture = capture };

        return new BookBuilderViewModel(
            Dune, scanner, probe, connection, (book, action) => recorded.Add(new Staged(book, action)));
    }

    private static CaptureSlotViewModel Slot(BookBuilderViewModel vm, ScanImageType type) =>
        vm.Slots.Single(slot => slot.Type == type);

    /// <summary>Capture and keep, in one go — what the tests that care about the staged result do.</summary>
    private static async Task CaptureAndAccept(BookBuilderViewModel vm, ScanImageType type)
    {
        await vm.CaptureCommand.ExecuteAsync(Slot(vm, type));
        await vm.AcceptCommand.ExecuteAsync(null);
    }

    [Fact]
    public void TheChecklist_ExpectsTwoCoversAndOffersTwoMore()
    {
        var vm = New(out _, out _, out _);

        Assert.Equal(
            [ScanImageType.FrontCover, ScanImageType.BackCover, ScanImageType.Spine, ScanImageType.DustJacket],
            vm.Slots.Select(slot => slot.Type));
        Assert.Equal([false, false, true, true], vm.Slots.Select(slot => slot.IsOptional));
        Assert.All(vm.Slots, slot => Assert.False(slot.HasImage));
    }

    [Fact]
    public void TheIsbnJustRead_HeadsTheScreen()
    {
        var vm = New(out _, out _, out _);

        Assert.Equal(Dune, vm.Isbn);
        Assert.Contains(Dune, vm.IsbnLine);
    }

    [Fact]
    public async Task ACapturedPage_WaitsForReviewBeforeItFillsItsSlot()
    {
        var vm = New(out var scanner, out _, out _);
        scanner.Next = DocumentScanResult.Captured(ScanImageEncoderTests.Jpeg(800, 600));

        await vm.CaptureCommand.ExecuteAsync(Slot(vm, ScanImageType.FrontCover));

        Assert.True(vm.HasReview);
        Assert.Equal(Resources.Builder_SlotFrontCover, vm.ReviewLabel);
        Assert.False(Slot(vm, ScanImageType.FrontCover).HasImage);

        await vm.AcceptCommand.ExecuteAsync(null);

        Assert.False(vm.HasReview);
        Assert.True(Slot(vm, ScanImageType.FrontCover).HasImage);
    }

    [Fact]
    public async Task AnAcceptedPage_IsSizedToWhatTheComputerAskedFor()
    {
        var vm = New(out var scanner, out _, out _, new CaptureSettings { MaxLongEdgePx = 640, JpegQuality = 70 });
        scanner.Next = DocumentScanResult.Captured(ScanImageEncoderTests.Jpeg(2400, 1800));

        await CaptureAndAccept(vm, ScanImageType.FrontCover);

        using var kept = SKBitmap.Decode(Slot(vm, ScanImageType.FrontCover).Jpeg);
        Assert.Equal(640, kept.Width);
        Assert.Equal(480, kept.Height);
    }

    [Fact]
    public async Task RetakingAPage_ThrowsTheCropAwayAndOpensTheScannerAgain()
    {
        var vm = New(out var scanner, out _, out _);
        scanner.Results.Enqueue(DocumentScanResult.Captured(ScanImageEncoderTests.Jpeg(400, 300)));
        scanner.Results.Enqueue(DocumentScanResult.Cancelled);

        await vm.CaptureCommand.ExecuteAsync(Slot(vm, ScanImageType.FrontCover));
        await vm.RetakeCommand.ExecuteAsync(null);

        Assert.Equal(2, scanner.Opened);
        Assert.False(vm.HasReview);
        Assert.False(Slot(vm, ScanImageType.FrontCover).HasImage);
    }

    [Fact]
    public async Task ACancelledCapture_LeavesTheSlotEmptyAndSaysNothing()
    {
        var vm = New(out var scanner, out _, out _);
        scanner.Next = DocumentScanResult.Cancelled;

        await vm.CaptureCommand.ExecuteAsync(Slot(vm, ScanImageType.BackCover));

        Assert.False(vm.HasReview);
        Assert.False(Slot(vm, ScanImageType.BackCover).HasImage);
        Assert.Null(vm.Message);
        Assert.False(vm.CanRetryCapture);
    }

    [Fact]
    public async Task ADeviceWithoutPlayServices_IsToldSoAndTheScannerIsNeverOpened()
    {
        var vm = New(out var scanner, out var probe, out _);
        probe.Availability = CaptureAvailability.PlayServicesMissing;

        await vm.CaptureCommand.ExecuteAsync(Slot(vm, ScanImageType.FrontCover));

        Assert.Equal(Resources.Builder_PlayServicesMissing, vm.Message);
        Assert.Equal(0, scanner.Opened);
        Assert.False(vm.CanRetryCapture);
        Assert.False(vm.RetryCaptureCommand.CanExecute(null));
    }

    [Fact]
    public async Task OutdatedPlayServices_GetTheirOwnMessage()
    {
        var vm = New(out _, out var probe, out _);
        probe.Availability = CaptureAvailability.PlayServicesOutdated;

        await vm.CaptureCommand.ExecuteAsync(Slot(vm, ScanImageType.FrontCover));

        Assert.Equal(Resources.Builder_PlayServicesOutdated, vm.Message);
    }

    [Fact]
    public async Task AScannerModuleThatIsNotThereYet_IsWorthAnotherTry()
    {
        var vm = New(out var scanner, out _, out _);
        scanner.Results.Enqueue(DocumentScanResult.ModuleUnavailable);
        scanner.Results.Enqueue(DocumentScanResult.Captured(ScanImageEncoderTests.Jpeg(400, 300)));

        await vm.CaptureCommand.ExecuteAsync(Slot(vm, ScanImageType.Spine));

        Assert.Equal(Resources.Builder_ModuleUnavailable, vm.Message);
        Assert.True(vm.CanRetryCapture);
        Assert.True(vm.RetryCaptureCommand.CanExecute(null));

        await vm.RetryCaptureCommand.ExecuteAsync(null);
        await vm.AcceptCommand.ExecuteAsync(null);

        Assert.Null(vm.Message);
        Assert.True(Slot(vm, ScanImageType.Spine).HasImage);
    }

    [Fact]
    public async Task ACaptureThatFails_SaysSoAndOffersAnotherTry()
    {
        var vm = New(out var scanner, out _, out _);
        scanner.Next = DocumentScanResult.Failed;

        await vm.CaptureCommand.ExecuteAsync(Slot(vm, ScanImageType.FrontCover));

        Assert.Equal(Resources.Builder_CaptureFailed, vm.Message);
        Assert.True(vm.CanRetryCapture);
    }

    [Fact]
    public void ABookWithNoPhotosAtAll_IsStillACompleteSet()
    {
        var vm = New(out _, out _, out var staged);

        vm.DoneCommand.Execute(null);

        var set = Assert.Single(staged);
        Assert.Equal(Dune, set.Book.Isbn);
        Assert.Empty(set.Book.Images);
        Assert.NotEmpty(set.Book.ClientItemId);
    }

    [Fact]
    public async Task AStagedSet_CarriesOneSizedJpegPerImageType()
    {
        var vm = New(out var scanner, out _, out var staged, new CaptureSettings { MaxLongEdgePx = 500, JpegQuality = 80 });
        scanner.Next = DocumentScanResult.Captured(ScanImageEncoderTests.Jpeg(2000, 1500));

        await CaptureAndAccept(vm, ScanImageType.FrontCover);
        await CaptureAndAccept(vm, ScanImageType.DustJacket);

        vm.SaveToBatchCommand.Execute(null);

        var set = Assert.Single(staged);
        Assert.Equal([ScanImageType.FrontCover, ScanImageType.DustJacket], set.Book.Images.Select(i => i.Type));
        Assert.All(set.Book.Images, image =>
        {
            using var decoded = SKBitmap.Decode(image.Jpeg);
            Assert.Equal(500, decoded.Width);
        });
    }

    [Theory]
    [InlineData(nameof(StageAction.NextBook))]
    [InlineData(nameof(StageAction.SendNow))]
    [InlineData(nameof(StageAction.SaveToBatch))]
    public void EachWayOfFinishing_StagesTheSetAndSaysWhichItWas(string action)
    {
        var vm = New(out _, out _, out var staged);

        switch (action)
        {
            case nameof(StageAction.NextBook): vm.DoneCommand.Execute(null); break;
            case nameof(StageAction.SendNow): vm.SendNowCommand.Execute(null); break;
            default: vm.SaveToBatchCommand.Execute(null); break;
        }

        Assert.Equal(action, Assert.Single(staged).Action.ToString());
    }

    [Fact]
    public void TwoBooks_GetIdsOfTheirOwn()
    {
        var first = New(out _, out _, out var staged);
        var second = New(out _, out _, out var alsoStaged);

        first.DoneCommand.Execute(null);
        second.DoneCommand.Execute(null);

        Assert.NotEqual(staged[0].Book.ClientItemId, alsoStaged[0].Book.ClientItemId);
    }

    /// <summary>The id is the book's, not the button's: a double-tap must not stage the same book twice.</summary>
    [Fact]
    public void FinishingTheSameBookTwice_KeepsOneId()
    {
        var vm = New(out _, out _, out var staged);

        vm.DoneCommand.Execute(null);
        vm.SaveToBatchCommand.Execute(null);

        Assert.Equal(staged[0].Book.ClientItemId, staged[1].Book.ClientItemId);
    }

    /// <summary>Re-opening a staged book from the tray: its covers are already in their slots, and finishing
    /// replaces that book rather than staging a second copy of it.</summary>
    [Fact]
    public async Task ABookReopenedForEditing_StartsFromWhatWasAlreadyCaptured()
    {
        var existing = new StagedBook
        {
            ClientItemId = "aaa1",
            Isbn = Dune,
            Images = [new StagedImage { Type = ScanImageType.BackCover, Jpeg = ScanImageEncoderTests.Jpeg(300, 200) }],
        };
        var recorded = new List<Staged>();
        var scanner = new FakeDocumentScanner
        {
            Next = DocumentScanResult.Captured(ScanImageEncoderTests.Jpeg(800, 600)),
        };
        var vm = new BookBuilderViewModel(
            Dune, scanner, new FakeCaptureProbe(), new FakeConnectionService(),
            (book, action) => recorded.Add(new Staged(book, action)), existing);

        Assert.True(Slot(vm, ScanImageType.BackCover).HasImage);
        Assert.False(Slot(vm, ScanImageType.FrontCover).HasImage);

        await CaptureAndAccept(vm, ScanImageType.FrontCover);
        vm.SaveToBatchCommand.Execute(null);

        var set = Assert.Single(recorded);
        Assert.Equal("aaa1", set.Book.ClientItemId);
        Assert.Equal([ScanImageType.FrontCover, ScanImageType.BackCover], set.Book.Images.Select(image => image.Type));
    }
}
