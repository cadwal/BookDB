using System.Collections.Generic;
using BookDB.Models.Interfaces;

namespace BookDB.Models.Entities;

public class Location : INamedLookup
{
    public int LocationId { get; set; }

    public string Name { get; set; } = string.Empty;

    public ICollection<Book> Books { get; set; } = [];

    int INamedLookup.Id => LocationId;
}
