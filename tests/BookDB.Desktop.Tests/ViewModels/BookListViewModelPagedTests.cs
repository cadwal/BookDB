using System;
using System.Threading.Tasks;
using BookDB.Data.DbContexts;
using BookDB.Desktop.Tests.Helpers;
using BookDB.Desktop.ViewModels;
using BookDB.Models.Entities;
using CommunityToolkit.Mvvm.Messaging;
using Xunit;

namespace BookDB.Desktop.Tests.ViewModels;

/// <summary>
/// Behavioral tests for BookListViewModel paged loading via GetBooksAsync (PageSize=100).
/// LoadBooksAsync populates Books from page 0; IsAllLoaded reflects whether all rows are fetched.
/// </summary>
public sealed class BookListViewModelPagedTests : IDisposable
{
    private readonly TestLookupServiceFactory _factory;
    private readonly BookListViewModel _bookList;

    public BookListViewModelPagedTests()
    {
        _factory = new TestLookupServiceFactory();
        var messenger = new WeakReferenceMessenger();
        var settingsService = new TestLookupServiceFactory.NullSettingsService();
        _bookList = new BookListViewModel(messenger, _factory.BookService, _factory.BookSearchService, _factory.BookImageService, new TestLookupServiceFactory.NullWindowService(), settingsService, _factory.LookupService, new TestLookupServiceFactory.NullClipboardService(), NSubstitute.Substitute.For<BookDB.Logic.Services.ILoanService>(), NSubstitute.Substitute.For<BookDB.Logic.Services.IConnectionHealthMonitor>(), NSubstitute.Substitute.For<BookDB.Data.Interfaces.IConnectionFailureClassifier>());
    }

    public void Dispose() => _factory.Dispose();

    private async Task SeedBooksAsync(int count)
    {
        await using var db = _factory.DbContextFactory.CreateDbContext();
        for (int i = 1; i <= count; i++)
        {
            db.Books.Add(new Book { Title = $"Book {i:D4}" });
        }
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task LoadBooksAsync_FirstPage_PopulatesBooks()
    {
        // Arrange — 150 books, page size is 100
        await SeedBooksAsync(150);

        // Act
        await _bookList.LoadBooksAsync(TestContext.Current.CancellationToken);

        // Assert — only first 100 rows fetched, total is 150, not all loaded
        Assert.Equal(100, _bookList.Books.Count);
        Assert.Equal(150, _bookList.FilteredTotal);
        Assert.False(_bookList.IsAllLoaded);
    }

    [Fact]
    public async Task LoadBooksAsync_SmallSet_SetsIsAllLoaded()
    {
        // Arrange — 10 books, well under page size
        await SeedBooksAsync(10);

        // Act
        await _bookList.LoadBooksAsync(TestContext.Current.CancellationToken);

        // Assert — all books returned, IsAllLoaded set
        Assert.Equal(10, _bookList.Books.Count);
        Assert.True(_bookList.IsAllLoaded);
    }

    [Fact]
    public async Task LoadBooksAsync_ClearsExistingBooks()
    {
        // Arrange — seed 5, load once
        await SeedBooksAsync(5);
        await _bookList.LoadBooksAsync(TestContext.Current.CancellationToken);
        Assert.Equal(5, _bookList.Books.Count);

        // Seed 3 more (now 8 total), reload
        await SeedBooksAsync(3);
        await _bookList.LoadBooksAsync(TestContext.Current.CancellationToken);

        // Assert — old rows cleared; count reflects current total (8), not 5+8=13
        Assert.Equal(8, _bookList.Books.Count);
    }

    [Fact]
    public async Task LoadMoreCommand_WhenAllLoaded_DoesNotLoadMore()
    {
        // Arrange — 10 books; after LoadBooksAsync IsAllLoaded == true
        await SeedBooksAsync(10);
        await _bookList.LoadBooksAsync(TestContext.Current.CancellationToken);
        Assert.True(_bookList.IsAllLoaded);

        var countBefore = _bookList.Books.Count;

        // Act — execute LoadMoreCommand; guard should fire immediately
        _bookList.LoadMoreCommand.Execute(null);
        // Allow any synchronous processing to complete
        await Task.Delay(50, TestContext.Current.CancellationToken);

        // Assert — no additional rows added
        Assert.Equal(countBefore, _bookList.Books.Count);
    }
}
