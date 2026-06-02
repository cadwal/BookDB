using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BookDB.Models.Entities;

namespace BookDB.Logic.Services;

public interface IBorrowerService
{
    Task<IReadOnlyList<Borrower>> GetAllAsync(CancellationToken ct = default);
    Task<IReadOnlyList<Borrower>> SearchAsync(string text, CancellationToken ct = default);
    Task<Borrower> CreateAsync(string firstName, string? lastName = null, int statusId = 0, CancellationToken ct = default);
    Task SaveAsync(Borrower borrower, CancellationToken ct = default);
    Task DeleteAsync(int borrowerId, CancellationToken ct = default);
}
