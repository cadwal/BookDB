using System.Collections.Generic;

namespace BookDB.Models.Entities;

public class ContributorRole
{
    public int ContributorRoleId { get; set; }

    public string Code { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string? ResourceKey { get; set; }

    public ICollection<BookContributor> BookContributors { get; set; } = [];
}
