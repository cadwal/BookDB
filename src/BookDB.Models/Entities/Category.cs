using System.Collections.Generic;
using BookDB.Models.Interfaces;

namespace BookDB.Models.Entities;

public class Category : INamedLookup
{
    int INamedLookup.Id => CategoryId;

    public int CategoryId { get; set; }

    public string Name { get; set; } = string.Empty;

    public int SortOrder { get; set; }

    public ICollection<BookCategory> BookCategories { get; set; } = [];

    public ICollection<CategoryCollection> CategoryCollections { get; set; } = [];
}
