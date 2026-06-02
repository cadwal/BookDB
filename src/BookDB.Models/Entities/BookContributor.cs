namespace BookDB.Models.Entities;

public class BookContributor
{
    public int BookContributorId { get; set; }

    public int BookId { get; set; }

    public int PersonId { get; set; }

    public int ContributorRoleId { get; set; }

    public int SortOrder { get; set; }

    public Book? Book { get; set; }

    public Person? Person { get; set; }

    public ContributorRole? ContributorRole { get; set; }
}
