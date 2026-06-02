using System.Collections.Generic;

namespace BookDB.Models.Entities;

public class Status
{
    public int StatusId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? ResourceKey { get; set; }

    public ICollection<Book> Books { get; set; } = [];
}
