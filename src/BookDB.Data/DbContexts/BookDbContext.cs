using BookDB.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace BookDB.Data.DbContexts;

public class BookDbContext(DbContextOptions<BookDbContext> options) : DbContext(options)
{
    public DbSet<Book> Books { get; set; } = null!;
    public DbSet<Person> People { get; set; } = null!;
    public DbSet<Collection> Collections { get; set; } = null!;
    public DbSet<BookContributor> BookContributors { get; set; } = null!;
    public DbSet<BookCategory> BookCategories { get; set; } = null!;
    public DbSet<CategoryCollection> CategoryCollections { get; set; } = null!;
    public DbSet<ContributorRole> ContributorRoles { get; set; } = null!;
    public DbSet<Publisher> Publishers { get; set; } = null!;
    public DbSet<Series> Series { get; set; } = null!;
    public DbSet<Category> Categories { get; set; } = null!;
    public DbSet<Settings> Settings { get; set; } = null!;
    public DbSet<Condition> Conditions { get; set; } = null!;
    public DbSet<Edition> Editions { get; set; } = null!;
    public DbSet<Format> Formats { get; set; } = null!;
    public DbSet<Language> Languages { get; set; } = null!;
    public DbSet<Location> Locations { get; set; } = null!;
    public DbSet<Owner> Owners { get; set; } = null!;
    public DbSet<PurchasePlace> PurchasePlaces { get; set; } = null!;
    public DbSet<Rating> Ratings { get; set; } = null!;
    public DbSet<ReadingLevel> ReadingLevels { get; set; } = null!;
    public DbSet<Source> Sources { get; set; } = null!;
    public DbSet<Status> Statuses { get; set; } = null!;
    public DbSet<SavedSearch> SavedSearches { get; set; } = null!;
    public DbSet<BatchQueueItem> BatchQueueItems { get; set; } = null!;
    public DbSet<BookImage> BookImages { get; set; } = null!;
    public DbSet<BookImageType> BookImageTypes { get; set; } = null!;
    public DbSet<BookVolume> BookVolumes { get; set; } = null!;
    public DbSet<BookChapter> BookChapters { get; set; } = null!;
    public DbSet<BorrowerStatus> BorrowerStatuses { get; set; } = null!;
    public DbSet<Borrower> Borrowers { get; set; } = null!;
    public DbSet<Loan> Loans { get; set; } = null!;
    public DbSet<ClientSession> ClientSessions { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Map every entity to its singular CLR type name to match DbUp DDL table names
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            entityType.SetTableName(entityType.ClrType.Name);
        }

        modelBuilder.Entity<BookCategory>()
            .HasKey(bc => new { bc.BookId, bc.CategoryId });

        modelBuilder.Entity<CategoryCollection>()
            .HasKey(cc => new { cc.CategoryId, cc.CollectionId });

        modelBuilder.Entity<Settings>()
            .HasKey(s => s.Key);

        // SessionId is neither "Id" nor "ClientSessionId", so the key needs to be stated explicitly.
        modelBuilder.Entity<ClientSession>()
            .HasKey(cs => cs.SessionId);

        modelBuilder.Entity<BookImage>()
            .HasOne(bi => bi.BookImageType)
            .WithMany(bit => bit.Images)
            .HasForeignKey(bi => bi.BookImageTypeId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Borrower>()
            .HasOne(b => b.BorrowerStatus)
            .WithMany(bs => bs.Borrowers)
            .HasForeignKey(b => b.StatusId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
