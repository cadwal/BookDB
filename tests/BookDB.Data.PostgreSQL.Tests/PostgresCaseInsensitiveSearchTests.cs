using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BookDB.Data;
using BookDB.Data.DbContexts;
using BookDB.Logic.Services;
using BookDB.Models.Entities;
using BookDB.Models.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BookDB.Data.PostgreSQL.Tests;

/// <summary>
/// Name-search case-insensitivity on Postgres. Plain <c>LIKE</c> is case-sensitive on Postgres
/// (case-insensitive on SQLite), so both services lower-case the column and pattern and escape
/// wildcards — proving a lower-case query matches an upper-case stored name on the remote backend.
/// </summary>
public sealed class PostgresCaseInsensitiveSearchTests : IClassFixture<PostgresTestDbFixture>
{
    private readonly PostgresTestDbFixture _fixture;

    public PostgresCaseInsensitiveSearchTests(PostgresTestDbFixture fixture) => _fixture = fixture;

    private async Task<(ServiceProvider sp, IDbContextFactory<BookDbContext> factory)> BuildAsync(CancellationToken ct)
    {
        var runner = new PostgresDbUpRunner(_fixture.ConnectionString, NullLogger<DatabaseStartupService>.Instance);
        await runner.RunAsync(new Progress<(int applied, int total)>(), ct);

        var services = new ServiceCollection();
        services.AddSingleton<IDataChangeTracker, DataChangeTracker>();
        services.AddPostgresProvider(_fixture.ConnectionString);
        var sp = services.BuildServiceProvider();
        return (sp, sp.GetRequiredService<IDbContextFactory<BookDbContext>>());
    }

    [Fact]
    public async Task BorrowerSearch_MatchesRegardlessOfCase()
    {
        Assert.SkipUnless(_fixture.IsAvailable, _fixture.SkipReason);
        var ct = TestContext.Current.CancellationToken;
        var (sp, factory) = await BuildAsync(ct);
        await using var _ = sp;

        var last = $"Andersson{Guid.NewGuid():N}";
        await using (var db = await factory.CreateDbContextAsync(ct))
        {
            db.Borrowers.Add(new Borrower { FirstName = "Alice", LastName = last });
            await db.SaveChangesAsync(ct);
        }

        var service = new BorrowerService(factory, sp.GetRequiredService<Data.Interfaces.IConstraintViolationClassifier>());
        var results = await service.SearchAsync($"alice {last.ToLowerInvariant()[..12]}", ct);

        Assert.Contains(results, b => b.LastName == last);
    }

    [Fact]
    public async Task AddBookWithContributors_ReusesExistingPerson_RegardlessOfCase()
    {
        Assert.SkipUnless(_fixture.IsAvailable, _fixture.SkipReason);
        var ct = TestContext.Current.CancellationToken;
        var (sp, factory) = await BuildAsync(ct);
        await using var _ = sp;

        var display = $"Selma Lagerlof{Guid.NewGuid():N}";
        int existingId;
        await using (var db = await factory.CreateDbContextAsync(ct))
        {
            var person = new Person { DisplayName = display, SortName = display };
            db.People.Add(person);
            await db.SaveChangesAsync(ct);
            existingId = person.PersonId;
        }

        var service = new BookService(factory);
        var book = await service.AddBookWithContributorsAsync(
            new Book { Title = $"Case Reuse {Guid.NewGuid():N}" }, [display.ToLowerInvariant()], ct);

        await using var verify = await factory.CreateDbContextAsync(ct);
        var contributor = await verify.BookContributors.SingleAsync(bc => bc.BookId == book.BookId, ct);
        Assert.Equal(existingId, contributor.PersonId);
        var lowered = display.ToLowerInvariant();
        Assert.Equal(1, await verify.People.CountAsync(p => p.DisplayName.ToLower() == lowered, ct));
    }
}
