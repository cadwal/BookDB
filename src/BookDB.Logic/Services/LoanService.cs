using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BookDB.Data.DbContexts;
using BookDB.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace BookDB.Logic.Services;

public sealed class LoanService : ILoanService
{
    private readonly IDbContextFactory<BookDbContext> _factory;

    public LoanService(IDbContextFactory<BookDbContext> factory)
    {
        _factory = factory;
    }

    public async Task CheckOutAsync(int bookId, int borrowerId, DateTime? dueDate, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var hasActiveLoan = await db.Loans
            .AnyAsync(l => l.BookId == bookId && l.ReturnedDate == null, ct);
        if (hasActiveLoan)
            throw new InvalidOperationException("Book already checked out.");

        var loan = new Loan
        {
            BookId = bookId,
            BorrowerId = borrowerId,
            LoanedDate = DateTime.UtcNow,
            DueDate = dueDate,
            ReturnedDate = null
        };
        db.Loans.Add(loan);
        await db.SaveChangesAsync(ct);
    }

    public async Task CheckInAsync(int bookId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var loan = await db.Loans
            .FirstOrDefaultAsync(l => l.BookId == bookId && l.ReturnedDate == null, ct);
        if (loan == null)
            throw new InvalidOperationException("No active loan for this book.");

        loan.ReturnedDate = DateTime.UtcNow;
        db.Loans.Update(loan);
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<LoanHistoryRow>> GetLoanHistoryAsync(int bookId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var loans = await db.Loans
            .Where(l => l.BookId == bookId)
            .Include(l => l.Borrower)
            .OrderByDescending(l => l.LoanedDate)
            .ToListAsync(ct);

        return loans.Select(l => new LoanHistoryRow(
            l.LoanId,
            (l.Borrower!.FirstName + " " + l.Borrower.LastName).Trim(),
            l.LoanedDate ?? DateTime.MinValue,
            l.DueDate,
            l.ReturnedDate
        )).ToList();
    }

    public async Task<(string DisplayName, DateTime? DueDate)?> GetActiveLoanAsync(int bookId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var loan = await db.Loans
            .Where(l => l.BookId == bookId && l.ReturnedDate == null)
            .Include(l => l.Borrower)
            .FirstOrDefaultAsync(ct);

        if (loan == null)
            return null;

        var displayName = (loan.Borrower!.FirstName + " " + loan.Borrower.LastName).Trim();
        return (displayName, loan.DueDate);
    }
}
