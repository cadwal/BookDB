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
using Npgsql;
using Xunit;

namespace BookDB.Data.PostgreSQL.Tests;

/// <summary>
/// Exercises the real <see cref="PostgresProviderServiceCollectionExtensions.AddPostgresProvider"/> wiring
/// against a live container: full entity round-trips (native bool/timestamp/bytea types, relations) and that
/// the registered <c>DataChangeCommandInterceptor</c> flags the change tracker on every write.
/// </summary>
public sealed class PostgresCrudRoundTripTests : IClassFixture<PostgresTestDbFixture>
{
    private readonly PostgresTestDbFixture _fixture;

    public PostgresCrudRoundTripTests(PostgresTestDbFixture fixture) => _fixture = fixture;

    private async Task<(ServiceProvider sp, IDbContextFactory<BookDbContext> factory, IDataChangeTracker tracker)>
        BuildProviderAsync(CancellationToken ct, string? connectionString = null)
    {
        // Schema is idempotent — DbUp's journal skips applied scripts — so every test can ensure it exists.
        var runner = new PostgresDbUpRunner(_fixture.ConnectionString, NullLogger<DatabaseStartupService>.Instance);
        await runner.RunAsync(new Progress<(int applied, int total)>(), ct);

        var services = new ServiceCollection();
        services.AddSingleton<IDataChangeTracker, DataChangeTracker>();
        services.AddPostgresProvider(connectionString ?? _fixture.ConnectionString);
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
        Assert.True(bookId > 0, "Identity column should assign a positive BookId.");

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
            // Postgres timestamp has microsecond precision; DateTime ticks are finer, so allow a small delta.
            Assert.Equal(added, reloaded.Added, TimeSpan.FromMilliseconds(1));

            Assert.NotNull(reloaded.Publisher);
            Assert.Equal($"Pub {marker}", reloaded.Publisher!.Name);

            var image = Assert.Single(reloaded.Images);
            Assert.Equal(imageBytes, image.ImageData);
            Assert.Equal("image/png", image.MimeType);
        }
    }

    [Fact]
    public async Task DateTime_StoredAsUtcWallClock_RegardlessOfSessionTimezone()
    {
        Assert.SkipUnless(_fixture.IsAvailable, _fixture.SkipReason);
        var ct = TestContext.Current.CancellationToken;

        // A client whose Postgres session runs in a non-UTC zone. With the default DateTime->timestamptz
        // mapping this would shift the stored wall-clock; the provider's timestamp-without-time-zone mapping
        // must store the UTC wall-clock verbatim so every client reads the same instant.
        var nonUtcConnectionString = _fixture.ConnectionString + ";Timezone=America/New_York";
        var (sp, factory, _) = await BuildProviderAsync(ct, nonUtcConnectionString);
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

        // Read the raw timestamp value (Kind=Unspecified wall-clock) — bypassing EF's converter — and confirm
        // it is the UTC wall-clock, not the New York one (which would be off by the UTC offset).
        await using var connection = new NpgsqlConnection(nonUtcConnectionString);
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand("SELECT \"Added\" FROM \"Book\" WHERE \"BookId\" = @id", connection);
        command.Parameters.AddWithValue("id", bookId);
        var storedWallClock = (DateTime)(await command.ExecuteScalarAsync(ct))!;

        Assert.Equal(added, DateTime.SpecifyKind(storedWallClock, DateTimeKind.Utc), TimeSpan.FromMilliseconds(1));
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
