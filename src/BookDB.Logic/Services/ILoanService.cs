using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BookDB.Logic.Services;

public record LoanHistoryRow(
    int LoanId,
    string BorrowerDisplayName,
    DateTime LoanedDate,
    DateTime? DueDate,
    DateTime? ReturnedDate);

public interface ILoanService
{
    Task CheckOutAsync(int bookId, int borrowerId, DateTime? dueDate, CancellationToken ct = default);
    Task CheckInAsync(int bookId, CancellationToken ct = default);
    Task<IReadOnlyList<LoanHistoryRow>> GetLoanHistoryAsync(int bookId, CancellationToken ct = default);
    Task<(string DisplayName, DateTime? DueDate)?> GetActiveLoanAsync(int bookId, CancellationToken ct = default);
}
