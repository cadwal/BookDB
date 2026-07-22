using System;
using System.Threading;
using System.Threading.Tasks;
using BookDB.Data;
using BookDB.Data.DbContexts;
using System.Linq;
using BookDB.Logic.Services;
using BookDB.Models.Entities;
using BookDB.Models.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BookDB.Data.PostgreSQL.Tests;

/// <summary>
/// Lookup duplicate-name detection on Postgres. The check used a SQLite <c>NOCASE</c> collation that
/// has no Postgres equivalent — proving the ILIKE-backed matcher folds case (including non-ASCII,
/// where Postgres <c>lower()</c> is Unicode-aware) keeps create/rename guards working on the remote backend.
/// </summary>
public sealed class PostgresLookupDuplicateTests : IClassFixture<PostgresTestDbFixture>
{
    private readonly PostgresTestDbFixture _fixture;

    public PostgresLookupDuplicateTests(PostgresTestDbFixture fixture) => _fixture = fixture;

    private async Task<(ServiceProvider sp, LookupManagementService service)> BuildAsync(CancellationToken ct)
    {
        var runner = new PostgresDbUpRunner(_fixture.ConnectionString, NullLogger<DatabaseStartupService>.Instance);
        await runner.RunAsync(new Progress<(int applied, int total)>(), ct);

        var services = new ServiceCollection();
        services.AddSingleton<IDataChangeTracker, DataChangeTracker>();
        services.AddPostgresProvider(_fixture.ConnectionString);
        var sp = services.BuildServiceProvider();
        var service = new LookupManagementService(
            sp.GetRequiredService<IDbContextFactory<BookDbContext>>(),
            sp.GetRequiredService<Data.Interfaces.ILookupNameMatcher>());
        return (sp, service);
    }

    [Fact]
    public async Task AddCollection_RejectsCaseInsensitiveDuplicate()
    {
        Assert.SkipUnless(_fixture.IsAvailable, _fixture.SkipReason);
        var ct = TestContext.Current.CancellationToken;
        var (sp, service) = await BuildAsync(ct);
        await using var _ = sp;

        var name = $"Shelf_{Guid.NewGuid():N}";
        await service.AddCollectionAsync(name, ct);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.AddCollectionAsync(name.ToUpperInvariant(), ct));
    }

    [Fact]
    public async Task AddPublisher_RejectsNonAsciiCaseDuplicate()
    {
        Assert.SkipUnless(_fixture.IsAvailable, _fixture.SkipReason);
        var ct = TestContext.Current.CancellationToken;
        var (sp, service) = await BuildAsync(ct);
        await using var _ = sp;

        var stem = Guid.NewGuid().ToString("N")[..8];
        await service.AddPublisherAsync($"Åsa{stem}", ct);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.AddPublisherAsync($"ÅSA{stem}", ct));
    }

    [Fact]
    public async Task RenamePublisher_RejectsCaseInsensitiveDuplicate()
    {
        Assert.SkipUnless(_fixture.IsAvailable, _fixture.SkipReason);
        var ct = TestContext.Current.CancellationToken;
        var (sp, service) = await BuildAsync(ct);
        await using var _ = sp;

        var stem = Guid.NewGuid().ToString("N")[..8];
        await service.AddPublisherAsync($"First{stem}", ct);
        var secondId = await service.AddPublisherAsync($"Second{stem}", ct);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.RenamePublisherAsync(secondId, $"FIRST{stem}", ct));
    }

    [Fact]
    public async Task CleanupIgnore_ExcludesTheProposal_AndCascadesWhenThePersonIsDeleted()
    {
        Assert.SkipUnless(_fixture.IsAvailable, _fixture.SkipReason);
        var ct = TestContext.Current.CancellationToken;
        var (sp, service) = await BuildAsync(ct);
        await using var _ = sp;
        var factory = sp.GetRequiredService<IDbContextFactory<BookDbContext>>();

        var dirty = $"by {Guid.NewGuid():N}.";
        int personId;
        await using (var db = await factory.CreateDbContextAsync(ct))
        {
            var p = new Person { DisplayName = dirty, SortName = dirty };
            db.People.Add(p);
            await db.SaveChangesAsync(ct);
            personId = p.PersonId;
        }

        // The shared container may hold other renames, so target this person specifically throughout.
        var (before, _, _) = await service.ScanPersonNameCleanupAsync(ct);
        var mine = before.Single(r => r.PersonId == personId);
        await service.AddCleanupIgnoreAsync(mine, ct);

        var (after, _, ignored) = await service.ScanPersonNameCleanupAsync(ct);
        Assert.DoesNotContain(after, r => r.PersonId == personId);
        Assert.True(ignored >= 1);
        Assert.Contains(await service.GetCleanupIgnoresAsync(ct), i => i.PersonId == personId);

        await service.DeletePersonAsync(personId, ct);
        Assert.DoesNotContain(await service.GetCleanupIgnoresAsync(ct), i => i.PersonId == personId);
    }
}
