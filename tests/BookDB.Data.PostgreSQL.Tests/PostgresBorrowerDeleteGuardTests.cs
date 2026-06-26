using System;
using System.Threading;
using System.Threading.Tasks;
using BookDB.Data;
using BookDB.Data.DbContexts;
using BookDB.Data.Interfaces;
using BookDB.Logic.Services;
using BookDB.Models.Entities;
using BookDB.Models.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BookDB.Data.PostgreSQL.Tests;

/// <summary>
/// The borrower-delete FK guard on Postgres. The block used to rely on a SQLite-only error code
/// (SqliteErrorCode 19), so on the remote backend a borrower with loan history could be deleted unguarded.
/// Proving the delete throws confirms the provider-neutral <see cref="IConstraintViolationClassifier"/> seam
/// catches the foreign-key violation (SQLSTATE 23503) on Postgres too.
/// </summary>
public sealed class PostgresBorrowerDeleteGuardTests : IClassFixture<PostgresTestDbFixture>
{
    private readonly PostgresTestDbFixture _fixture;

    public PostgresBorrowerDeleteGuardTests(PostgresTestDbFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task DeleteBorrower_WithLoans_ThrowsOnPostgres()
    {
        Assert.SkipUnless(_fixture.IsAvailable, _fixture.SkipReason);
        var ct = TestContext.Current.CancellationToken;

        var runner = new PostgresDbUpRunner(_fixture.ConnectionString, NullLogger<DatabaseStartupService>.Instance);
        await runner.RunAsync(new Progress<(int applied, int total)>(), ct);

        var services = new ServiceCollection();
        services.AddSingleton<IDataChangeTracker, DataChangeTracker>();
        services.AddPostgresProvider(_fixture.ConnectionString);
        await using var sp = services.BuildServiceProvider();
        var factory = sp.GetRequiredService<IDbContextFactory<BookDbContext>>();

        int borrowerId;
        await using (var db = await factory.CreateDbContextAsync(ct))
        {
            var borrower = new Borrower { FirstName = "Bob", LastName = $"Jones_{Guid.NewGuid():N}" };
            db.Borrowers.Add(borrower);
            var book = new Book { Title = $"Borrowed_{Guid.NewGuid():N}" };
            db.Books.Add(book);
            await db.SaveChangesAsync(ct);

            db.Loans.Add(new Loan { BookId = book.BookId, BorrowerId = borrower.BorrowerId, LoanedDate = DateTime.UtcNow });
            await db.SaveChangesAsync(ct);
            borrowerId = borrower.BorrowerId;
        }

        var service = new BorrowerService(factory, sp.GetRequiredService<IConstraintViolationClassifier>());

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteAsync(borrowerId, ct));
    }
}
