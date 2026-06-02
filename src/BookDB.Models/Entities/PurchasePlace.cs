using System.Collections.Generic;
using BookDB.Models.Interfaces;

namespace BookDB.Models.Entities;

public class PurchasePlace : INamedLookup
{
    int INamedLookup.Id => PurchasePlaceId;

    public int PurchasePlaceId { get; set; }

    public string Name { get; set; } = string.Empty;

    public ICollection<Book> Books { get; set; } = [];
}
