using System;
using BookDB.Desktop.Localization;
using BookDB.Logic.Services;

namespace BookDB.Desktop.ViewModels;

/// <summary>
/// Display row ViewModel for the Loan History DataGrid in FullDetailsWindow.
/// </summary>
public sealed class LoanHistoryRowViewModel
{
    public string BorrowerDisplayName { get; init; } = string.Empty;
    public string LoanedDate { get; init; } = string.Empty;
    public string DueDateDisplay { get; init; } = string.Empty;
    public string ReturnedDateDisplay { get; init; } = string.Empty;
    public string StatusDisplay { get; init; } = string.Empty;
    public bool IsActive { get; init; }
    public bool IsOverdue { get; init; }

    public static LoanHistoryRowViewModel FromLoanHistoryRow(LoanHistoryRow row)
    {
        bool isActive = row.ReturnedDate == null;
        bool isOverdue = isActive && row.DueDate.HasValue && row.DueDate.Value.Date < DateTime.UtcNow.Date;

        string loanedDate = row.LoanedDate.ToLocalTime().ToShortDateString();
        string dueDateDisplay = row.DueDate.HasValue
            ? row.DueDate.Value.ToLocalTime().ToShortDateString()
            : "—"; // em dash

        string returnedDateDisplay = isActive
            ? Resources.LoanHistory_Status_Active
            : row.ReturnedDate!.Value.ToLocalTime().ToShortDateString();

        string statusDisplay;
        if (!isActive)
            statusDisplay = Resources.LoanHistory_Status_Returned;
        else if (isOverdue)
            statusDisplay = Resources.LoanHistory_Status_Overdue;
        else
            statusDisplay = Resources.LoanHistory_Status_Active;

        return new LoanHistoryRowViewModel
        {
            BorrowerDisplayName = row.BorrowerDisplayName,
            LoanedDate = loanedDate,
            DueDateDisplay = dueDateDisplay,
            ReturnedDateDisplay = returnedDateDisplay,
            StatusDisplay = statusDisplay,
            IsActive = isActive,
            IsOverdue = isOverdue,
        };
    }
}
