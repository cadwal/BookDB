using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BookDB.Desktop.Services;
using BookDB.Desktop.ViewModels;
using BookDB.Logic.Services;
using BookDB.Models.Entities;
using BookDB.Models.Interfaces;
using CommunityToolkit.Mvvm.Messaging;
using NSubstitute;
using Xunit;

namespace BookDB.Desktop.Tests.ViewModels;

/// <summary>
/// BookEditViewModelBase category M:M support contracts.
/// Uses FullDetailsWindowViewModel (concrete subclass) and NSubstitute for all services.
/// </summary>
public sealed class BookEditCategoryTests
{
    private static IBookService BuildBookService()
    {
        var svc = Substitute.For<IBookService>();
        svc.GetPeopleAsync(Arg.Any<CancellationToken>())
           .Returns(Task.FromResult<IReadOnlyList<Person>>(System.Array.Empty<Person>()));
        return svc;
    }

    private static FullDetailsWindowViewModel CreateVm(
        IBookService? bookSvc = null,
        ILookupService? lookupSvc = null)
    {
        bookSvc ??= BuildBookService();
        lookupSvc ??= BuildLookupService();
        var bookImgSvc = Substitute.For<IBookImageService>();
        var fileSvc = Substitute.For<IFilePickerService>();
        var winSvc = Substitute.For<IWindowService>();
        var msgr = Substitute.For<IMessenger>();
        var httpFactory = Substitute.For<IHttpClientFactory>();

        var loanSvc = Substitute.For<ILoanService>();
        return new FullDetailsWindowViewModel(bookSvc, bookImgSvc, lookupSvc,
                                              fileSvc, msgr, winSvc, new Helpers.PassThroughWriteGuard(), httpFactory, loanSvc,
                                              Substitute.For<IConnectionHealthMonitor>(), Substitute.For<BookDB.Data.Interfaces.IConnectionFailureClassifier>());
    }

    /// <summary>
    /// Builds an ILookupService stub that returns empty lists for every GetAllAsync<T> call
    /// and GetContributorRolesAsync needed by LoadLookupsAsync.
    /// </summary>
    private static ILookupService BuildLookupService(IReadOnlyList<Category>? categories = null)
    {
        var svc = Substitute.For<ILookupService>();

        svc.GetAllAsync<Format>(Arg.Any<CancellationToken>())
           .Returns(Task.FromResult<IReadOnlyList<Format>>(System.Array.Empty<Format>()));
        svc.GetAllAsync<Publisher>(Arg.Any<CancellationToken>())
           .Returns(Task.FromResult<IReadOnlyList<Publisher>>(System.Array.Empty<Publisher>()));
        svc.GetAllAsync<Series>(Arg.Any<CancellationToken>())
           .Returns(Task.FromResult<IReadOnlyList<Series>>(System.Array.Empty<Series>()));
        svc.GetAllAsync<Language>(Arg.Any<CancellationToken>())
           .Returns(Task.FromResult<IReadOnlyList<Language>>(System.Array.Empty<Language>()));
        svc.GetAllAsync<Edition>(Arg.Any<CancellationToken>())
           .Returns(Task.FromResult<IReadOnlyList<Edition>>(System.Array.Empty<Edition>()));
        svc.GetAllAsync<Rating>(Arg.Any<CancellationToken>())
           .Returns(Task.FromResult<IReadOnlyList<Rating>>(System.Array.Empty<Rating>()));
        svc.GetAllAsync<Condition>(Arg.Any<CancellationToken>())
           .Returns(Task.FromResult<IReadOnlyList<Condition>>(System.Array.Empty<Condition>()));
        svc.GetAllAsync<Status>(Arg.Any<CancellationToken>())
           .Returns(Task.FromResult<IReadOnlyList<Status>>(System.Array.Empty<Status>()));
        svc.GetAllAsync<Location>(Arg.Any<CancellationToken>())
           .Returns(Task.FromResult<IReadOnlyList<Location>>(System.Array.Empty<Location>()));
        svc.GetAllAsync<Owner>(Arg.Any<CancellationToken>())
           .Returns(Task.FromResult<IReadOnlyList<Owner>>(System.Array.Empty<Owner>()));
        svc.GetAllAsync<ReadingLevel>(Arg.Any<CancellationToken>())
           .Returns(Task.FromResult<IReadOnlyList<ReadingLevel>>(System.Array.Empty<ReadingLevel>()));
        svc.GetAllAsync<PurchasePlace>(Arg.Any<CancellationToken>())
           .Returns(Task.FromResult<IReadOnlyList<PurchasePlace>>(System.Array.Empty<PurchasePlace>()));
        svc.GetAllAsync<Category>(Arg.Any<CancellationToken>())
           .Returns(Task.FromResult<IReadOnlyList<Category>>(
               categories ?? System.Array.Empty<Category>()));
        svc.GetAllAsync<Source>(Arg.Any<CancellationToken>())
           .Returns(Task.FromResult<IReadOnlyList<Source>>(System.Array.Empty<Source>()));
        svc.GetContributorRolesAsync(Arg.Any<CancellationToken>())
           .Returns(Task.FromResult<IReadOnlyList<ContributorRole>>(System.Array.Empty<ContributorRole>()));

        return svc;
    }

    // CopyDetailsTabToFields populates CategoryRows from book.Categories, selected items
    // sorted to the top (OrderByDescending(selected).ThenBy(Name)).
    [Fact]
    public async Task CopyDetailsTabToFields_PopulatesCategoryRows_SelectedSortedToTop()
    {
        // Arrange: 3 categories; book has categories 1 and 3 selected.
        var categories = new Category[]
        {
            new() { CategoryId = 1, Name = "Alpha" },
            new() { CategoryId = 2, Name = "Beta" },
            new() { CategoryId = 3, Name = "Gamma" },
        };
        var lookupSvc = BuildLookupService(categories);
        var bookSvc = BuildBookService();

        var book = new Book
        {
            BookId = 42,
            Title = "Test Book",
            Categories =
            [
                new() { BookId = 42, CategoryId = 1 },
                new() { BookId = 42, CategoryId = 3 },
            ]
        };
        bookSvc.GetBookByIdAsync(42, Arg.Any<CancellationToken>()).Returns(book);

        var vm = CreateVm(bookSvc, lookupSvc);
        await vm.LoadBookAsync(42);

        // Assert: CategoryRows has 3 items; selected ones appear first.
        Assert.Equal(3, vm.CategoryRows.Count);

        // The first two rows must be the selected ones (Alpha and Gamma, both selected).
        var selectedRows = vm.CategoryRows.Where(r => r.IsSelected).ToList();
        var unselectedRows = vm.CategoryRows.Where(r => !r.IsSelected).ToList();

        Assert.Equal(2, selectedRows.Count);
        Assert.Contains(selectedRows, r => r.CategoryId == 1);
        Assert.Contains(selectedRows, r => r.CategoryId == 3);
        Assert.Single(unselectedRows);
        Assert.Equal(2, unselectedRows[0].CategoryId); // Beta is the only unselected

        // Selected rows come before unselected rows in the list.
        var firstUnselectedIndex = vm.CategoryRows
            .Select((r, i) => (r, i))
            .First(x => !x.r.IsSelected).i;
        var lastSelectedIndex = vm.CategoryRows
            .Select((r, i) => (r, i))
            .Where(x => x.r.IsSelected)
            .Max(x => x.i);
        Assert.True(lastSelectedIndex < firstUnselectedIndex,
            "All selected categories must appear before unselected ones.");
    }

    // Setting IsSelected=true on any CategoryRows item triggers HasUnsavedChanges=true
    // when not in _loadingInProgress.
    [Fact]
    public async Task CategoryRows_TogglingIsSelected_MarksDirty()
    {
        // Arrange: one category, book has no categories selected.
        var categories = new Category[]
        {
            new() { CategoryId = 1, Name = "Alpha" },
        };
        var lookupSvc = BuildLookupService(categories);
        var bookSvc = BuildBookService();

        var book = new Book
        {
            BookId = 10,
            Title = "Dirty Test Book",
            Categories = [] // none selected
        };
        bookSvc.GetBookByIdAsync(10, Arg.Any<CancellationToken>()).Returns(book);

        var vm = CreateVm(bookSvc, lookupSvc);
        await vm.LoadBookAsync(10);

        // After load, HasUnsavedChanges should be false.
        Assert.False(vm.HasUnsavedChanges, "HasUnsavedChanges must be false immediately after LoadBookAsync.");

        // Act: toggle the first (unselected) category to selected.
        var item = vm.CategoryRows.First();
        Assert.False(item.IsSelected, "Category should start unselected.");
        item.IsSelected = true;

        // Assert: dirty flag set.
        Assert.True(vm.HasUnsavedChanges,
            "Toggling CategorySelectionItem.IsSelected must set HasUnsavedChanges=true.");
    }

    // SaveAsync extracts selected category IDs and calls UpdateBookCategoriesAsync.
    [Fact]
    public async Task SaveAsync_CallsUpdateBookCategoriesAsync_WithSelectedCategoryIds()
    {
        // Arrange: 3 categories; book has category 2 selected initially.
        var categories = new Category[]
        {
            new() { CategoryId = 1, Name = "Alpha" },
            new() { CategoryId = 2, Name = "Beta" },
            new() { CategoryId = 3, Name = "Gamma" },
        };
        var lookupSvc = BuildLookupService(categories);
        var bookSvc = BuildBookService();

        var book = new Book
        {
            BookId = 99,
            Title = "Save Test Book",
            Categories =
            [
                new() { BookId = 99, CategoryId = 2 }
            ]
        };
        bookSvc.GetBookByIdAsync(99, Arg.Any<CancellationToken>()).Returns(book);
        // UpdateBookAsync must succeed (return completed task).
        bookSvc.UpdateBookAsync(Arg.Any<Book>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        bookSvc.UpdateBookContributorsAsync(
            Arg.Any<int>(),
            Arg.Any<IReadOnlyList<(string, int?)>>(),
            Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        bookSvc.UpdateBookCategoriesAsync(
            Arg.Any<int>(),
            Arg.Any<IReadOnlyList<int>>(),
            Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        // GetBookByIdAsync is called again after save to refresh.
        bookSvc.GetBookByIdAsync(99, Arg.Any<CancellationToken>()).Returns(book);

        var vm = CreateVm(bookSvc, lookupSvc);
        await vm.LoadBookAsync(99);

        // Select categories 1 and 3 (deselect 2 by toggling, select 1 and 3).
        var cat1 = vm.CategoryRows.First(r => r.CategoryId == 1);
        var cat2 = vm.CategoryRows.First(r => r.CategoryId == 2);
        var cat3 = vm.CategoryRows.First(r => r.CategoryId == 3);

        cat1.IsSelected = true;  // add
        cat2.IsSelected = false; // remove initial selection
        cat3.IsSelected = true;  // add

        // Act: invoke SaveCommand (via the protected SaveAsync through the relay command).
        await vm.SaveCommand.ExecuteAsync(null);

        // Assert: UpdateBookCategoriesAsync called with IDs {1, 3}.
        await bookSvc.Received(1).UpdateBookCategoriesAsync(
            99,
            Arg.Is<IReadOnlyList<int>>(ids =>
                ids != null && ids.Count == 2 && ids.Contains(1) && ids.Contains(3)),
            Arg.Any<CancellationToken>());
    }
}
