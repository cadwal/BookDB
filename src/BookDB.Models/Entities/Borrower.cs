using System.Collections.Generic;

namespace BookDB.Models.Entities;

public class Borrower
{
    public int BorrowerId { get; set; }
    public int StatusId { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? BorrowerExternalId { get; set; }
    public string? Organization { get; set; }
    public string? Address1 { get; set; }
    public string? Address2 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public string? Country { get; set; }
    public string? Phone1 { get; set; }
    public string? Phone2 { get; set; }
    public string? Email { get; set; }
    public string? Fax { get; set; }
    public BorrowerStatus? BorrowerStatus { get; set; }
    public ICollection<Loan> Loans { get; set; } = [];

    /// <summary>
    /// Display name derived from first/last name or organization.
    /// Used by ManageBorrowersWindow ListBox ItemTemplate.
    /// </summary>
    public string DisplayName =>
        ((FirstName ?? "") + " " + (LastName ?? "")).Trim() is { Length: > 0 } name
            ? name
            : Organization ?? string.Empty;
}
