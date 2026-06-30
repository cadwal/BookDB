using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BookDB.Data;
using BookDB.Data.DbContexts;
using BookDB.Models.Entities;
using BookDB.Models.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MySqlConnector;
using Xunit;

namespace BookDB.Data.MySql.Tests;

/// <summary>
/// Exercises the real <see cref="MySqlProviderServiceCollectionExtensions.AddMySqlProvider"/> wiring against a
/// live container: full entity round-trips with native MySQL types (tinyint(1) bools, datetime(6) UTC dates,
/// LONGBLOB bytes, utf8mb4 Unicode), AUTO_INCREMENT keys, and that the registered DataChangeCommandInterceptor
/// flags the change tracker on every write. Run on both engines via the subclasses at the bottom.
/// </summary>
public abstract class MySqlCrudRoundTripTests
{
    private readonly MySqlTestDbFixture _fixture;

    protected MySqlCrudRoundTripTests(MySqlTestDbFixture fixture) => _fixture = fixture;

    private async Task<(ServiceProvider sp, IDbContextFactory<BookDbContext> factory, IDataChangeTracker tracker)>
        BuildProviderAsync(CancellationToken ct)
    {
        // Schema is idempotent — DbUp's journal skips applied scripts — so every test can ensure it exists.
        var runner = new MySqlDbUpRunner(_fixture.ConnectionString, NullLogger<DatabaseStartupService>.Instance);
        await runner.RunAsync(new Progress<(int applied, int total)>(), ct);

        var services = new ServiceCollection();
        services.AddSingleton<IDataChangeTracker, DataChangeTracker>();
        services.AddMySqlProvider(_fixture.ConnectionString);
        var sp = services.BuildServiceProvider();
        return (sp, sp.GetRequiredService<IDbContextFactory<BookDbContext>>(), sp.GetRequiredService<IDataChangeTracker>());
    }

    [Fact]
    public async Task Book_WithBoolsDatesAndImage_RoundTrips_AndFlagsTracker()
    {
        Assert.SkipUnless(_fixture.IsAvailable, _fixture.SkipReason);
        var ct = TestContext.Current.CancellationToken;
        var (sp, factory, tracker) = await BuildProviderAsync(ct);
        await using var scope = sp;

        var marker = Guid.NewGuid().ToString("N");
        var added = DateTime.UtcNow;
        var imageBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0xFF };
        int bookId;

        await using (var ctx = await factory.CreateDbContextAsync(ct))
        {
            var publisher = new Publisher { Name = $"Pub {marker}" };
            var book = new Book
            {
                Title = $"Round Trip {marker}",
                Publisher = publisher,
                Signed = true,
                OutOfPrint = false,
                Favorite = true,
                Display = true,
                PurchasePrice = 19.95m,
                Added = added,
                Updated = added,
                Images =
                {
                    new BookImage { ImageData = imageBytes, MimeType = "image/png", IsPrimary = true, Added = added },
                },
            };
            ctx.Books.Add(book);
            await ctx.SaveChangesAsync(ct);
            bookId = book.BookId;
        }

        Assert.True(tracker.HasChanges);
        Assert.True(bookId > 0, "AUTO_INCREMENT column should assign a positive BookId.");

        await using (var ctx = await factory.CreateDbContextAsync(ct))
        {
            var reloaded = await ctx.Books
                .Include(b => b.Publisher)
                .Include(b => b.Images)
                .SingleAsync(b => b.BookId == bookId, ct);

            Assert.True(reloaded.Signed);
            Assert.False(reloaded.OutOfPrint);
            Assert.True(reloaded.Favorite);
            Assert.Equal(19.95m, reloaded.PurchasePrice);
            // datetime(6) has microsecond precision; DateTime ticks are finer, so allow a small delta.
            Assert.Equal(added, reloaded.Added, TimeSpan.FromMilliseconds(1));

            Assert.NotNull(reloaded.Publisher);
            Assert.Equal($"Pub {marker}", reloaded.Publisher!.Name);

            var image = Assert.Single(reloaded.Images);
            Assert.Equal(imageBytes, image.ImageData);
            Assert.Equal("image/png", image.MimeType);
        }
    }

    [Fact]
    public async Task Unicode_Utf8mb4_RoundTripsIncludingFourByteCharacters()
    {
        Assert.SkipUnless(_fixture.IsAvailable, _fixture.SkipReason);
        var ct = TestContext.Current.CancellationToken;
        var (sp, factory, _) = await BuildProviderAsync(ct);
        await using var scope = sp;

        // Accented Latin, CJK, a Nordic glyph, and a 4-byte emoji — the emoji only survives on utf8mb4.
        var title = $"Café 日本語 Ø 📚 {Guid.NewGuid():N}";
        int bookId;
        await using (var ctx = await factory.CreateDbContextAsync(ct))
        {
            var book = new Book { Title = title, Added = DateTime.UtcNow, Updated = DateTime.UtcNow };
            ctx.Books.Add(book);
            await ctx.SaveChangesAsync(ct);
            bookId = book.BookId;
        }

        await using (var ctx = await factory.CreateDbContextAsync(ct))
        {
            var reloaded = await ctx.Books.SingleAsync(b => b.BookId == bookId, ct);
            Assert.Equal(title, reloaded.Title);
        }
    }

    [Fact]
    public async Task DateTime_StoredAsUtcWallClock_AndReadBackAsUtc()
    {
        Assert.SkipUnless(_fixture.IsAvailable, _fixture.SkipReason);
        var ct = TestContext.Current.CancellationToken;
        var (sp, factory, _) = await BuildProviderAsync(ct);
        await using var scope = sp;

        var added = DateTime.UtcNow;
        var marker = Guid.NewGuid().ToString("N");
        int bookId;
        await using (var ctx = await factory.CreateDbContextAsync(ct))
        {
            var book = new Book { Title = $"TZ {marker}", Added = added, Updated = added };
            ctx.Books.Add(book);
            await ctx.SaveChangesAsync(ct);
            bookId = book.BookId;
        }

        // Read the raw datetime(6) value (Kind=Unspecified wall-clock) — bypassing EF's converter — and confirm
        // it is the UTC wall-clock. MySQL DATETIME is timezone-naive, so no session-timezone shift can occur.
        await using var connection = new MySqlConnection(_fixture.ConnectionString);
        await connection.OpenAsync(ct);
        await using var command = new MySqlCommand("SELECT `Added` FROM `Book` WHERE `BookId` = @id", connection);
        command.Parameters.AddWithValue("@id", bookId);
        var storedWallClock = (DateTime)(await command.ExecuteScalarAsync(ct))!;
        Assert.Equal(added, DateTime.SpecifyKind(storedWallClock, DateTimeKind.Utc), TimeSpan.FromMilliseconds(1));

        // The provider re-tags the value Utc on read so callers get a UTC instant, not Unspecified.
        await using var ctx2 = await factory.CreateDbContextAsync(ct);
        var reloaded = await ctx2.Books.SingleAsync(b => b.BookId == bookId, ct);
        Assert.Equal(DateTimeKind.Utc, reloaded.Added.Kind);
        Assert.Equal(added, reloaded.Added, TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public async Task AutoIncrement_AssignsAscendingKeys()
    {
        Assert.SkipUnless(_fixture.IsAvailable, _fixture.SkipReason);
        var ct = TestContext.Current.CancellationToken;
        var (sp, factory, _) = await BuildProviderAsync(ct);
        await using var scope = sp;

        var marker = Guid.NewGuid().ToString("N");
        await using var ctx = await factory.CreateDbContextAsync(ct);
        var first = new Book { Title = $"First {marker}", Added = DateTime.UtcNow, Updated = DateTime.UtcNow };
        ctx.Books.Add(first);
        await ctx.SaveChangesAsync(ct);

        var second = new Book { Title = $"Second {marker}", Added = DateTime.UtcNow, Updated = DateTime.UtcNow };
        ctx.Books.Add(second);
        await ctx.SaveChangesAsync(ct);

        Assert.True(first.BookId > 0);
        Assert.True(second.BookId > first.BookId, "AUTO_INCREMENT must assign a strictly higher key.");
    }

    [Fact]
    public async Task BulkExecuteDelete_FlagsTracker()
    {
        Assert.SkipUnless(_fixture.IsAvailable, _fixture.SkipReason);
        var ct = TestContext.Current.CancellationToken;
        var (sp, factory, tracker) = await BuildProviderAsync(ct);
        await using var scope = sp;

        var name = $"Series {Guid.NewGuid():N}";
        await using (var ctx = await factory.CreateDbContextAsync(ct))
        {
            ctx.Series.Add(new Series { Name = name });
            await ctx.SaveChangesAsync(ct);
        }
        tracker.Reset();

        await using (var ctx = await factory.CreateDbContextAsync(ct))
        {
            // Bulk delete bypasses SaveChanges — only the command interceptor can observe it.
            await ctx.Series.Where(s => s.Name == name).ExecuteDeleteAsync(ct);
        }

        Assert.True(tracker.HasChanges);
    }

    [Fact]
    public async Task Read_DoesNotFlagTracker()
    {
        Assert.SkipUnless(_fixture.IsAvailable, _fixture.SkipReason);
        var ct = TestContext.Current.CancellationToken;
        var (sp, factory, tracker) = await BuildProviderAsync(ct);
        await using var scope = sp;

        tracker.Reset();
        await using var ctx = await factory.CreateDbContextAsync(ct);
        _ = await ctx.Books.AsNoTracking().Take(1).ToListAsync(ct);

        Assert.False(tracker.HasChanges);
    }
}

public sealed class MySqlServerCrudRoundTripTests : MySqlCrudRoundTripTests, IClassFixture<MySqlServerFixture>
{
    public MySqlServerCrudRoundTripTests(MySqlServerFixture fixture) : base(fixture) { }
}

public sealed class MariaDbCrudRoundTripTests : MySqlCrudRoundTripTests, IClassFixture<MariaDbFixture>
{
    public MariaDbCrudRoundTripTests(MariaDbFixture fixture) : base(fixture) { }
}
