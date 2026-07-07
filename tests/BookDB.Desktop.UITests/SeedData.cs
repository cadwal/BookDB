using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BookDB.Logic.Services;
using BookDB.Models.Entities;

namespace BookDB.Desktop.UITests;

/// <summary>
/// Builds known fixtures through the app's own Logic write paths (not hand-rolled SQL), so seeded data matches
/// what the UI would produce. Each test seeds exactly what it asserts on.
/// </summary>
public static class SeedData
{
    public static Task<Book> AddBookAsync(TestHost host, string title, CancellationToken ct = default) =>
        host.Resolve<IBookService>().AddBookAsync(new Book { Title = title }, ct);

    public static Task<Book> AddBookAsync(TestHost host, string title, string isbn, CancellationToken ct = default) =>
        host.Resolve<IBookService>().AddBookAsync(new Book { Title = title, Isbn = isbn }, ct);

    public static Task<Book> AddBookAsync(
        TestHost host, string title, IReadOnlyList<string> authors, CancellationToken ct = default) =>
        host.Resolve<IBookService>().AddBookWithContributorsAsync(new Book { Title = title }, authors, ct);

    public static Task<Borrower> AddBorrowerAsync(
        TestHost host, string firstName, string? lastName = null, CancellationToken ct = default) =>
        host.Resolve<IBorrowerService>().CreateAsync(firstName, lastName, ct: ct);

    public static Task<Collection> AddCollectionAsync(TestHost host, string name, CancellationToken ct = default) =>
        host.Resolve<ILookupService>().AddAsync(new Collection { Name = name }, ct);
}
