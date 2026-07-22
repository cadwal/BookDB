using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BookDB.Desktop.Services;
using BookDB.Desktop.ViewModels;
using BookDB.Logic.Services;
using BookDB.Models.Metadata;
using Xunit;

namespace BookDB.Desktop.Tests.ViewModels;

/// <summary>
/// The review dialog must open without waiting on cover downloads: cached covers fill their
/// slots synchronously, the rest open as loading placeholders and stream in afterwards.
/// </summary>
public sealed class CoverStreamingTests
{
    private static BookMetadata MakeSource(string sourceName, string? coverUrl = null) =>
        new BookMetadata(
            Title: "Title " + sourceName,
            Subtitle: null,
            Authors: new List<string>(),
            Publisher: null,
            PubDate: null,
            Language: null,
            Isbn: "9780451526538",
            Pages: null,
            Description: null,
            CoverImageUrl: coverUrl,
            Series: null,
            SeriesNumber: null,
            SourceName: sourceName);

    private static MergeReviewViewModel MakeViewModel(IReadOnlyList<CoverOption> coverOptions) =>
        new MergeReviewViewModel(
            sources:
            [
                MakeSource("GoogleBooks"),
                MakeSource("OpenLibrary")
            ],
            currentBook: null,
            coverOptions: coverOptions,
            bookMetadataService: null!,
            messenger: null!,
            existingBookId: null,
            collectionId: null,
            closeDialog: _ => { });

    [Fact]
    public void BuildCoverSlots_CacheHitFillsSlotImmediately_MissBecomesStreamingPlaceholder()
    {
        var sources = new List<BookMetadata>
        {
            MakeSource("GoogleBooks", "https://covers.example/a.jpg"),
            MakeSource("OpenLibrary", "https://covers.example/b.jpg"),
            MakeSource("NoCoverSource")
        };

        var slots = BatchReviewRunner.BuildCoverSlots(
            sources,
            sourceName => sourceName == "GoogleBooks" ? [1, 2] : null,
            out var pendingSlots);

        Assert.Equal(2, slots.Count);

        var cacheHit = Assert.Single(slots, s => s.SourceName == "GoogleBooks");
        Assert.Equal(new byte[] { 1, 2 }, cacheHit.ImageData);
        Assert.False(cacheHit.IsLoading);

        var cacheMiss = Assert.Single(slots, s => s.SourceName == "OpenLibrary");
        Assert.Null(cacheMiss.ImageData);
        Assert.True(cacheMiss.IsLoading);
        Assert.Equal("https://covers.example/b.jpg", cacheMiss.RemoteUrl);

        Assert.Same(cacheMiss, Assert.Single(pendingSlots));
    }

    [Fact]
    public async Task FillCoverSlot_LeavesSlotLoading_UntilTheDownloadCompletes()
    {
        var slot = new CoverOption
        {
            SourceName = "GoogleBooks",
            RemoteUrl = "https://covers.example/a.jpg",
            IsLoading = true
        };
        var fetcher = new GatedCoverFetcher();

        var fillTask = BatchReviewRunner.FillCoverSlotAsync(
            fetcher, slot, "9780451526538", action => action());

        Assert.False(fillTask.IsCompleted);
        Assert.True(slot.IsLoading);
        Assert.Null(slot.ImageData);

        fetcher.Complete([1, 2, 3]);
        await fillTask;

        Assert.False(slot.IsLoading);
        Assert.Equal(new byte[] { 1, 2, 3 }, slot.ImageData);
    }

    [Fact]
    public async Task FillCoverSlot_FailedDownload_ClearsLoadingAndLeavesSlotEmpty()
    {
        var slot = new CoverOption
        {
            SourceName = "GoogleBooks",
            RemoteUrl = "https://covers.example/a.jpg",
            IsLoading = true
        };
        var fetcher = new GatedCoverFetcher();

        var fillTask = BatchReviewRunner.FillCoverSlotAsync(
            fetcher, slot, "9780451526538", action => action());
        fetcher.Complete(null);
        await fillTask;

        Assert.False(slot.IsLoading);
        Assert.Null(slot.ImageData);
    }

    [Fact]
    public void PlaceholderCovers_NothingSelected_UntilTheFirstStreamedCoverArrives()
    {
        var slotA = new CoverOption { SourceName = "GoogleBooks", IsLoading = true };
        var slotB = new CoverOption { SourceName = "OpenLibrary", IsLoading = true };
        var vm = MakeViewModel([slotA, slotB]);

        Assert.DoesNotContain(vm.CoverOptions, co => co.IsSelected);

        slotB.ImageData = [1, 2, 3];

        Assert.True(slotB.IsSelected);
        Assert.False(slotA.IsSelected);

        // A later arrival must not steal the selection.
        slotA.ImageData = [4];

        Assert.True(slotB.IsSelected);
        Assert.False(slotA.IsSelected);
    }

    [Fact]
    public void PreloadedCover_SelectedImmediately_LateArrivalsDoNotOvertake()
    {
        var loaded = new CoverOption { SourceName = "GoogleBooks", ImageData = [7] };
        var placeholder = new CoverOption { SourceName = "OpenLibrary", IsLoading = true };
        var vm = MakeViewModel([loaded, placeholder]);

        Assert.True(loaded.IsSelected);

        placeholder.ImageData = [8];

        Assert.True(loaded.IsSelected);
        Assert.False(placeholder.IsSelected);
    }

    [Fact]
    public void CoverCells_CarryStillLoadingSlots_SoTheDialogOpensWithPlaceholders()
    {
        var placeholder = new CoverOption { SourceName = "GoogleBooks", IsLoading = true };
        var vm = MakeViewModel([placeholder]);

        var googleCell = Assert.Single(vm.CoverCells, c => c.ColumnName == "GoogleBooks");
        Assert.True(googleCell.HasCover);
        Assert.True(googleCell.Cover!.IsLoading);

        var openLibraryCell = Assert.Single(vm.CoverCells, c => c.ColumnName == "OpenLibrary");
        Assert.False(openLibraryCell.HasCover);
    }

    /// <summary>Cover download that completes only when the test releases it.</summary>
    private sealed class GatedCoverFetcher : ICoverFetcher
    {
        private readonly TaskCompletionSource<byte[]?> _tcs =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Complete(byte[]? bytes) => _tcs.SetResult(bytes);

        public Task<byte[]?> DownloadCoverAsync(
            string coverUrl, string isbn, string sourceName, CancellationToken ct = default)
            => _tcs.Task;
    }
}
