using System.Threading;
using System.Threading.Tasks;
using BookDB.Data.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace BookDB.Logic.Tests;

/// <summary>
/// Shared IDbContextFactory implementation for logic layer tests.
/// Creates a fresh context from the given options for each test.
/// </summary>
internal sealed class TestBookDbContextFactory(DbContextOptions<BookDbContext> options) : IDbContextFactory<BookDbContext>
{
    private readonly DbContextOptions<BookDbContext> _options = options;

    public BookDbContext CreateDbContext() => new(_options);

    public Task<BookDbContext> CreateDbContextAsync(CancellationToken ct = default)
        => Task.FromResult(new BookDbContext(_options));
}
