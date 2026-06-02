using System.Collections.Generic;

namespace BookDB.Models.Entities;

public class BookImageType
{
    public int BookImageTypeId { get; set; }
    public string TypeName { get; set; } = string.Empty;

    public string? ResourceKey { get; set; }

    public ICollection<BookImage> Images { get; set; } = [];
}
