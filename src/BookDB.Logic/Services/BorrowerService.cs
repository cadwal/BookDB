using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BookDB.Data.DbContexts;
using BookDB.Models.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace BookDB.Logic.Services;

public sealed class BorrowerService : IBorrowerService
{
    private readonly IDbContextFactory<BookDbContext> _factory;

    public BorrowerService(IDbContextFactory<BookDbContext> factory)
    {
        _factory = factory;
    }

    public async Task<IReadOnlyList<Borrower>> GetAllAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Borrowers
            .OrderBy(b => b.LastName)
            .ThenBy(b => b.FirstName)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Borrower>> SearchAsync(string text, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var pattern = $"%{text}%";
        return await db.Borrowers
            .Where(b =>
                EF.Functions.Like(b.FirstName + " " + b.LastName, pattern) ||
                EF.Functions.Like(b.LastName + ", " + b.FirstName, pattern))
            .OrderBy(b => b.LastName).ThenBy(b => b.FirstName)
            .Take(20)
            .ToListAsync(ct);
    }

    public async Task<Borrower> CreateAsync(string firstName, string? lastName = null, int statusId = 0, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var borrower = new Borrower
        {
            FirstName = firstName,
            LastName = lastName,
            StatusId = statusId
        };
        db.Borrowers.Add(borrower);
        await db.SaveChangesAsync(ct);
        return borrower;
    }

    public async Task SaveAsync(Borrower borrower, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        if (borrower.BorrowerId == 0)
            db.Borrowers.Add(borrower);
        else
            db.Borrowers.Update(borrower);
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(int borrowerId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var borrower = await db.Borrowers.FindAsync([borrowerId], ct);
        if (borrower == null) return;
        db.Borrowers.Remove(borrower);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqliteException { SqliteErrorCode: 19 })
        {
            throw new InvalidOperationException("Cannot delete: this borrower has loan history.", ex);
        }
    }
}
