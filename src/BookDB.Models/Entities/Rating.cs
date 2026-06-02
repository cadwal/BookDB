using System.Collections.Generic;

namespace BookDB.Models.Entities;

public class Rating
{
    public int RatingId { get; set; }

    public string Name { get; set; } = string.Empty;

    public double? NumericValue { get; set; }

    public string? ResourceKey { get; set; }

    public ICollection<Book> Books { get; set; } = [];
}
