using System;
using System.Collections.Generic;
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
/// Integration tests for the BookService.GetBooksAsync loaned-out filter.
/// Uses temp-file SQLite + DbUp pattern. Loans are inserted directly via DbContext
/// (not via LoanService) to avoid dependency ordering.
/// </summary>
public sealed class BookServiceLoanFilterTests : IDisposable
{
    private readonly string _dbPath;
    private readonly TestBookDbContextFactory _factory;
    private readonly BookService _sut;

    public BookServiceLoanFilterTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"bookdb_loanfilter_test_{Guid.NewGuid():N}.db");
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
        _sut = new BookService(_factory);
    }

    public void Dispose()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        try { File.Delete(_dbPath); } catch { /* best effort */ }
    }

    [Fact]
    public async Task GetBooksAsync_WithIsLoanedOutFilter_ReturnsOnlyLoanedBooks()
    {
        var ct = TestContext.Current.CancellationToken;

        // Seed a borrower (required for Loan FK)
        await using var db = await _factory.CreateDbContextAsync(ct);
        var borrower = new Borrower { FirstName = "Alice", LastName = "Test" };
        db.Borrowers.Add(borrower);
        var book1 = new Book { Title = "Loaned Book" };
        var book2 = new Book { Title = "Not Loaned Book" };
        db.Books.Add(book1);
        db.Books.Add(book2);
        await db.SaveChangesAsync(ct);

        // Create an active loan for book1 only
        var loan = new Loan
        {
            BookId = book1.BookId,
            BorrowerId = borrower.BorrowerId,
            LoanedDate = DateTime.UtcNow,
            ReturnedDate = null
        };
        db.Loans.Add(loan);
        await db.SaveChangesAsync(ct);

        // Act: filter by isLoanedOut = true
        var (books, filteredTotal, _) = await _sut.GetBooksAsync(
            collectionIds: null,
            searchBookIds: null,
            facetFilters: null,
            sortColumn: null,
            sortAscending: true,
            skip: 0,
            take: 100,
            isLoanedOut: true,
            ct: ct);

        // Assert: only book1 returned
        Assert.Equal(1, filteredTotal);
        Assert.Single(books);
        Assert.Equal(book1.BookId, books[0].BookId);
        Assert.True(books[0].IsLoaned);
        Assert.Equal("Alice Test", books[0].LoanedToName);
    }

    [Fact]
    public async Task GetBooksAsync_WithIsLoanedOutFilter_ExcludesReturnedBooks()
    {
        var ct = TestContext.Current.CancellationToken;

        // Seed a borrower and a book with a returned loan
        await using var db = await _factory.CreateDbContextAsync(ct);
        var borrower = new Borrower { FirstName = "Bob", LastName = "Test" };
        db.Borrowers.Add(borrower);
        var book = new Book { Title = "Returned Book" };
        db.Books.Add(book);
        await db.SaveChangesAsync(ct);

        // Create a returned loan (ReturnedDate is set)
        var loan = new Loan
        {
            BookId = book.BookId,
            BorrowerId = borrower.BorrowerId,
            LoanedDate = DateTime.UtcNow.AddDays(-7),
            ReturnedDate = DateTime.UtcNow.AddDays(-1)
        };
        db.Loans.Add(loan);
        await db.SaveChangesAsync(ct);

        // Act: filter by isLoanedOut = true
        var (books, filteredTotal, _) = await _sut.GetBooksAsync(
            collectionIds: null,
            searchBookIds: null,
            facetFilters: null,
            sortColumn: null,
            sortAscending: true,
            skip: 0,
            take: 100,
            isLoanedOut: true,
            ct: ct);

        // Assert: zero books returned (loan was returned)
        Assert.Equal(0, filteredTotal);
        Assert.Empty(books);
    }
}
