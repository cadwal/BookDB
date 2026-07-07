using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using BookDB.Desktop.Services;
using BookDB.Desktop.ViewModels;
using BookDB.Desktop.Views;
using BookDB.Logic.Services;
using BookDB.Models.Entities;
using BookDB.Models.Interfaces;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace BookDB.Desktop.UITests;

/// <summary>
/// Cover image journeys through the edit form's Images tab. Attaching a picked file previews it immediately but
/// stages the bytes — only Save writes the BookImages row for the selected type, one row per type. Remove clears
/// the staged type the same way, and discarding an edit leaves the database untouched. The preview drives the
/// NativeImageWidth/Height size caps (the past null→double binding-bug area, now watched by the binding gate).
/// </summary>
public class CoverImageFlowTests : HeadlessTest
{
    // A real decodable 1×1 PNG — the preview path decodes the attached bytes into a Bitmap.
    private static readonly byte[] OnePixelPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");

    [Fact]
    public async Task AttachingSavingAndRemovingCovers_RoundTripsPerImageType()
    {
        var ct = TestContext.Current.CancellationToken;
        var picker = Substitute.For<IFilePickerService>();

        await RunUi(async () =>
        {
            using var host = TestHost.Create(s => s.AddSingleton(picker));
            var book = await SeedData.AddBookAsync(host, "Covered Book", ct);
            var pngPath = await WriteTempPngAsync();
            picker.PickFileAsync(Arg.Any<string>(), Arg.Any<System.Collections.Generic.IReadOnlyList<string>>())
                .Returns(pngPath);

            try
            {
                var (vm, window) = await OpenImagesTabAsync(host, book.BookId);
                var images = host.Resolve<IBookImageService>();

                // No cover yet: the empty label shows and the Remove button stays hidden.
                Assert.Null(vm.ImageEditor.CoverBitmap);
                Assert.Contains(window.Descendants<TextBlock>(),
                    t => t.IsEffectivelyVisible && t.Text == vm.ImageEditor.SelectedTypeEmptyLabel);
                var removeButton = window.ButtonFor(vm.ImageEditor.RemoveCoverForSelectedTypeCommand);
                Assert.False(removeButton.IsEffectivelyVisible);

                // Attach stages the picked file and previews it at its native 1×1 size, but writes nothing yet.
                await Ui.ClickAsync(window.ButtonFor(vm.ImageEditor.AttachCoverFromFileCommand));
                Assert.NotNull(vm.ImageEditor.CoverBitmap);
                Assert.Equal(1, vm.ImageEditor.NativeImageWidth);
                Assert.Equal(1, vm.ImageEditor.NativeImageHeight);
                Assert.True(vm.HasUnsavedChanges);
                Assert.True(removeButton.IsEffectivelyVisible);
                Assert.Empty(await images.GetBookImagesAsync(book.BookId, ct));

                // Save lands the front-cover row with the exact picked bytes.
                await Ui.ClickAsync(window.ButtonFor(vm.SaveCommand));
                var saved = Assert.Single(await images.GetBookImagesAsync(book.BookId, ct));
                Assert.Equal(BookImageTypeId.FrontCover, saved.BookImageTypeId);
                Assert.Equal(OnePixelPng, saved.ImageData);

                // A second image on another type: pick Back via its type button, attach, save — one row per type.
                await Ui.ClickAsync(TypeButton(window, vm, BookImageTypeId.BackCover));
                Assert.Equal(BookImageTypeId.BackCover, vm.ImageEditor.SelectedImageTypeId);
                Assert.Null(vm.ImageEditor.CoverBitmap);
                await Ui.ClickAsync(window.ButtonFor(vm.ImageEditor.AttachCoverFromFileCommand));
                await Ui.ClickAsync(window.ButtonFor(vm.SaveCommand));
                var byType = await images.GetBookImagesAsync(book.BookId, ct);
                Assert.Equal(2, byType.Count);
                Assert.Contains(byType, i => i.BookImageTypeId == BookImageTypeId.FrontCover);
                Assert.Contains(byType, i => i.BookImageTypeId == BookImageTypeId.BackCover);

                // Remove clears the selected type only: back on Front, remove, save — the back cover survives.
                await Ui.ClickAsync(TypeButton(window, vm, BookImageTypeId.FrontCover));
                Assert.NotNull(vm.ImageEditor.CoverBitmap);
                await Ui.ClickAsync(window.ButtonFor(vm.ImageEditor.RemoveCoverForSelectedTypeCommand));
                Assert.Null(vm.ImageEditor.CoverBitmap);
                Assert.False(removeButton.IsEffectivelyVisible);
                await Ui.ClickAsync(window.ButtonFor(vm.SaveCommand));
                var remaining = Assert.Single(await images.GetBookImagesAsync(book.BookId, ct));
                Assert.Equal(BookImageTypeId.BackCover, remaining.BookImageTypeId);
                window.Close();
            }
            finally { File.Delete(pngPath); }
        });
    }

    [Fact]
    public async Task DiscardingAnAttachedCover_PersistsNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        var picker = Substitute.For<IFilePickerService>();
        var windowService = Substitute.For<IWindowService>();
        windowService.ShowUnsavedChangesDialogAsync(Arg.Any<string>()).Returns(UnsavedChangesResult.Discard);

        await RunUi(async () =>
        {
            using var host = TestHost.Create(s =>
            {
                s.AddSingleton(picker);
                s.AddSingleton(windowService);
            });
            var book = await SeedData.AddBookAsync(host, "Uncovered Book", ct);
            var pngPath = await WriteTempPngAsync();
            picker.PickFileAsync(Arg.Any<string>(), Arg.Any<System.Collections.Generic.IReadOnlyList<string>>())
                .Returns(pngPath);

            try
            {
                var (vm, window) = await OpenImagesTabAsync(host, book.BookId);

                await Ui.ClickAsync(window.ButtonFor(vm.ImageEditor.AttachCoverFromFileCommand));
                Assert.True(vm.HasUnsavedChanges);

                await Ui.ClickAsync(window.ButtonFor(vm.CancelEditCommand));
                Assert.False(vm.HasUnsavedChanges);
                Assert.Null(vm.ImageEditor.CoverBitmap);
                Assert.Empty(await host.Resolve<IBookImageService>().GetBookImagesAsync(book.BookId, ct));
                window.Close();
            }
            finally { File.Delete(pngPath); }
        });
    }

    // ─── Plumbing ────────────────────────────────────────────────────────────

    /// <summary>Loads the book into the full-details window and brings up its Images tab (a TabControl only
    /// realizes the selected tab's content, so the image buttons don't exist in the tree until then).</summary>
    private static async Task<(FullDetailsWindowViewModel Vm, FullDetailsWindow Window)> OpenImagesTabAsync(
        TestHost host, int bookId)
    {
        var vm = host.Resolve<FullDetailsWindowViewModel>();
        Assert.True(await vm.LoadBookAsync(bookId));
        var window = new FullDetailsWindow { DataContext = vm };
        window.Show();
        Ui.Pump();
        var tabs = window.Find<TabControl>();
        tabs.SelectedItem = tabs.Items.Cast<TabItem>()
            .Single(t => Equals(t.Header, BookDB.Desktop.Localization.Resources.BookEditForm_Tab_Images));
        Ui.Pump();
        return (vm, window);
    }

    /// <summary>The per-type selector button at the top of the Images tab.</summary>
    private static Button TypeButton(Window window, FullDetailsWindowViewModel vm, int imageTypeId) =>
        window.Descendants<Button>().Single(b =>
            ReferenceEquals(b.Command, vm.ImageEditor.SelectImageTypeCommand) && Equals(b.CommandParameter, imageTypeId));

    private static async Task<string> WriteTempPngAsync()
    {
        var path = Path.Combine(Path.GetTempPath(), $"bookdb_cover_{Guid.NewGuid():N}.png");
        await File.WriteAllBytesAsync(path, OnePixelPng);
        return path;
    }

}
