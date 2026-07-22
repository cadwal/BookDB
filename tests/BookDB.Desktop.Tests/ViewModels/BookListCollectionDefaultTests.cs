using System.Collections.Generic;
using System.Linq;
using BookDB.Desktop.ViewModels;
using BookDB.Models;
using BookDB.Models.Entities;
using Xunit;

namespace BookDB.Desktop.Tests.ViewModels;

/// <summary>
/// Precedence for the collection a guided new book files into. The configured default wins over an
/// "all collections" browse state; only an explicit single selection overrides it.
/// </summary>
public class BookListCollectionDefaultTests
{
    private static List<Collection> Collections() =>
        new()
        {
            new Collection { CollectionId = 1, Name = "Comics", SortOrder = 1 },
            new Collection { CollectionId = 2, Name = "Fiction", SortOrder = 2 },
            new Collection { CollectionId = 3, Name = "Non-Fiction", SortOrder = 3 },
        };

    [Fact]
    public void SingleSelectedCollection_Wins_EvenOverConfiguredDefault()
    {
        var result = BookListViewModel.ResolveNewBookCollectionId(
            Collections(), new HashSet<int> { 1 }, configuredDefaultId: 2);

        Assert.Equal(1, result);
    }

    [Fact]
    public void AllSelected_UsesConfiguredDefault_NotTheFirstCollection()
    {
        var result = BookListViewModel.ResolveNewBookCollectionId(
            Collections(), new HashSet<int> { 1, 2, 3, CollectionFilter.Uncategorized }, configuredDefaultId: 2);

        Assert.Equal(2, result);
    }

    [Fact]
    public void AllSelected_NoDefault_FallsBackToFirstSelectedInDisplayOrder()
    {
        var result = BookListViewModel.ResolveNewBookCollectionId(
            Collections(), new HashSet<int> { 1, 2, 3 }, configuredDefaultId: null);

        Assert.Equal(1, result);
    }

    [Fact]
    public void ConfiguredDefaultNoLongerExists_FallsBackToFirstSelected()
    {
        var result = BookListViewModel.ResolveNewBookCollectionId(
            Collections(), new HashSet<int> { 2, 3 }, configuredDefaultId: 99);

        Assert.Equal(2, result);
    }

    [Fact]
    public void SingleRealPlusUncategorizedSelected_CountsAsSingleSelection()
    {
        var result = BookListViewModel.ResolveNewBookCollectionId(
            Collections(), new HashSet<int> { 3, CollectionFilter.Uncategorized }, configuredDefaultId: 2);

        Assert.Equal(3, result);
    }

    [Fact]
    public void OnlyUncategorizedSelected_WithDefault_UsesDefault()
    {
        var result = BookListViewModel.ResolveNewBookCollectionId(
            Collections(), new HashSet<int> { CollectionFilter.Uncategorized }, configuredDefaultId: 2);

        Assert.Equal(2, result);
    }

    [Fact]
    public void NothingSelected_NoDefault_FallsBackToFirstCollection()
    {
        var result = BookListViewModel.ResolveNewBookCollectionId(
            Collections(), new HashSet<int>(), configuredDefaultId: null);

        Assert.Equal(1, result);
    }
}
