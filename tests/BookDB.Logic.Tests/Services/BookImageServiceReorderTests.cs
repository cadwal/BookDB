using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using BookDB.Data;
using BookDB.Data.DbContexts;
using BookDB.Logic.Services;
using BookDB.Models.Entities;
using DbUp;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BookDB.Logic.Tests.Services;

/// <summary>
/// Integration tests for BookImageService.ReorderBookImageAsync and ReassignBookImageTypeAsync.
/// Uses a temp-file SQLite database (FTS5 requires a real file — consistent with existing fixture pattern).
/// </summary>
public sealed class BookImageServiceReorderTests : IDisposable
{
    private readonly string _dbPath;
    private readonly string _connectionString;
    private readonly TestBookDbContextFactory _factory;
    private readonly BookImageService _sut;

    public BookImageServiceReorderTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"bookdb_reorder_test_{Guid.NewGuid():N}.db");
        _connectionString = $"Data Source={_dbPath}";

        var upgrader = SqliteExtensions.SqliteDatabase(DeployChanges.To, _connectionString)
            .WithScriptsEmbeddedInAssembly(
                Assembly.GetAssembly(typeof(BookDbContext))!,
                name => name.Contains(".Migrations."))
            .LogToNowhere()
            .Build();

        var result = upgrader.PerformUpgrade();
        if (!result.Successful)
            throw new InvalidOperationException($"DbUp migration failed: {result.Error}");

        var options = new DbContextOptionsBuilder<BookDbContext>()
            .UseSqlite(_connectionString)
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
            .Options;

        _factory = new TestBookDbContextFactory(options);
        _sut = new BookImageService(_factory);
    }

    public void Dispose()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { File.Delete(_dbPath); } catch { /* best effort */ }
    }

    private async Task<int> SeedBookAsync()
    {
        await using var dbContext = _factory.CreateDbContext();
        var book = new Book { Title = "Reorder Test Book" };
        dbContext.Books.Add(book);
        await dbContext.SaveChangesAsync();
        return book.BookId;
    }

    private async Task<int> SeedImageAsync(int bookId, int bookImageTypeId, int displayOrder)
    {
        await using var dbContext = _factory.CreateDbContext();
        var image = new BookImage
        {
            BookId = bookId,
            ImageData = new byte[] { 0xFF, 0xD8, 0xFF },
            MimeType = "image/jpeg",
            IsPrimary = bookImageTypeId == 0,
            DisplayOrder = displayOrder,
            BookImageTypeId = bookImageTypeId,
            Added = DateTime.UtcNow
        };
        dbContext.BookImages.Add(image);
        await dbContext.SaveChangesAsync();
        return image.BookImageId;
    }

    // ---------------------------------------------------------------------------
    // ReorderBookImageAsync
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task ReorderBookImageAsync_UpdatesDisplayOrderForMatchingRow()
    {
        // Arrange
        int bookId = await SeedBookAsync();
        int imageId = await SeedImageAsync(bookId, bookImageTypeId: 0, displayOrder: 5);

        // Act
        await _sut.ReorderBookImageAsync(bookId, imageId, newDisplayOrder: 2, TestContext.Current.CancellationToken);

        // Assert
        await using var dbContext = _factory.CreateDbContext();
        var image = await dbContext.BookImages.FindAsync([imageId], TestContext.Current.CancellationToken);
        Assert.NotNull(image);
        Assert.Equal(2, image.DisplayOrder);
    }

    [Fact]
    public async Task ReorderBookImageAsync_WhenImageIdNotOwnedByBook_UpdatesNoRows()
    {
        // Arrange — imageId belongs to book2, not book1
        int book1Id = await SeedBookAsync();
        int book2Id = await SeedBookAsync();
        int imageId = await SeedImageAsync(book2Id, bookImageTypeId: 0, displayOrder: 5);
        int originalDisplayOrder = 5;

        // Act — pass book1Id but image belongs to book2
        await _sut.ReorderBookImageAsync(book1Id, imageId, newDisplayOrder: 99, TestContext.Current.CancellationToken);

        // Assert — DisplayOrder on book2's image unchanged
        await using var dbContext = _factory.CreateDbContext();
        var image = await dbContext.BookImages.FindAsync([imageId], TestContext.Current.CancellationToken);
        Assert.NotNull(image);
        Assert.Equal(originalDisplayOrder, image.DisplayOrder);
    }

    // ---------------------------------------------------------------------------
    // ReassignBookImageTypeAsync
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task ReassignBookImageTypeAsync_UpdatesBookImageTypeIdForMatchingRow()
    {
        // Arrange
        int bookId = await SeedBookAsync();
        int imageId = await SeedImageAsync(bookId, bookImageTypeId: 0, displayOrder: 0);

        // Act
        await _sut.ReassignBookImageTypeAsync(bookId, imageId, newTypeId: 2, TestContext.Current.CancellationToken);

        // Assert
        await using var dbContext = _factory.CreateDbContext();
        var image = await dbContext.BookImages.FindAsync([imageId], TestContext.Current.CancellationToken);
        Assert.NotNull(image);
        Assert.Equal(2, image.BookImageTypeId);
    }

    [Fact]
    public async Task ReassignBookImageTypeAsync_WhenImageIdNotOwnedByBook_UpdatesNoRows()
    {
        // Arrange — imageId belongs to book2, not book1
        int book1Id = await SeedBookAsync();
        int book2Id = await SeedBookAsync();
        int imageId = await SeedImageAsync(book2Id, bookImageTypeId: 0, displayOrder: 0);

        // Act — pass book1Id but image belongs to book2
        await _sut.ReassignBookImageTypeAsync(book1Id, imageId, newTypeId: 3, TestContext.Current.CancellationToken);

        // Assert — BookImageTypeId on book2's image unchanged
        await using var dbContext = _factory.CreateDbContext();
        var image = await dbContext.BookImages.FindAsync([imageId], TestContext.Current.CancellationToken);
        Assert.NotNull(image);
        Assert.Equal(0, image.BookImageTypeId);
    }

    [Fact]
    public async Task ReassignBookImageTypeAsync_WhenNoImageExists_ThrowsNoException()
    {
        // Arrange
        int bookId = await SeedBookAsync();
        int nonExistentImageId = 99999;

        // Act + Assert — no exception thrown
        await _sut.ReassignBookImageTypeAsync(bookId, nonExistentImageId, newTypeId: 2, TestContext.Current.CancellationToken);
    }

    // ---------------------------------------------------------------------------
    // RemoveBookImageByIdAsync
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task RemoveBookImageByIdAsync_DeletesMatchingRow()
    {
        // Arrange
        int bookId = await SeedBookAsync();
        int imageId = await SeedImageAsync(bookId, bookImageTypeId: 0, displayOrder: 1);

        // Act
        await _sut.RemoveBookImageByIdAsync(bookId, imageId, TestContext.Current.CancellationToken);

        // Assert — row should be gone
        await using var dbContext = _factory.CreateDbContext();
        bool exists = await dbContext.BookImages.AnyAsync(i => i.BookImageId == imageId, TestContext.Current.CancellationToken);
        Assert.False(exists);
    }

    [Fact]
    public async Task RemoveBookImageByIdAsync_WhenImageIdDoesNotExist_DoesNotThrow()
    {
        // Arrange
        int bookId = await SeedBookAsync();
        int nonExistentImageId = 99999;

        // Act + Assert — no exception thrown; is a no-op
        await _sut.RemoveBookImageByIdAsync(bookId, nonExistentImageId, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task RemoveBookImageByIdAsync_WhenImageIdNotOwnedByBook_DeletesNoRows()
    {
        // Arrange — imageId belongs to book2, not book1
        int book1Id = await SeedBookAsync();
        int book2Id = await SeedBookAsync();
        int imageId = await SeedImageAsync(book2Id, bookImageTypeId: 0, displayOrder: 1);

        // Act — pass book1Id but image belongs to book2
        await _sut.RemoveBookImageByIdAsync(book1Id, imageId, TestContext.Current.CancellationToken);

        // Assert — row must still exist; book1 cannot delete book2's image
        await using var dbContext = _factory.CreateDbContext();
        bool exists = await dbContext.BookImages.AnyAsync(i => i.BookImageId == imageId, TestContext.Current.CancellationToken);
        Assert.True(exists);
    }
}
