namespace BookDB.Models.Entities;

public class CategoryCollection
{
    public int CategoryId { get; set; }

    public int CollectionId { get; set; }

    public Category? Category { get; set; }

    public Collection? Collection { get; set; }
}
