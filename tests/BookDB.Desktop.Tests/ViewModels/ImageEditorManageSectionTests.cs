using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BookDB.Desktop.Localization;
using BookDB.Desktop.Services;
using BookDB.Desktop.ViewModels;
using BookDB.Logic.Services;
using BookDB.Models.Entities;
using BookDB.Models.Interfaces;
using NSubstitute;
using Xunit;

namespace BookDB.Desktop.Tests.ViewModels;

/// <summary>
/// Unit tests for ImageEditorViewModel manage section:
/// ImageItems, HasMultipleImages, MoveUp/Down, FlushPendingAsync, ResetToSaved.
/// </summary>
public sealed class ImageEditorManageSectionTests
{
    private static IReadOnlyList<BookImage> CreateImageList(params (int typeId, int order)[] specs)
    {
        var typeNames = new Dictionary<int, string>
        {
            [0] = "Front Cover",
            [1] = "Thumbnail",
            [2] = "Back Cover",
            [3] = "Spine",
            [4] = "Dust Jacket",
        };

        var list = new List<BookImage>();
        int idCounter = 1;
        foreach (var (typeId, order) in specs)
        {
            list.Add(new BookImage
            {
                BookImageId = idCounter++,
                BookId = 42,
                BookImageTypeId = typeId,
                DisplayOrder = order,
                BookImageType = new BookImageType
                {
                    BookImageTypeId = typeId,
                    TypeName = typeNames.GetValueOrDefault(typeId, $"Type{typeId}"),
                },
            });
        }
        return list;
    }

    private static ImageEditorViewModel CreateVm(IBookImageService? bookSvc = null)
    {
        bookSvc ??= Substitute.For<IBookImageService>();
        var fileSvc = Substitute.For<IFilePickerService>();
        var httpFactory = Substitute.For<IHttpClientFactory>();
        return new ImageEditorViewModel(bookSvc, fileSvc, httpFactory);
    }

    private static Book MakeBook(int bookId = 42) => new Book { BookId = bookId };

    // ---------------------------------------------------------------------------
    // HasMultipleImages
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task HasMultipleImages_ReturnsFalse_WhenNoImagesLoaded()
    {
        var bookSvc = Substitute.For<IBookImageService>();
        bookSvc.GetBookImagesAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
               .Returns(Task.FromResult<IReadOnlyList<BookImage>>(new List<BookImage>()));
        bookSvc.GetBookImageBytesAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
               .Returns(Task.FromResult<byte[]?>(null));

        var vm = CreateVm(bookSvc);
        await vm.LoadForBookAsync(MakeBook());

        Assert.False(vm.HasMultipleImages);
    }

    [Fact]
    public async Task HasMultipleImages_ReturnsFalse_WhenExactlyOneImage()
    {
        var bookSvc = Substitute.For<IBookImageService>();
        bookSvc.GetBookImagesAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
               .Returns(Task.FromResult(CreateImageList((0, 1))));
        bookSvc.GetBookImageBytesAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
               .Returns(Task.FromResult<byte[]?>(null));

        var vm = CreateVm(bookSvc);
        await vm.LoadForBookAsync(MakeBook());

        Assert.False(vm.HasMultipleImages);
    }

    [Fact]
    public async Task HasMultipleImages_ReturnsTrue_WhenSameTypeDuplicatesExist()
    {
        var bookSvc = Substitute.For<IBookImageService>();
        bookSvc.GetBookImagesAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
               .Returns(Task.FromResult(CreateImageList((0, 1), (0, 2)))); // Two Front Cover images
        bookSvc.GetBookImageBytesAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
               .Returns(Task.FromResult<byte[]?>(null));

        var vm = CreateVm(bookSvc);
        await vm.LoadForBookAsync(MakeBook());

        Assert.True(vm.HasMultipleImages);
    }

    [Fact]
    public async Task HasMultipleImages_ReturnsFalse_WhenImagesAllHaveDifferentTypes()
    {
        var bookSvc = Substitute.For<IBookImageService>();
        bookSvc.GetBookImagesAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
               .Returns(Task.FromResult(CreateImageList((0, 1), (2, 1)))); // Front Cover + Back Cover
        bookSvc.GetBookImageBytesAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
               .Returns(Task.FromResult<byte[]?>(null));

        var vm = CreateVm(bookSvc);
        await vm.LoadForBookAsync(MakeBook());

        Assert.False(vm.HasMultipleImages);
    }

    // ---------------------------------------------------------------------------
    // IsThumbnailType
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task RebuildImageItems_SetsThumbnailTypeFlag_ForTypeId1()
    {
        var bookSvc = Substitute.For<IBookImageService>();
        bookSvc.GetBookImagesAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
               .Returns(Task.FromResult(CreateImageList((1, 1), (0, 1)))); // Thumbnail + Front Cover
        bookSvc.GetBookImageBytesAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
               .Returns(Task.FromResult<byte[]?>(null));

        var vm = CreateVm(bookSvc);
        await vm.LoadForBookAsync(MakeBook());

        var thumbnailItem = vm.ImageItems.Single(i => i.OriginalTypeId == 1);
        var coverItem = vm.ImageItems.Single(i => i.OriginalTypeId == 0);
        Assert.True(thumbnailItem.IsThumbnailType);
        Assert.False(coverItem.IsThumbnailType);
    }

    // ---------------------------------------------------------------------------
    // ImageItems built correctly
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task ImageItems_BuiltSortedByTypeIdThenDisplayOrder()
    {
        var bookSvc = Substitute.For<IBookImageService>();
        // Deliberately out of order: typeId=2 order=1, typeId=0 order=2, typeId=0 order=1
        bookSvc.GetBookImagesAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
               .Returns(Task.FromResult(CreateImageList((2, 1), (0, 2), (0, 1))));
        bookSvc.GetBookImageBytesAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
               .Returns(Task.FromResult<byte[]?>(null));

        var vm = CreateVm(bookSvc);
        await vm.LoadForBookAsync(MakeBook());

        Assert.Equal(3, vm.ImageItems.Count);
        // First two items: typeId=0, ordered by DisplayOrder ascending
        Assert.Equal(0, vm.ImageItems[0].SelectedTypeId);
        Assert.Equal(1, vm.ImageItems[0].DisplayOrder);
        Assert.Equal(0, vm.ImageItems[1].SelectedTypeId);
        Assert.Equal(2, vm.ImageItems[1].DisplayOrder);
        // Third item: typeId=2
        Assert.Equal(2, vm.ImageItems[2].SelectedTypeId);
    }

    // ---------------------------------------------------------------------------
    // MoveUpCommand
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task MoveUp_SwapsDisplayOrder_WithPreviousItemInSameTypeGroup()
    {
        var bookSvc = Substitute.For<IBookImageService>();
        bookSvc.GetBookImagesAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
               .Returns(Task.FromResult(CreateImageList((0, 1), (0, 2))));
        bookSvc.GetBookImageBytesAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
               .Returns(Task.FromResult<byte[]?>(null));

        var vm = CreateVm(bookSvc);
        await vm.LoadForBookAsync(MakeBook());

        var first = vm.ImageItems[0];  // DisplayOrder=1, CanMoveUp=false
        var second = vm.ImageItems[1]; // DisplayOrder=2, CanMoveUp=true

        vm.MoveUpCommand.Execute(second);

        Assert.Equal(1, second.DisplayOrder);
        Assert.Equal(2, first.DisplayOrder);
        Assert.True(second.IsDirty);
        Assert.True(first.IsDirty);
    }

    [Fact]
    public async Task MoveUp_DoesNothing_WhenCanMoveUpIsFalse()
    {
        var bookSvc = Substitute.For<IBookImageService>();
        bookSvc.GetBookImagesAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
               .Returns(Task.FromResult(CreateImageList((0, 1), (0, 2))));
        bookSvc.GetBookImageBytesAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
               .Returns(Task.FromResult<byte[]?>(null));

        var vm = CreateVm(bookSvc);
        await vm.LoadForBookAsync(MakeBook());

        var first = vm.ImageItems[0]; // DisplayOrder=1, CanMoveUp=false

        vm.MoveUpCommand.Execute(first);

        // Nothing swapped — first still has DisplayOrder=1
        Assert.Equal(1, first.DisplayOrder);
        Assert.False(first.IsDirty);
    }

    // ---------------------------------------------------------------------------
    // MoveDownCommand
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task MoveDown_DoesNothing_WhenCanMoveDownIsFalse()
    {
        var bookSvc = Substitute.For<IBookImageService>();
        bookSvc.GetBookImagesAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
               .Returns(Task.FromResult(CreateImageList((0, 1), (0, 2))));
        bookSvc.GetBookImageBytesAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
               .Returns(Task.FromResult<byte[]?>(null));

        var vm = CreateVm(bookSvc);
        await vm.LoadForBookAsync(MakeBook());

        var last = vm.ImageItems[1]; // DisplayOrder=2, CanMoveDown=false

        vm.MoveDownCommand.Execute(last);

        Assert.Equal(2, last.DisplayOrder);
        Assert.False(last.IsDirty);
    }

    // ---------------------------------------------------------------------------
    // FlushPendingAsync — type-conflict guard
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task FlushPendingAsync_ThrowsInvalidOperationException_WhenDuplicateSelectedTypeId()
    {
        var bookSvc = Substitute.For<IBookImageService>();
        bookSvc.GetBookImagesAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
               .Returns(Task.FromResult(CreateImageList((0, 1), (2, 1))));
        bookSvc.GetBookImageBytesAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
               .Returns(Task.FromResult<byte[]?>(null));

        var vm = CreateVm(bookSvc);
        await vm.LoadForBookAsync(MakeBook());

        // Assign both items to same type — creates conflict
        vm.ImageItems[0].SelectedTypeId = 5;
        vm.ImageItems[1].SelectedTypeId = 5;

        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.FlushPendingAsync(42));
    }

    // ---------------------------------------------------------------------------
    // FlushPendingAsync — writes staged changes
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task FlushPendingAsync_CallsReorderBookImageAsync_ForDirtyItems()
    {
        var bookSvc = Substitute.For<IBookImageService>();
        var images = CreateImageList((0, 1), (0, 2));
        bookSvc.GetBookImagesAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
               .Returns(Task.FromResult(images));
        bookSvc.GetBookImageBytesAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
               .Returns(Task.FromResult<byte[]?>(null));

        var vm = CreateVm(bookSvc);
        await vm.LoadForBookAsync(MakeBook());

        // Move second item up (swap DisplayOrder)
        vm.MoveUpCommand.Execute(vm.ImageItems[1]);

        await vm.FlushPendingAsync(42);

        // Both items involved in the swap should be flushed
        await bookSvc.Received().ReorderBookImageAsync(42, Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FlushPendingAsync_CallsReassignBookImageTypeAsync_ForItemsWithChangedType()
    {
        var bookSvc = Substitute.For<IBookImageService>();
        var images = CreateImageList((0, 1), (2, 1));
        bookSvc.GetBookImagesAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
               .Returns(Task.FromResult(images));
        bookSvc.GetBookImageBytesAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
               .Returns(Task.FromResult<byte[]?>(null));

        var vm = CreateVm(bookSvc);
        await vm.LoadForBookAsync(MakeBook());

        // Change the type of the second item (from type 2 to type 3)
        vm.ImageItems[1].SelectedTypeId = 3;
        vm.ImageItems[1].IsDirty = true;

        await vm.FlushPendingAsync(42);

        await bookSvc.Received().ReassignBookImageTypeAsync(42, vm.ImageItems[1].BookImageId, 3, Arg.Any<CancellationToken>());
    }

    // ---------------------------------------------------------------------------
    // ResetToSaved
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task ResetToSaved_RebuildsImageItemsFromSnapshot()
    {
        var bookSvc = Substitute.For<IBookImageService>();
        bookSvc.GetBookImagesAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
               .Returns(Task.FromResult(CreateImageList((0, 1), (0, 2))));
        bookSvc.GetBookImageBytesAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
               .Returns(Task.FromResult<byte[]?>(null));

        var vm = CreateVm(bookSvc);
        await vm.LoadForBookAsync(MakeBook());

        // Mutate a staged item
        vm.ImageItems[0].SelectedTypeId = 99;
        vm.ImageItems[0].IsDirty = true;

        await vm.ResetToSaved();

        // All items should be fresh (IsDirty=false, original typeId restored)
        Assert.All(vm.ImageItems, item => Assert.False(item.IsDirty));
        Assert.Equal(0, vm.ImageItems[0].SelectedTypeId);
    }

    // ---------------------------------------------------------------------------
    // AvailableImageTypes
    // ---------------------------------------------------------------------------

    [Fact]
    public void AvailableImageTypes_ReturnsFiveTypes()
    {
        var vm = CreateVm();

        // Assert against the resource values (not hard-coded English) so the test is
        // culture-independent — it verifies each type ID maps to the correct localized name.
        Assert.Equal(5, vm.AvailableImageTypes.Count);
        Assert.Contains(vm.AvailableImageTypes, t => t.BookImageTypeId == 0 && t.Name == Resources.BookImageType_Cover);
        Assert.Contains(vm.AvailableImageTypes, t => t.BookImageTypeId == 1 && t.Name == Resources.BookImageType_Thumbnail);
        Assert.Contains(vm.AvailableImageTypes, t => t.BookImageTypeId == 2 && t.Name == Resources.BookImageType_BackCover);
        Assert.Contains(vm.AvailableImageTypes, t => t.BookImageTypeId == 3 && t.Name == Resources.BookImageType_Spine);
        Assert.Contains(vm.AvailableImageTypes, t => t.BookImageTypeId == 4 && t.Name == Resources.BookImageType_DustJacket);
    }

    // ---------------------------------------------------------------------------
    // Gap F — OnSelectedTypeIdChanged dirty tracking
    // ---------------------------------------------------------------------------

    /// <summary>Test A: Initial construction must NOT set IsDirty (OriginalTypeId is assigned before SelectedTypeId).</summary>
    [Fact]
    public async Task SelectedTypeId_InitialConstruction_DoesNotSetIsDirty()
    {
        var bookSvc = Substitute.For<IBookImageService>();
        bookSvc.GetBookImagesAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
               .Returns(Task.FromResult(CreateImageList((0, 1))));
        bookSvc.GetBookImageBytesAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
               .Returns(Task.FromResult<byte[]?>(null));

        var vm = CreateVm(bookSvc);
        await vm.LoadForBookAsync(MakeBook());

        // After construction: OriginalTypeId=0, SelectedTypeId=0 — IsDirty must be false
        Assert.False(vm.ImageItems[0].IsDirty);
    }

    /// <summary>Test B: Setting SelectedTypeId to a different value marks IsDirty=true.</summary>
    [Fact]
    public async Task SelectedTypeId_ChangedToDifferentValue_SetsIsDirty()
    {
        var bookSvc = Substitute.For<IBookImageService>();
        bookSvc.GetBookImagesAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
               .Returns(Task.FromResult(CreateImageList((0, 1))));
        bookSvc.GetBookImageBytesAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
               .Returns(Task.FromResult<byte[]?>(null));

        var vm = CreateVm(bookSvc);
        await vm.LoadForBookAsync(MakeBook());

        vm.ImageItems[0].SelectedTypeId = 2;

        Assert.True(vm.ImageItems[0].IsDirty);
    }

    /// <summary>Test C: Setting SelectedTypeId back to OriginalTypeId keeps IsDirty=true (once dirty, stays dirty).</summary>
    [Fact]
    public async Task SelectedTypeId_RevertedToOriginal_RemainsIsDirty()
    {
        var bookSvc = Substitute.For<IBookImageService>();
        bookSvc.GetBookImagesAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
               .Returns(Task.FromResult(CreateImageList((0, 1))));
        bookSvc.GetBookImageBytesAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
               .Returns(Task.FromResult<byte[]?>(null));

        var vm = CreateVm(bookSvc);
        await vm.LoadForBookAsync(MakeBook());

        vm.ImageItems[0].SelectedTypeId = 2; // make dirty
        vm.ImageItems[0].SelectedTypeId = 0; // revert to original

        // IsDirty must remain true — once dirty, stays dirty until FlushPendingAsync clears it
        Assert.True(vm.ImageItems[0].IsDirty);
    }

    /// <summary>Test D: Changing SelectedTypeId via ViewModel and calling FlushPendingAsync calls ReassignBookImageTypeAsync automatically.</summary>
    [Fact]
    public async Task FlushPendingAsync_CallsReassignBookImageTypeAsync_WhenSelectedTypeIdChanged_ViaDirtyTracking()
    {
        var bookSvc = Substitute.For<IBookImageService>();
        bookSvc.GetBookImagesAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
               .Returns(Task.FromResult(CreateImageList((0, 1), (0, 2))));
        bookSvc.GetBookImageBytesAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
               .Returns(Task.FromResult<byte[]?>(null));

        var vm = CreateVm(bookSvc);
        await vm.LoadForBookAsync(MakeBook());

        // Change SelectedTypeId — OnSelectedTypeIdChanged must set IsDirty automatically
        vm.ImageItems[0].SelectedTypeId = 2;

        await vm.FlushPendingAsync(42);

        await bookSvc.Received(1).ReassignBookImageTypeAsync(
            42, Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    // ---------------------------------------------------------------------------
    // Gap E — RemoveManageItemCommand + _pendingDeleteImageIds
    // ---------------------------------------------------------------------------

    /// <summary>Test A: After RemoveManageItem, item is no longer in ImageItems.</summary>
    [Fact]
    public async Task RemoveManageItem_RemovesItemFromImageItems()
    {
        var bookSvc = Substitute.For<IBookImageService>();
        bookSvc.GetBookImagesAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
               .Returns(Task.FromResult(CreateImageList((0, 1), (0, 2))));
        bookSvc.GetBookImageBytesAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
               .Returns(Task.FromResult<byte[]?>(null));

        var vm = CreateVm(bookSvc);
        await vm.LoadForBookAsync(MakeBook());

        var itemToRemove = vm.ImageItems[0];
        vm.RemoveManageItemCommand.Execute(itemToRemove);

        Assert.DoesNotContain(itemToRemove, vm.ImageItems);
        Assert.Single(vm.ImageItems);
    }

    /// <summary>Test B: After RemoveManageItem, FlushPendingAsync calls RemoveBookImageByIdAsync with the removed item's BookImageId.</summary>
    [Fact]
    public async Task RemoveManageItem_ThenFlush_CallsRemoveBookImageByIdAsync()
    {
        var bookSvc = Substitute.For<IBookImageService>();
        bookSvc.GetBookImagesAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
               .Returns(Task.FromResult(CreateImageList((0, 1), (0, 2))));
        bookSvc.GetBookImageBytesAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
               .Returns(Task.FromResult<byte[]?>(null));
        bookSvc.RemoveBookImageByIdAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
               .Returns(Task.CompletedTask);

        var vm = CreateVm(bookSvc);
        await vm.LoadForBookAsync(MakeBook());

        var itemToRemove = vm.ImageItems[0];
        int expectedImageId = itemToRemove.BookImageId;
        vm.RemoveManageItemCommand.Execute(itemToRemove);

        await vm.FlushPendingAsync(42);

        await bookSvc.Received(1).RemoveBookImageByIdAsync(42, expectedImageId, Arg.Any<CancellationToken>());
    }

    /// <summary>Test C: After RemoveManageItem + FlushPendingAsync, a second FlushPendingAsync does NOT call RemoveBookImageByIdAsync again.</summary>
    [Fact]
    public async Task RemoveManageItem_AfterFlush_SecondFlushDoesNotCallRemoveAgain()
    {
        var bookSvc = Substitute.For<IBookImageService>();
        bookSvc.GetBookImagesAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
               .Returns(Task.FromResult(CreateImageList((0, 1), (0, 2))));
        bookSvc.GetBookImageBytesAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
               .Returns(Task.FromResult<byte[]?>(null));
        bookSvc.RemoveBookImageByIdAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
               .Returns(Task.CompletedTask);

        var vm = CreateVm(bookSvc);
        await vm.LoadForBookAsync(MakeBook());

        vm.RemoveManageItemCommand.Execute(vm.ImageItems[0]);
        await vm.FlushPendingAsync(42); // first flush — clears _pendingDeleteImageIds

        await vm.FlushPendingAsync(42); // second flush — must NOT call RemoveBookImageByIdAsync

        await bookSvc.Received(1).RemoveBookImageByIdAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    /// <summary>Test D: After RemoveManageItem then ResetToSaved, ImageItems is restored and second FlushPendingAsync does NOT call RemoveBookImageByIdAsync.</summary>
    [Fact]
    public async Task RemoveManageItem_ThenResetToSaved_RestoresItemsAndClearsPendingDelete()
    {
        var bookSvc = Substitute.For<IBookImageService>();
        bookSvc.GetBookImagesAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
               .Returns(Task.FromResult(CreateImageList((0, 1), (0, 2))));
        bookSvc.GetBookImageBytesAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
               .Returns(Task.FromResult<byte[]?>(null));
        bookSvc.RemoveBookImageByIdAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
               .Returns(Task.CompletedTask);

        var vm = CreateVm(bookSvc);
        await vm.LoadForBookAsync(MakeBook());

        Assert.Equal(2, vm.ImageItems.Count);

        vm.RemoveManageItemCommand.Execute(vm.ImageItems[0]);
        Assert.Single(vm.ImageItems);

        await vm.ResetToSaved(); // Cancel — should restore items and clear pending-delete list

        Assert.Equal(2, vm.ImageItems.Count); // restored

        await vm.FlushPendingAsync(42); // should NOT call RemoveBookImageByIdAsync since list was cleared

        await bookSvc.DidNotReceive().RemoveBookImageByIdAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    // ---------------------------------------------------------------------------
    // Manage-list edits re-derive the per-type preview shown at the top of the view
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Removing the representative (lowest DisplayOrder) image of a type promotes the next image of
    /// that type to the top-of-view preview. CoverBitmapSizeBytes reads the per-type map, so it must
    /// reflect the new representative after the manage-list edit.
    /// </summary>
    [Fact]
    public async Task RemoveManageItem_PromotesNextImageToTopPreview()
    {
        var bookSvc = Substitute.For<IBookImageService>();
        // Two Front Cover images with distinct byte lengths so we can tell which one drives the preview.
        var images = new List<BookImage>
        {
            new() { BookImageId = 1, BookId = 42, BookImageTypeId = 0, DisplayOrder = 1, ImageData = new byte[4] },
            new() { BookImageId = 2, BookId = 42, BookImageTypeId = 0, DisplayOrder = 2, ImageData = new byte[8] },
        };
        bookSvc.GetBookImagesAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
               .Returns(Task.FromResult<IReadOnlyList<BookImage>>(images));
        bookSvc.GetBookImageBytesAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
               .Returns(Task.FromResult<byte[]?>(null));

        var vm = CreateVm(bookSvc);
        await vm.LoadForBookAsync(MakeBook());

        // Selected type defaults to Front Cover; representative is the order-1 image (4 bytes).
        Assert.Equal(4, vm.CoverBitmapSizeBytes);

        // Remove the representative — the order-2 image (8 bytes) should become the top preview.
        await vm.RemoveManageItemCommand.ExecuteAsync(vm.ImageItems[0]);

        Assert.Equal(8, vm.CoverBitmapSizeBytes);
    }

    /// <summary>
    /// Reassigning a manage item to a different type updates the per-type preview map for the new type.
    /// </summary>
    [Fact]
    public async Task ReassignType_UpdatesTopPreviewForNewType()
    {
        var bookSvc = Substitute.For<IBookImageService>();
        var images = new List<BookImage>
        {
            new() { BookImageId = 1, BookId = 42, BookImageTypeId = 2 /* BackCover */, DisplayOrder = 1, ImageData = new byte[6] },
        };
        bookSvc.GetBookImagesAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
               .Returns(Task.FromResult<IReadOnlyList<BookImage>>(images));
        bookSvc.GetBookImageBytesAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
               .Returns(Task.FromResult<byte[]?>(null));

        var vm = CreateVm(bookSvc);
        await vm.LoadForBookAsync(MakeBook());

        // Selected type is Front Cover (0), which has no image yet.
        Assert.Null(vm.CoverBitmapSizeBytes);

        // Reassign the only image to Front Cover via the type picker binding; the per-type map is
        // rebuilt synchronously (before the bitmap load awaits), so the top preview for type 0 resolves.
        vm.ImageItems[0].SelectedTypeId = 0;

        Assert.Equal(6, vm.CoverBitmapSizeBytes);
    }

    /// <summary>Test E: HasMultipleImages is re-evaluated after RemoveManageItem removes an item — if same-type count drops to 1, becomes false.</summary>
    [Fact]
    public async Task RemoveManageItem_UpdatesHasMultipleImages()
    {
        var bookSvc = Substitute.For<IBookImageService>();
        bookSvc.GetBookImagesAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
               .Returns(Task.FromResult(CreateImageList((0, 1), (0, 2)))); // Two Front Cover — HasMultipleImages=true
        bookSvc.GetBookImageBytesAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
               .Returns(Task.FromResult<byte[]?>(null));

        var vm = CreateVm(bookSvc);
        await vm.LoadForBookAsync(MakeBook());

        Assert.True(vm.HasMultipleImages); // precondition

        vm.RemoveManageItemCommand.Execute(vm.ImageItems[0]); // remove one of the two duplicates

        Assert.False(vm.HasMultipleImages); // now only 1 Front Cover — no duplicates
    }
}
