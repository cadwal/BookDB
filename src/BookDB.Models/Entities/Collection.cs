using System.Collections.Generic;
using BookDB.Models.Interfaces;

namespace BookDB.Models.Entities;

public class Collection : INamedLookup
{
    public int CollectionId { get; set; }

    public string Name { get; set; } = string.Empty;

    public int SortOrder { get; set; }

    public ICollection<Book> Books { get; set; } = [];

    public ICollection<CategoryCollection> CategoryCollections { get; set; } = [];

    int INamedLookup.Id => CollectionId;
}
