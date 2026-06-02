using System;

namespace BookDB.Models.Entities;

public class Loan
{
    public int LoanId { get; set; }
    public int BookId { get; set; }
    public int BorrowerId { get; set; }
    public DateTime? LoanedDate { get; set; }
    public DateTime? DueDate { get; set; }
    public DateTime? ReturnedDate { get; set; }
    public string? LoanExternalId { get; set; }
    public Book? Book { get; set; }
    public Borrower? Borrower { get; set; }
}
