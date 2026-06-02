using System.Collections.Generic;
using BookDB.Models.Interfaces;

namespace BookDB.Models.Entities;

public class Publisher : INamedLookup
{
    public int PublisherId { get; set; }

    public string Name { get; set; } = string.Empty;

    public ICollection<Book> Books { get; set; } = [];

    int INamedLookup.Id => PublisherId;
}
