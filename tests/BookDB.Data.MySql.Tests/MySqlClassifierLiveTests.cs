using System;
using System.Threading;
using System.Threading.Tasks;
using BookDB.Data;
using BookDB.Data.DbContexts;
using BookDB.Data.Interfaces;
using BookDB.Models.Entities;
using BookDB.Models.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BookDB.Data.MySql.Tests;

/// <summary>
/// Exercises the classifiers against real MySqlExceptions from a live server — the branches that cannot be
/// unit-tested because MySqlException has no public constructor. Proves the loss-classification split: a server
/// SQL error (FK 1451, duplicate 1062) is correctly identified yet is never mistaken for a connection loss. Run
/// on both engines via the subclasses at the bottom.
/// </summary>
public abstract class MySqlClassifierLiveTests
{
    private readonly MySqlTestDbFixture _fixture;

    protected MySqlClassifierLiveTests(MySqlTestDbFixture fixture) => _fixture = fixture;

    private async Task<(ServiceProvider sp, IDbContextFactory<BookDbContext> factory,
        IConstraintViolationClassifier constraint, IConnectionFailureClassifier connection)> BuildAsync(CancellationToken ct)
    {
        var runner = new MySqlDbUpRunner(_fixture.ConnectionString, NullLogger<DatabaseStartupService>.Instance);
        await runner.RunAsync(new Progress<(int applied, int total)>(), ct);

        var services = new ServiceCollection();
        services.AddSingleton<IDataChangeTracker, DataChangeTracker>();
        services.AddMySqlProvider(_fixture.ConnectionString);
        var sp = services.BuildServiceProvider();
        return (sp, sp.GetRequiredService<IDbContextFactory<BookDbContext>>(),
            sp.GetRequiredService<IConstraintViolationClassifier>(),
            sp.GetRequiredService<IConnectionFailureClassifier>());
    }

    [Fact]
    public async Task ForeignKeyViolation_IsClassified_AndNotConnectionLoss()
    {
        Assert.SkipUnless(_fixture.IsAvailable, _fixture.SkipReason);
        var ct = TestContext.Current.CancellationToken;
        var (sp, factory, constraint, connection) = await BuildAsync(ct);
        await using var scope = sp;

        // A loan referencing a borrower blocks that borrower's delete (FK_Loan_Borrower is ON DELETE RESTRICT).
        int borrowerId;
        await using (var seed = await factory.CreateDbContextAsync(ct))
        {
            var borrower = new Borrower { FirstName = "Bob", LastName = $"Jones_{Guid.NewGuid():N}" };
            var book = new Book { Title = $"Borrowed_{Guid.NewGuid():N}" };
            seed.Borrowers.Add(borrower);
            seed.Books.Add(book);
            await seed.SaveChangesAsync(ct);
            seed.Loans.Add(new Loan { BookId = book.BookId, BorrowerId = borrower.BorrowerId, LoanedDate = DateTime.UtcNow });
            await seed.SaveChangesAsync(ct);
            borrowerId = borrower.BorrowerId;
        }

        await using var db = await factory.CreateDbContextAsync(ct);
        db.Borrowers.Remove(new Borrower { BorrowerId = borrowerId });
        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(ct));

        Assert.True(constraint.IsForeignKeyViolation(ex));
        Assert.False(connection.IsConnectionLoss(ex), "A server SQL error must never read as a connection loss.");
    }

    [Fact]
    public async Task UniqueViolation_IsNotForeignKey_AndNotConnectionLoss()
    {
        Assert.SkipUnless(_fixture.IsAvailable, _fixture.SkipReason);
        var ct = TestContext.Current.CancellationToken;
        var (sp, factory, constraint, connection) = await BuildAsync(ct);
        await using var scope = sp;

        var name = $"Pub_{Guid.NewGuid():N}";
        await using (var first = await factory.CreateDbContextAsync(ct))
        {
            first.Publishers.Add(new Publisher { Name = name });
            await first.SaveChangesAsync(ct);
        }

        await using var db = await factory.CreateDbContextAsync(ct);
        db.Publishers.Add(new Publisher { Name = name });   // UX_Publisher_Name duplicate
        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(ct));

        Assert.False(constraint.IsForeignKeyViolation(ex), "A unique violation is not a foreign-key violation.");
        Assert.False(connection.IsConnectionLoss(ex));
    }
}

public sealed class MySqlServerClassifierLiveTests : MySqlClassifierLiveTests, IClassFixture<MySqlServerFixture>
{
    public MySqlServerClassifierLiveTests(MySqlServerFixture fixture) : base(fixture) { }
}

public sealed class MariaDbClassifierLiveTests : MySqlClassifierLiveTests, IClassFixture<MariaDbFixture>
{
    public MariaDbClassifierLiveTests(MariaDbFixture fixture) : base(fixture) { }
}
