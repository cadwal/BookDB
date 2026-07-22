using System;
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

namespace BookDB.Data.MySql.Tests;

/// <summary>
/// Contributor reuse-or-create on MySQL/MariaDB: the person lookup lowers both sides, so a
/// case-variant author name reuses the stored person instead of creating a duplicate — the same
/// behaviour SQLite and Postgres get from the shared query. Run on both engines via the
/// subclasses at the bottom.
/// </summary>
public abstract class MySqlPersonReuseTests
{
    private readonly MySqlTestDbFixture _fixture;

    protected MySqlPersonReuseTests(MySqlTestDbFixture fixture) => _fixture = fixture;

    private async Task<(ServiceProvider sp, IDbContextFactory<BookDbContext> factory)> BuildAsync(
        CancellationToken ct)
    {
        var runner = new MySqlDbUpRunner(_fixture.ConnectionString, NullLogger<DatabaseStartupService>.Instance);
        await runner.RunAsync(new Progress<(int applied, int total)>(), ct);

        var services = new ServiceCollection();
        services.AddSingleton<IDataChangeTracker, DataChangeTracker>();
        services.AddMySqlProvider(_fixture.ConnectionString);
        var sp = services.BuildServiceProvider();
        return (sp, sp.GetRequiredService<IDbContextFactory<BookDbContext>>());
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

public sealed class MySqlServerPersonReuseTests : MySqlPersonReuseTests, IClassFixture<MySqlServerFixture>
{
    public MySqlServerPersonReuseTests(MySqlServerFixture fixture) : base(fixture) { }
}

public sealed class MariaDbPersonReuseTests : MySqlPersonReuseTests, IClassFixture<MariaDbFixture>
{
    public MariaDbPersonReuseTests(MariaDbFixture fixture) : base(fixture) { }
}
