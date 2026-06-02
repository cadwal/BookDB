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

namespace BookDB.Logic.Tests;

/// <summary>
/// Tests for the multi-image BookImageService methods.
/// Uses the same temp-file SQLite fixture as BookServiceTests (FTS5 requires a real file).
/// </summary>
public sealed class BookServiceImageTests : IDisposable
{
    private readonly string _dbPath;
    private readonly string _connectionString;
    private readonly TestBookDbContextFactory _factory;
    private readonly BookImageService _sut;

    public BookServiceImageTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"bookdb_test_{Guid.NewGuid():N}.db");
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
        try { File.Delete(_dbPath); } catch { /* best effort */ }
    }

    private async Task<int> SeedBookAsync()
    {
        await using var dbContext = _factory.CreateDbContext();
        var book = new Book { Title = "Test Book" };
        dbContext.Books.Add(book);
        await dbContext.SaveChangesAsync();
        return book.BookId;
    }

    private async Task SeedImageAsync(int bookId, int bookImageTypeId, byte[] imageData, bool isPrimary = false)
    {
        await using var dbContext = _factory.CreateDbContext();
        dbContext.BookImages.Add(new BookImage
        {
            BookId = bookId,
            ImageData = imageData,
            MimeType = "image/jpeg",
            IsPrimary = isPrimary,
            DisplayOrder = bookImageTypeId,
            BookImageTypeId = bookImageTypeId,
            Added = DateTime.UtcNow
        });
        await dbContext.SaveChangesAsync();
    }

    // ---------------------------------------------------------------------------
    // GetBookImagesAsync
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task GetBookImagesAsync_ReturnsAllRowsForBook()
    {
        // Arrange
        var bookId = await SeedBookAsync();
        var coverBytes = new byte[] { 0xFF, 0xD8, 0xFF };
        var backCoverBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47 };
        await SeedImageAsync(bookId, 0, coverBytes, isPrimary: true);
        await SeedImageAsync(bookId, 2, backCoverBytes);

        // Act
        var images = await _sut.GetBookImagesAsync(bookId, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, images.Count);
        Assert.Contains(images, i => i.BookImageTypeId == 0);
        Assert.Contains(images, i => i.BookImageTypeId == 2);
    }

    // ---------------------------------------------------------------------------
    // SaveBookImageByTypeAsync — replace existing row for same type
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task SaveBookImageByTypeAsync_ReplacesExistingRowForSameType()
    {
        // Arrange
        var bookId = await SeedBookAsync();
        var originalBytes = new byte[] { 0xFF, 0xD8, 0xFF };
        var newBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47 };
        await _sut.SaveBookImageByTypeAsync(bookId, 0, originalBytes, TestContext.Current.CancellationToken);

        // Act
        await _sut.SaveBookImageByTypeAsync(bookId, 0, newBytes, TestContext.Current.CancellationToken);

        // Assert
        var images = await _sut.GetBookImagesAsync(bookId, TestContext.Current.CancellationToken);
        var only = Assert.Single(images);
        Assert.Equal(newBytes, only.ImageData);
    }

    // ---------------------------------------------------------------------------
    // SaveBookImageByTypeAsync — preserve other types
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task SaveBookImageByTypeAsync_PreservesOtherTypes()
    {
        // Arrange
        var bookId = await SeedBookAsync();
        var coverBytes = new byte[] { 0xFF, 0xD8, 0xFF };
        var backCoverBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47 };
        var newCoverBytes = new byte[] { 0x47, 0x49, 0x46 };
        await _sut.SaveBookImageByTypeAsync(bookId, 0, coverBytes, TestContext.Current.CancellationToken);
        await _sut.SaveBookImageByTypeAsync(bookId, 2, backCoverBytes, TestContext.Current.CancellationToken);

        // Act
        await _sut.SaveBookImageByTypeAsync(bookId, 0, newCoverBytes, TestContext.Current.CancellationToken);

        // Assert
        var images = await _sut.GetBookImagesAsync(bookId, TestContext.Current.CancellationToken);
        Assert.Equal(2, images.Count);
        var cover = Assert.Single(images, i => i.BookImageTypeId == 0);
        Assert.Equal(newCoverBytes, cover.ImageData);
        var backCover = Assert.Single(images, i => i.BookImageTypeId == 2);
        Assert.Equal(backCoverBytes, backCover.ImageData);
    }

    // ---------------------------------------------------------------------------
    // IMG-04a: SaveBookImageByTypeAsync — IsPrimary = true only for TypeId 0 (Cover)
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task SaveBookImageByTypeAsync_SetsPrimaryOnlyForTypeId0()
    {
        // Arrange
        var bookId = await SeedBookAsync();
        var coverBytes = new byte[] { 0xFF, 0xD8, 0xFF };
        var backCoverBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47 };

        // Act
        await _sut.SaveBookImageByTypeAsync(bookId, 0, coverBytes, TestContext.Current.CancellationToken);
        await _sut.SaveBookImageByTypeAsync(bookId, 2, backCoverBytes, TestContext.Current.CancellationToken);

        // Assert
        var images = await _sut.GetBookImagesAsync(bookId, TestContext.Current.CancellationToken);
        var cover = Assert.Single(images, i => i.BookImageTypeId == 0);
        var backCover = Assert.Single(images, i => i.BookImageTypeId == 2);
        Assert.True(cover.IsPrimary);
        Assert.False(backCover.IsPrimary);
    }

    // ---------------------------------------------------------------------------
    // IMG-04b: RemoveBookImageByTypeAsync — removes only the specified type
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task RemoveBookImageByTypeAsync_RemovesOnlySpecifiedType()
    {
        // Arrange
        var bookId = await SeedBookAsync();
        var coverBytes = new byte[] { 0xFF, 0xD8, 0xFF };
        var backCoverBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47 };
        await _sut.SaveBookImageByTypeAsync(bookId, 0, coverBytes, TestContext.Current.CancellationToken);
        await _sut.SaveBookImageByTypeAsync(bookId, 2, backCoverBytes, TestContext.Current.CancellationToken);

        // Act
        await _sut.RemoveBookImageByTypeAsync(bookId, 2, TestContext.Current.CancellationToken);

        // Assert
        var images = await _sut.GetBookImagesAsync(bookId, TestContext.Current.CancellationToken);
        var only = Assert.Single(images);
        Assert.Equal(0, only.BookImageTypeId);
    }
}
