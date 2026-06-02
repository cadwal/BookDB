using BookDB.Desktop.ViewModels;
using BookDB.Logic.Services;
using Xunit;

namespace BookDB.Desktop.Tests.ViewModels;

public sealed class BookRowViewModelBadgeTests
{
    [Fact]
    public void HasMultipleImages_True_WhenHasDuplicateImageTypes()
    {
        var vm = new BookRowViewModel { HasDuplicateImageTypes = true };
        Assert.True(vm.HasMultipleImages);
    }

    [Fact]
    public void HasMultipleImages_False_WhenNoDuplicateImageTypes()
    {
        var vm = new BookRowViewModel { HasDuplicateImageTypes = false };
        Assert.False(vm.HasMultipleImages);
    }

    [Fact]
    public void ImageCountBadge_IsExclamationMark()
    {
        var vm = new BookRowViewModel { HasDuplicateImageTypes = true };
        Assert.Equal("!", vm.ImageCountBadge);
    }

    [Fact]
    public void FromListRow_MapsDuplicateImageTypes_True()
    {
        var row = CreateMinimalRow(hasDuplicateImageTypes: true);
        var vm = BookRowViewModel.FromListRow(row);
        Assert.True(vm.HasDuplicateImageTypes);
        Assert.True(vm.HasMultipleImages);
    }

    [Fact]
    public void FromListRow_MapsDuplicateImageTypes_False()
    {
        var row = CreateMinimalRow(hasDuplicateImageTypes: false);
        var vm = BookRowViewModel.FromListRow(row);
        Assert.False(vm.HasDuplicateImageTypes);
        Assert.False(vm.HasMultipleImages);
    }

    private static BookService.BookListRow CreateMinimalRow(bool hasDuplicateImageTypes) =>
        new(1, "Title", null, null, null, null, null,
            false, null, null, null, null, null, null, null, null,
            [], [], null, null, null, null, hasDuplicateImageTypes,
            false, false, null);
}
