using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using BookDB.Desktop.Services;
using BookDB.Desktop.ViewModels;
using BookDB.Desktop.Views;
using BookDB.Logic.Services;
using BookDB.Models.Metadata;
using CommunityToolkit.Mvvm.Messaging;
using Xunit;

namespace BookDB.Desktop.UITests;

/// <summary>
/// The merge-review cover strip while covers stream in: a still-downloading slot renders a live
/// progress indicator and cannot be selected; once its cover arrives the indicator disappears,
/// the thumbnail becomes selectable, and the first arrival claims the default selection.
/// </summary>
public class MergeReviewCoverStreamingTests : HeadlessTest
{
    // A real decodable 1×1 PNG — the fill path decodes the downloaded bytes into a Bitmap.
    private static readonly byte[] OnePixelPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");

    private static BookMetadata MakeSource(string sourceName, string title) =>
        new BookMetadata(
            Title: title,
            Subtitle: null,
            Authors: new List<string>(),
            Publisher: null,
            PubDate: null,
            Language: null,
            Isbn: "9780451526538",
            Pages: null,
            Description: null,
            CoverImageUrl: "https://covers.example/" + sourceName + ".jpg",
            Series: null,
            SeriesNumber: null,
            SourceName: sourceName);

    [Fact]
    public async Task StreamingSlot_ShowsProgressIndicator_ThenBecomesSelectableWhenTheCoverArrives()
    {
        await RunUi(async () =>
        {
            using var host = TestHost.Create();
            var googleSlot = new CoverOption
            {
                SourceName = "GoogleBooks",
                RemoteUrl = "https://covers.example/GoogleBooks.jpg",
                IsLoading = true
            };
            var openLibrarySlot = new CoverOption
            {
                SourceName = "OpenLibrary",
                RemoteUrl = "https://covers.example/OpenLibrary.jpg",
                IsLoading = true
            };
            var vm = new MergeReviewViewModel(
                sources:
                [
                    MakeSource("GoogleBooks", "Title A"),
                    MakeSource("OpenLibrary", "Title B")
                ],
                currentBook: null,
                coverOptions: [googleSlot, openLibrarySlot],
                bookMetadataService: host.Resolve<IBookMetadataService>(),
                messenger: host.Resolve<IMessenger>(),
                existingBookId: null,
                collectionId: null,
                closeDialog: _ => { },
                windowService: host.Resolve<IWindowService>());
            var dialog = new MergeReviewDialog { DataContext = vm };
            dialog.Show();
            Ui.Pump();

            var googleButton = dialog.Descendants<Button>()
                .Single(b => ReferenceEquals(b.CommandParameter, googleSlot));
            var openLibraryButton = dialog.Descendants<Button>()
                .Single(b => ReferenceEquals(b.CommandParameter, openLibrarySlot));

            // Both slots are still downloading: indicators shown, thumbnails not selectable.
            Assert.True(googleButton.Find<ProgressBar>().IsEffectivelyVisible);
            Assert.True(openLibraryButton.Find<ProgressBar>().IsEffectivelyVisible);
            Assert.False(googleButton.IsEffectivelyEnabled);
            Assert.False(openLibraryButton.IsEffectivelyEnabled);
            Assert.DoesNotContain(vm.CoverOptions, co => co.IsSelected);

            // The GoogleBooks cover arrives while the dialog is already open.
            await BatchReviewRunner.FillCoverSlotAsync(
                new OnePngCoverFetcher(OnePixelPng), googleSlot, "9780451526538", action => action());
            Ui.Pump();

            Assert.False(googleButton.Find<ProgressBar>().IsEffectivelyVisible);
            Assert.True(googleButton.IsEffectivelyEnabled);
            Assert.NotNull(googleSlot.ThumbnailBitmap);
            Assert.True(googleSlot.IsSelected);

            // The other slot is still streaming and stays in the loading state.
            Assert.True(openLibraryButton.Find<ProgressBar>().IsEffectivelyVisible);
            Assert.False(openLibraryButton.IsEffectivelyEnabled);

            dialog.Close();
        });
    }

    /// <summary>Returns the given PNG bytes for any URL without touching the network.</summary>
    private sealed class OnePngCoverFetcher(byte[] png) : ICoverFetcher
    {
        public Task<byte[]?> DownloadCoverAsync(
            string coverUrl, string isbn, string sourceName, CancellationToken ct = default)
            => Task.FromResult<byte[]?>(png);
    }
}
