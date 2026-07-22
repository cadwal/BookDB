using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BookDB.Data.DbContexts;
using BookDB.Desktop.Localization;
using BookDB.Desktop.Services;
using BookDB.Desktop.ViewModels;
using BookDB.Logic.Services;
using BookDB.MetadataSources.Services;
using BookDB.Models.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace BookDB.Desktop.UITests;

/// <summary>
/// The guided identify stage: an ISBN lookup runs through the batch-queue seam with the
/// force-review flag (so even an agreeing single source lands in review), a known ISBN hits the
/// duplicate dialog before anything is enqueued, and a failed lookup shows its localized reason
/// while the dialog stays open for the manual path.
/// </summary>
public class AddBookIdentifyFlowTests : HeadlessTest
{
    private const string Isbn = "9780451526538";

    private static BookMetadata MakeSource(string sourceName, string title) =>
        new(Title: title, Subtitle: null, Authors: ["George Orwell"], Publisher: null, PubDate: null,
            Language: null, Isbn: Isbn, Pages: null, Description: null,
            CoverImageUrl: null, Series: null, SeriesNumber: null, SourceName: sourceName);

    private sealed class FakeLookupService(MetadataLookupResult result) : IMetadataLookupService
    {
        public Task<MetadataLookupResult> FetchAllSourcesAsync(string isbn, CancellationToken ct = default)
            => Task.FromResult(result);
    }

    private static AddBookIdentifyViewModel MakeVm(TestHost host, out List<bool?> closedWith)
    {
        var vm = host.Resolve<AddBookIdentifyViewModel>();
        vm.Initialize(null);
        var closes = new List<bool?>();
        vm.CloseDialog = r => closes.Add(r);
        closedWith = closes;
        return vm;
    }

    [Fact]
    public async Task Lookup_ForcesReviewEvenWithOneAgreeingSource_AndSavesThroughTheReviewDialog()
    {
        var ct = TestContext.Current.CancellationToken;

        await RunUi(async () =>
        {
            var windowService = Substitute.For<IWindowService>();
            windowService.ShowMergeReviewDialogAsync(
                    Arg.Any<IReadOnlyList<BookMetadata>>(), Arg.Any<BookMetadata?>(),
                    Arg.Any<IReadOnlyList<CoverOption>>(), Arg.Any<int?>(), Arg.Any<int?>(),
                    Arg.Any<IReadOnlyList<string>?>(),
                    Arg.Any<IReadOnlyList<string>?>(),
                    Arg.Any<IReadOnlyList<string>?>(),
                    Arg.Any<Avalonia.Controls.Window?>())
                .Returns(Task.FromResult<bool?>(true));
            using var host = TestHost.Create(s =>
            {
                s.AddSingleton<IMetadataLookupService>(
                    new FakeLookupService(new MetadataLookupResult([MakeSource("GoogleBooks", "1984")], 1, 0)));
                s.AddSingleton(windowService);
            });

            var vm = MakeVm(host, out var closedWith);
            vm.IsbnText = "978-0-451-52653-8";
            await vm.LookUpCommand.ExecuteAsync(null);

            Assert.Equal(new bool?[] { true }, closedWith);
            await windowService.Received(1).ShowMergeReviewDialogAsync(
                Arg.Any<IReadOnlyList<BookMetadata>>(), Arg.Any<BookMetadata?>(),
                Arg.Any<IReadOnlyList<CoverOption>>(), null, null,
                Arg.Any<IReadOnlyList<string>?>(),
                Arg.Any<IReadOnlyList<string>?>(),
                Arg.Any<IReadOnlyList<string>?>(),
                Arg.Any<Avalonia.Controls.Window?>());

            var queueService = host.Resolve<BatchQueueService>();
            var done = await queueService.GetItemsByStatusAsync("Done", ct);
            var item = Assert.Single(done);
            Assert.Equal(Isbn, item.Isbn);
            Assert.True(item.ForceReview);
        });
    }

    [Fact]
    public async Task FailedLookup_ShowsTheLocalizedReason_AndLeavesTheDialogOpenForManualEntry()
    {
        var ct = TestContext.Current.CancellationToken;

        await RunUi(async () =>
        {
            using var host = TestHost.Create(s =>
                s.AddSingleton<IMetadataLookupService>(
                    new FakeLookupService(new MetadataLookupResult([], SourcesQueried: 2, SourcesFailed: 0))));

            var vm = MakeVm(host, out var closedWith);
            vm.IsbnText = Isbn;
            await vm.LookUpCommand.ExecuteAsync(null);

            Assert.Empty(closedWith);
            Assert.True(vm.HasFailure);
            Assert.Equal(
                string.Format(Resources.AddBookIdentify_LookupFailed, Resources.BatchQueue_Failure_NoResults),
                vm.FailureText);

            var queueService = host.Resolve<BatchQueueService>();
            var failed = await queueService.GetItemsByStatusAsync("Failed", ct);
            Assert.Single(failed);
        });
    }

    [Fact]
    public async Task ManualEntry_ClosesTheDialog_AndCarriesTheNormalizedIsbnIntoTheManualStage()
    {
        await RunUi(async () =>
        {
            var windowService = Substitute.For<IWindowService>();
            using var host = TestHost.Create(s => s.AddSingleton(windowService));

            var vm = MakeVm(host, out var closedWith);
            vm.IsbnText = "978-0-451-52653-8";
            await vm.ManualEntryCommand.ExecuteAsync(null);

            Assert.Equal(new bool?[] { false }, closedWith);
            await windowService.Received(1).ShowAddBookDialogAsync(null, Isbn);
        });
    }

    [Fact]
    public async Task KnownIsbn_CancelAtTheDuplicateDialog_EnqueuesNothing()
    {
        var ct = TestContext.Current.CancellationToken;

        await RunUi(async () =>
        {
            var windowService = Substitute.For<IWindowService>();
            windowService.ShowDuplicateIsbnDialogAsync(Arg.Any<string>(), Arg.Any<string>())
                .Returns(Task.FromResult(DuplicateIsbnResult.Cancel));
            using var host = TestHost.Create(s =>
            {
                s.AddSingleton<IMetadataLookupService>(
                    new FakeLookupService(new MetadataLookupResult([MakeSource("GoogleBooks", "1984")], 1, 0)));
                s.AddSingleton(windowService);
            });
            await SeedData.AddBookAsync(host, "Existing 1984", Isbn, ct);

            var vm = MakeVm(host, out var closedWith);
            vm.IsbnText = Isbn;
            await vm.LookUpCommand.ExecuteAsync(null);

            Assert.Empty(closedWith);
            await windowService.Received(1).ShowDuplicateIsbnDialogAsync(Isbn, "Existing 1984");

            var factory = host.Resolve<IDbContextFactory<BookDbContext>>();
            await using var db = await factory.CreateDbContextAsync(ct);
            Assert.Equal(0, await db.BatchQueueItems.CountAsync(ct));
        });
    }

    [Fact]
    public async Task KnownIsbn_UpdateExisting_RoutesTheLookupOntoTheExistingBook()
    {
        var ct = TestContext.Current.CancellationToken;

        await RunUi(async () =>
        {
            var windowService = Substitute.For<IWindowService>();
            windowService.ShowDuplicateIsbnDialogAsync(Arg.Any<string>(), Arg.Any<string>())
                .Returns(Task.FromResult(DuplicateIsbnResult.UpdateExisting));
            windowService.ShowMergeReviewDialogAsync(
                    Arg.Any<IReadOnlyList<BookMetadata>>(), Arg.Any<BookMetadata?>(),
                    Arg.Any<IReadOnlyList<CoverOption>>(), Arg.Any<int?>(), Arg.Any<int?>(),
                    Arg.Any<IReadOnlyList<string>?>(),
                    Arg.Any<IReadOnlyList<string>?>(),
                    Arg.Any<IReadOnlyList<string>?>(),
                    Arg.Any<Avalonia.Controls.Window?>())
                .Returns(Task.FromResult<bool?>(true));
            using var host = TestHost.Create(s =>
            {
                s.AddSingleton<IMetadataLookupService>(
                    new FakeLookupService(new MetadataLookupResult([MakeSource("GoogleBooks", "1984")], 1, 0)));
                s.AddSingleton(windowService);
            });
            var existing = await SeedData.AddBookAsync(host, "Existing 1984", Isbn, ct);

            var vm = MakeVm(host, out var closedWith);
            vm.IsbnText = Isbn;
            await vm.LookUpCommand.ExecuteAsync(null);

            Assert.Equal(new bool?[] { true }, closedWith);
            var queueService = host.Resolve<BatchQueueService>();
            var done = await queueService.GetItemsByStatusAsync("Done", ct);
            var item = Assert.Single(done);
            Assert.Equal(existing.BookId, item.BookId);
            await windowService.Received(1).ShowMergeReviewDialogAsync(
                Arg.Any<IReadOnlyList<BookMetadata>>(), Arg.Any<BookMetadata?>(),
                Arg.Any<IReadOnlyList<CoverOption>>(), existing.BookId, null,
                Arg.Any<IReadOnlyList<string>?>(),
                Arg.Any<IReadOnlyList<string>?>(),
                Arg.Any<IReadOnlyList<string>?>(),
                Arg.Any<Avalonia.Controls.Window?>());
        });
    }
}
