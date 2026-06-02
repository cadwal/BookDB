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
/// Integration tests for LoanService using temp-file SQLite + DbUp migration pattern.
/// Covers CheckOut and CheckIn behaviors.
/// </summary>
public sealed class LoanServiceTests : IDisposable
{
    private readonly string _dbPath;
    private readonly TestBookDbContextFactory _factory;
    private readonly LoanService _sut;

    public LoanServiceTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"bookdb_loan_test_{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={_dbPath}";

        var upgrader = SqliteExtensions.SqliteDatabase(DeployChanges.To, connectionString)
            .WithScriptsEmbeddedInAssembly(
                Assembly.GetAssembly(typeof(BookDbContext))!,
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
        _sut = new LoanService(_factory);
    }

    public void Dispose()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        try { File.Delete(_dbPath); } catch { /* best effort */ }
    }

    private async Task<(int BookId, int BorrowerId)> SeedBookAndBorrowerAsync()
    {
        await using var db = await _factory.CreateDbContextAsync(TestContext.Current.CancellationToken);
        var book = new Book { Title = "Test Book" };
        db.Books.Add(book);
        var borrower = new Borrower { FirstName = "Alice", LastName = "Smith" };
        db.Borrowers.Add(borrower);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return (book.BookId, borrower.BorrowerId);
    }

    [Fact]
    public async Task CheckOut_CreatesLoanRecord()
    {
        var (bookId, borrowerId) = await SeedBookAndBorrowerAsync();

        await _sut.CheckOutAsync(bookId, borrowerId, dueDate: null, TestContext.Current.CancellationToken);

        await using var db = await _factory.CreateDbContextAsync(TestContext.Current.CancellationToken);
        var loan = await db.Loans.FirstOrDefaultAsync(
            l => l.BookId == bookId && l.BorrowerId == borrowerId && l.ReturnedDate == null,
            TestContext.Current.CancellationToken);
        Assert.NotNull(loan);
        Assert.NotNull(loan!.LoanedDate);
        Assert.Null(loan.ReturnedDate);
    }

    [Fact]
    public async Task CheckOut_AlreadyLoaned_Throws()
    {
        var (bookId, borrowerId) = await SeedBookAndBorrowerAsync();

        await _sut.CheckOutAsync(bookId, borrowerId, dueDate: null, TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.CheckOutAsync(bookId, borrowerId, dueDate: null, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CheckIn_SetsReturnedDate()
    {
        var (bookId, borrowerId) = await SeedBookAndBorrowerAsync();

        await _sut.CheckOutAsync(bookId, borrowerId, dueDate: null, TestContext.Current.CancellationToken);
        await _sut.CheckInAsync(bookId, TestContext.Current.CancellationToken);

        await using var db = await _factory.CreateDbContextAsync(TestContext.Current.CancellationToken);
        var loan = await db.Loans.FirstOrDefaultAsync(
            l => l.BookId == bookId && l.BorrowerId == borrowerId,
            TestContext.Current.CancellationToken);
        Assert.NotNull(loan);
        Assert.NotNull(loan!.ReturnedDate);
    }

    [Fact]
    public async Task CheckIn_NoActiveLoan_Throws()
    {
        await using var db = await _factory.CreateDbContextAsync(TestContext.Current.CancellationToken);
        var book = new Book { Title = "No Loan Book" };
        db.Books.Add(book);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.CheckInAsync(book.BookId, TestContext.Current.CancellationToken));
    }
}
