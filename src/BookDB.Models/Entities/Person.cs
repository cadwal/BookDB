using System.Collections.Generic;

namespace BookDB.Models.Entities;

public class Person
{
    public int PersonId { get; set; }

    public string DisplayName { get; set; } = string.Empty;

    public string SortName { get; set; } = string.Empty;

    public string? Bio { get; set; }
    public string? BirthDate { get; set; }
    public string? BirthPlace { get; set; }
    public string? DeathDate { get; set; }
    public string? DeathPlace { get; set; }
    public string? Website { get; set; }

    public ICollection<BookContributor> BookContributions { get; set; } = [];
}
