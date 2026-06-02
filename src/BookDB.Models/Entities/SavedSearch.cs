using System;

namespace BookDB.Models.Entities;

public class SavedSearch
{
    public int SavedSearchId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string QueryJson { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
