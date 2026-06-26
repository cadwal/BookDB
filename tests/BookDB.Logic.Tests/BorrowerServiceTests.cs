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
/// Integration tests for BorrowerService using temp-file SQLite + DbUp migration pattern.
/// Covers the FK guard on DeleteAsync (borrower-deletion safety).
/// </summary>
public sealed class BorrowerServiceTests : IDisposable
{
    private readonly string _dbPath;
    private readonly TestBookDbContextFactory _factory;
    private readonly BorrowerService _sut;

    public BorrowerServiceTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"bookdb_borrower_test_{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={_dbPath}";

        var upgrader = SqliteExtensions.SqliteDatabase(DeployChanges.To, connectionString)
            .WithScriptsEmbeddedInAssembly(
                Assembly.GetAssembly(typeof(BookDB.Data.Sqlite.SqliteDbUpRunner))!,
                name => name.Contains(".Migrations."))
            .LogToNowhere()
            .Build();
        var result = upgrader.PerformUpgrade();
        if (!result.Successful)
            throw new InvalidOperationException($"DbUp migration failed: {result.Error}");

        var options = new DbContextOptionsBuilder<BookDbContext>()
            .UseSqlite(connectionString)
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
            .Options;
        _factory = new TestBookDbContextFactory(options);
        _sut = new BorrowerService(_factory, new BookDB.Data.Sqlite.SqliteConstraintViolationClassifier());
    }

    public void Dispose()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        try { File.Delete(_dbPath); } catch { /* best effort */ }
    }

    [Fact]
    public async Task SearchAsync_MatchesRegardlessOfCase()
    {
        await using var db = await _factory.CreateDbContextAsync(TestContext.Current.CancellationToken);
        db.Borrowers.Add(new Borrower { FirstName = "Alice", LastName = "Andersson" });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var results = await _sut.SearchAsync("alice anders", TestContext.Current.CancellationToken);

        Assert.Contains(results, b => b.LastName == "Andersson");
    }

    [Fact]
    public async Task SearchAsync_TreatsWildcardsLiterally()
    {
        await using var db = await _factory.CreateDbContextAsync(TestContext.Current.CancellationToken);
        db.Borrowers.Add(new Borrower { FirstName = "Real", LastName = "Person" });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        // A bare "%" must be escaped to a literal, so it matches only names that actually contain '%'.
        var results = await _sut.SearchAsync("%", TestContext.Current.CancellationToken);

        Assert.DoesNotContain(results, b => b.LastName == "Person");
    }

    [Fact]
    public async Task Delete_WithLoans_Throws()
    {
        // Arrange: seed a borrower and a book, then create a loan referencing the borrower
        await using var db = await _factory.CreateDbContextAsync(TestContext.Current.CancellationToken);
        var borrower = new Borrower { FirstName = "Bob", LastName = "Jones" };
        db.Borrowers.Add(borrower);
        var book = new Book { Title = "Borrowed Book" };
        db.Books.Add(book);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var loan = new Loan
        {
            BookId = book.BookId,
            BorrowerId = borrower.BorrowerId,
            LoanedDate = DateTime.UtcNow
        };
        db.Loans.Add(loan);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act + Assert: attempting to delete the borrower must throw
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.DeleteAsync(borrower.BorrowerId, TestContext.Current.CancellationToken));
    }
}
