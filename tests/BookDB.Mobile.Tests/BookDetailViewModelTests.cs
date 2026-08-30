using System.Linq;
using System.Threading.Tasks;
using BookDB.Contracts;
using BookDB.Mobile.Localization;
using BookDB.Mobile.Services;
using BookDB.Mobile.ViewModels;
using Xunit;

namespace BookDB.Mobile.Tests;

/// <summary>
/// The read-only book screen: which fields it shows, what it does with the ones the book has not got, how
/// the photos arrive, and what it says when the book — or the computer — is gone.
/// </summary>
public class BookDetailViewModelTests
{
    private sealed class StubConnection : ICompanionConnection
    {
        public StubConnection(IBookScannerService service) => Service = service;

        public IBookScannerService Service { get; }

        public void Dispose() { }
    }

    private static BookDetailViewModel New(
        StubScannerService desktop, int bookId = 7, string fallbackTitle = "Dune", bool offline = false)
    {
        var connection = new FakeConnectionService();
        if (!offline)
        {
            connection.Connection = new StubConnection(desktop);
        }

        return new BookDetailViewModel(connection, bookId, fallbackTitle);
    }

    private static BookDetail Full() => new()
    {
        Found = true,
        BookId = 7,
        Title = "Kallocain",
        Subtitle = "En roman om framtiden",
        Authors = "Karin Boye",
        Series = "Framtidsserien #2",
        Publisher = "Bonniers",
        PubDate = "1940",
        Format = "Hardcover",
        Language = "Swedish",
        Pages = 191,
        Isbn = "9780306406157",
        Collection = "Fiction",
        Comments = "Signed",
    };

    [Fact]
    public async Task ABookWithEverything_ShowsEveryFieldInTheOrderTheScreenLaysOut()
    {
        var desktop = new StubScannerService();
        desktop.Details[7] = Full();
        var vm = New(desktop);

        await vm.LoadCommand.ExecuteAsync(null);

        Assert.Equal("Kallocain", vm.BookTitle);
        Assert.Equal("Kallocain", vm.Title);
        Assert.True(vm.HasSubtitle);
        Assert.Equal("En roman om framtiden", vm.Subtitle);
        Assert.Equal(
            [
                Resources.Detail_Field_Authors,
                Resources.Detail_Field_Series,
                Resources.Detail_Field_Publisher,
                Resources.Detail_Field_Published,
                Resources.Detail_Field_Format,
                Resources.Detail_Field_Language,
                Resources.Detail_Field_Pages,
                Resources.Detail_Field_Isbn,
                Resources.Detail_Field_Collection,
                Resources.Detail_Field_Comments,
            ],
            vm.Fields.Select(f => f.Label));
        Assert.Equal("Karin Boye", vm.Fields[0].Value);
        Assert.Equal("191", vm.Fields[6].Value);
    }

    [Fact]
    public async Task AFieldTheBookHasNot_IsAbsentRatherThanAnEmptyLine()
    {
        var desktop = new StubScannerService();
        desktop.Details[7] = new BookDetail { Found = true, BookId = 7, Title = "Kallocain" };
        var vm = New(desktop);

        await vm.LoadCommand.ExecuteAsync(null);

        Assert.Empty(vm.Fields);
        Assert.False(vm.HasSubtitle);
        Assert.False(vm.HasImages);
    }

    [Fact]
    public async Task TheRowsTitleStandsInUntilTheDetailArrives()
    {
        var vm = New(new StubScannerService(), fallbackTitle: "Dune", offline: true);

        Assert.Equal("Dune", vm.Title);

        await vm.LoadCommand.ExecuteAsync(null);

        Assert.Equal("Dune", vm.Title);
    }

    [Fact]
    public async Task ABookWithNoTitleOfItsOwn_KeepsTheOneTheRowShowed()
    {
        var desktop = new StubScannerService();
        desktop.Details[7] = new BookDetail { Found = true, BookId = 7, Title = "" };
        var vm = New(desktop, fallbackTitle: "Dune");

        await vm.LoadCommand.ExecuteAsync(null);

        Assert.Equal("Dune", vm.BookTitle);
    }

    [Fact]
    public async Task ThePhotosAreNamedFirstAndFetchedOneAtATimeForThisBook()
    {
        var desktop = new StubScannerService();
        var detail = Full();
        detail.Images = [ScanImageType.FrontCover, ScanImageType.Spine];
        desktop.Details[7] = detail;
        desktop.Images[(7, ScanImageType.FrontCover)] = [1, 2, 3];
        desktop.Images[(7, ScanImageType.Spine)] = [4, 5];
        var vm = New(desktop);

        await vm.LoadCommand.ExecuteAsync(null);

        Assert.True(vm.HasImages);
        Assert.Equal(
            [Resources.Builder_SlotFrontCover, Resources.Builder_SlotSpine],
            vm.Images.Select(i => i.Label));
        Assert.Equal(new byte[] { 1, 2, 3 }, vm.Images[0].Jpeg);
        Assert.Equal(new byte[] { 4, 5 }, vm.Images[1].Jpeg);
        Assert.All(desktop.ImageReads, read => Assert.Equal(7, read.BookId));
    }

    /// <summary>Stored covers are far larger than a phone screen; asking for them whole would be megabytes
    /// per photo.</summary>
    [Fact]
    public async Task ThePhotosAreAskedForAtAScreenSizeRatherThanWhole()
    {
        var desktop = new StubScannerService();
        var detail = Full();
        detail.Images = [ScanImageType.FrontCover];
        desktop.Details[7] = detail;
        var vm = New(desktop);

        await vm.LoadCommand.ExecuteAsync(null);

        Assert.Equal(BookDetailViewModel.ImageLongEdgePx, Assert.Single(desktop.ImageReads).MaxLongEdgePx);
    }

    [Fact]
    public async Task ABookDeletedSinceItWasListed_SaysSoRatherThanShowingAnEmptyScreen()
    {
        var vm = New(new StubScannerService());

        await vm.LoadCommand.ExecuteAsync(null);

        Assert.True(vm.IsMissing);
        Assert.False(vm.IsUnreachable);
        Assert.Empty(vm.Fields);
    }

    [Fact]
    public async Task AnUnreachableComputer_IsAnExplanationRatherThanAMissingBook()
    {
        var vm = New(new StubScannerService(), offline: true);

        await vm.LoadCommand.ExecuteAsync(null);

        Assert.True(vm.IsUnreachable);
        Assert.False(vm.IsMissing);
    }

    [Fact]
    public async Task AComputerThatComesBack_FillsTheScreenInOnRetry()
    {
        var desktop = new StubScannerService { BrowseFails = true };
        desktop.Details[7] = Full();
        var vm = New(desktop);
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.True(vm.IsUnreachable);

        desktop.BrowseFails = false;
        await vm.RetryCommand.ExecuteAsync(null);

        Assert.False(vm.IsUnreachable);
        Assert.Equal("Kallocain", vm.BookTitle);
    }

    [Fact]
    public async Task ReloadingDoesNotShowEveryFieldTwice()
    {
        var desktop = new StubScannerService();
        desktop.Details[7] = Full();
        var vm = New(desktop);

        await vm.LoadCommand.ExecuteAsync(null);
        await vm.RetryCommand.ExecuteAsync(null);

        Assert.Equal(10, vm.Fields.Count);
    }

    /// <summary>A format and language the computer seeded are said in this device's words, whatever language
    /// the library itself was set up in.</summary>
    [Fact]
    public async Task SeededFormatAndLanguage_ReadInThisDevicesLanguage()
    {
        var detail = Full();
        detail.Format = "Gebundenes Buch";
        detail.FormatKey = "Format_Hardcover";
        detail.Language = "Schwedisch";
        detail.LanguageKey = "Language_Swedish";

        var desktop = new StubScannerService();
        desktop.Details[7] = detail;
        var vm = New(desktop);

        await vm.LoadCommand.ExecuteAsync(null);

        Assert.Equal(Resources.Format_Hardcover, Value(vm, Resources.Detail_Field_Format));
        Assert.Equal(Resources.Language_Swedish, Value(vm, Resources.Detail_Field_Language));
    }

    /// <summary>A row the user typed has no key, so it travels as the word they typed and is shown as it is.</summary>
    [Fact]
    public async Task AFormatTheUserInvented_IsShownAsTheyWroteIt()
    {
        var detail = Full();
        detail.Format = "Zine";
        detail.FormatKey = null;

        var desktop = new StubScannerService();
        desktop.Details[7] = detail;
        var vm = New(desktop);

        await vm.LoadCommand.ExecuteAsync(null);

        Assert.Equal("Zine", Value(vm, Resources.Detail_Field_Format));
    }

    private static string Value(BookDetailViewModel vm, string label) =>
        vm.Fields.Single(f => f.Label == label).Value;
}
