using System.Collections.Generic;

namespace BookDB.Models.Entities;

public class BorrowerStatus
{
    public int BorrowerStatusId { get; set; }
    public string StatusName { get; set; } = string.Empty;

    public string? ResourceKey { get; set; }

    public ICollection<Borrower> Borrowers { get; set; } = [];
}
