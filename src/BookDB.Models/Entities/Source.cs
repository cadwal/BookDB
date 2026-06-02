using System.Collections.Generic;

namespace BookDB.Models.Entities;

public class Source
{
    public int SourceId { get; set; }

    public string Name { get; set; } = string.Empty;

    public ICollection<Book> Books { get; set; } = [];
}
