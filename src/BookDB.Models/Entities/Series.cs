using System.Collections.Generic;
using BookDB.Models.Interfaces;

namespace BookDB.Models.Entities;

public class Series : INamedLookup
{
    public int SeriesId { get; set; }

    public string Name { get; set; } = string.Empty;

    public ICollection<Book> Books { get; set; } = [];

    int INamedLookup.Id => SeriesId;
}
