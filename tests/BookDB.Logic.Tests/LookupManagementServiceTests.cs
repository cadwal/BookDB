using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using BookDB.Data;
using BookDB.Data.DbContexts;
using BookDB.Logic.Services;
using BookDB.Models.Entities;
using DbUp;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BookDB.Logic.Tests;

public sealed class LookupManagementServiceTests : IDisposable
{
    private readonly string _dbPath;
    private readonly TestBookDbContextFactory _factory;
    private readonly LookupManagementService _sut;

    public LookupManagementServiceTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"bookdb_lookupmgmt_test_{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={_dbPath}";

        var upgrader = SqliteExtensions.SqliteDatabase(DeployChanges.To, connectionString)
            .WithScriptsEmbeddedInAssembly(
                Assembly.GetAssembly(typeof(BookDB.Data.Sqlite.SqliteDbUpRunner))!,
                name => name.Contains(".Migrations."))
            .LogToNowhere()
            .Build();
        var result = upgrader.PerformUpgrade();
        if (!result.Successful) throw new InvalidOperationException($"Migration failed: {result.Error}");

        var options = new DbContextOptionsBuilder<BookDbContext>()
            .UseSqlite(connectionString)
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
            .Options;
        _factory = new TestBookDbContextFactory(options);
        _sut = new LookupManagementService(_factory, new BookDB.Data.Sqlite.SqliteLookupNameMatcher());
    }

    public void Dispose()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        try { File.Delete(_dbPath); } catch { /* best effort */ }
    }

    // -----------------------------------------------------------------------
    // Case-insensitive duplicate detection
    // -----------------------------------------------------------------------

    [Fact]
    public async Task AddCollectionAsync_RejectsCaseInsensitiveDuplicate()
    {
        var ct = TestContext.Current.CancellationToken;
        await _sut.AddCollectionAsync("MyShelf", ct);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.AddCollectionAsync("myshelf", ct));
    }

    [Fact]
    public async Task AddPublisherAsync_RejectsCaseInsensitiveDuplicate()
    {
        var ct = TestContext.Current.CancellationToken;
        await _sut.AddPublisherAsync("Penguin", ct);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.AddPublisherAsync("PENGUIN", ct));
    }

    [Fact]
    public async Task AddPublisherAsync_RejectsCaseInsensitiveDuplicate_WithNonAsciiUppercase()
    {
        // "ÅSA" and "Åsa" differ only in ASCII case. NOCASE folds the ASCII letters (Å stays Å on
        // both sides) and treats them as the same name. A ToLower() comparison would miss this on
        // SQLite: lower() leaves the column's Å alone while C# folds the parameter's Å to å.
        var ct = TestContext.Current.CancellationToken;
        await _sut.AddPublisherAsync("ÅSA", ct);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.AddPublisherAsync("Åsa", ct));
    }

    [Fact]
    public async Task RenamePublisherAsync_RejectsRenameToCaseInsensitiveDuplicate()
    {
        var ct = TestContext.Current.CancellationToken;
        await _sut.AddPublisherAsync("Penguin", ct);
        var vintageId = await _sut.AddPublisherAsync("Vintage", ct);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.RenamePublisherAsync(vintageId, "penguin", ct));
    }

    // -----------------------------------------------------------------------
    // Helper: seed using a fresh tracking context
    // -----------------------------------------------------------------------

    private BookDbContext GetTrackingContext()
    {
        var opts = new DbContextOptionsBuilder<BookDbContext>()
            .UseSqlite($"Data Source={_dbPath}")
            .Options;
        return new BookDbContext(opts);
    }

    private async Task<int> SeedPublisherAsync(string name, CancellationToken ct)
    {
        await using var db = GetTrackingContext();
        var pub = new Publisher { Name = name };
        db.Publishers.Add(pub);
        await db.SaveChangesAsync(ct);
        return pub.PublisherId;
    }

    private async Task<int> SeedSeriesAsync(string name, CancellationToken ct)
    {
        await using var db = GetTrackingContext();
        var s = new Series { Name = name };
        db.Series.Add(s);
        await db.SaveChangesAsync(ct);
        return s.SeriesId;
    }

    private async Task<int> SeedPersonAsync(string displayName, CancellationToken ct, string sortName = "")
    {
        await using var db = GetTrackingContext();
        var p = new Person { DisplayName = displayName, SortName = string.IsNullOrEmpty(sortName) ? displayName : sortName };
        db.People.Add(p);
        await db.SaveChangesAsync(ct);
        return p.PersonId;
    }

    private async Task<int> SeedContributorRoleAsync(CancellationToken ct)
    {
        await using var db = GetTrackingContext();
        var existing = await db.ContributorRoles.FirstOrDefaultAsync(r => r.Code == "Author", ct);
        if (existing is not null) return existing.ContributorRoleId;
        var role = new ContributorRole { Code = "Author", DisplayName = "Author" };
        db.ContributorRoles.Add(role);
        await db.SaveChangesAsync(ct);
        return role.ContributorRoleId;
    }

    private async Task<int> SeedBookAsync(CancellationToken ct, int? publisherId = null, int? seriesId = null,
        int? locationId = null, int? ownerId = null, int? languageId = null, string title = "Test Book")
    {
        await using var db = GetTrackingContext();
        var book = new Book
        {
            Title = title,
            PublisherId = publisherId,
            SeriesId = seriesId,
            LocationId = locationId,
            OwnerId = ownerId,
            LanguageId = languageId
        };
        db.Books.Add(book);
        await db.SaveChangesAsync(ct);
        return book.BookId;
    }

    private async Task SeedBookContributorAsync(int bookId, int personId, int roleId, CancellationToken ct)
    {
        await using var db = GetTrackingContext();
        var bc = new BookContributor { BookId = bookId, PersonId = personId, ContributorRoleId = roleId, SortOrder = 1 };
        db.BookContributors.Add(bc);
        await db.SaveChangesAsync(ct);
    }

    // -----------------------------------------------------------------------
    // Count tests
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetPublisherBookCountAsync_ReturnsCorrectCount()
    {
        var ct = TestContext.Current.CancellationToken;
        var pubId = await SeedPublisherAsync("Penguin", ct);
        await SeedBookAsync(ct, publisherId: pubId, title: "Book 1");
        await SeedBookAsync(ct, publisherId: pubId, title: "Book 2");
        await SeedBookAsync(ct, publisherId: pubId, title: "Book 3");

        var count = await _sut.GetPublisherBookCountAsync(pubId, ct);

        Assert.Equal(3, count);
    }

    [Fact]
    public async Task GetPublisherBookCountAsync_UnusedPublisher_ReturnsZero()
    {
        var ct = TestContext.Current.CancellationToken;
        var pubId = await SeedPublisherAsync("Unused", ct);
        var count = await _sut.GetPublisherBookCountAsync(pubId, ct);
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task GetPersonBookContributionCountAsync_ReturnsCorrectCount()
    {
        var ct = TestContext.Current.CancellationToken;
        var personId = await SeedPersonAsync("Stephen King", ct);
        var roleId = await SeedContributorRoleAsync(ct);
        for (var i = 0; i < 5; i++)
        {
            var bookId = await SeedBookAsync(ct, title: $"Book {i}");
            await SeedBookContributorAsync(bookId, personId, roleId, ct);
        }

        var count = await _sut.GetPersonBookContributionCountAsync(personId, ct);

        Assert.Equal(5, count);
    }

    // -----------------------------------------------------------------------
    // Rename tests
    // -----------------------------------------------------------------------

    [Fact]
    public async Task RenamePublisherAsync_UpdatesName()
    {
        var ct = TestContext.Current.CancellationToken;
        var pubId = await SeedPublisherAsync("Old Name", ct);

        await _sut.RenamePublisherAsync(pubId, "New Name", ct);

        await using var db = GetTrackingContext();
        var pub = await db.Publishers.FindAsync([pubId], ct);
        Assert.Equal("New Name", pub!.Name);
    }

    [Fact]
    public async Task RenamePublisherAsync_DuplicateName_Throws()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedPublisherAsync("Existing", ct);
        var pubId = await SeedPublisherAsync("Other", ct);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.RenamePublisherAsync(pubId, "Existing", ct));
        Assert.Contains("Publisher", ex.Message);
        Assert.Contains("already exists", ex.Message);
    }

    [Fact]
    public async Task RenamePublisherAsync_DuplicateNameCaseInsensitive_Throws()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedPublisherAsync("Existing", ct);
        var pubId = await SeedPublisherAsync("Other", ct);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.RenamePublisherAsync(pubId, "EXISTING", ct));
    }

    [Fact]
    public async Task RenamePublisherAsync_EmptyName_Throws()
    {
        var ct = TestContext.Current.CancellationToken;
        var pubId = await SeedPublisherAsync("Some Publisher", ct);

        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => _sut.RenamePublisherAsync(pubId, "", ct));
        Assert.Contains("empty", ex.Message);
    }

    [Fact]
    public async Task RenamePublisherAsync_WhitespaceName_Throws()
    {
        var ct = TestContext.Current.CancellationToken;
        var pubId = await SeedPublisherAsync("Some Publisher", ct);

        await Assert.ThrowsAsync<ArgumentException>(
            () => _sut.RenamePublisherAsync(pubId, "   ", ct));
    }

    // -----------------------------------------------------------------------
    // Delete tests
    // -----------------------------------------------------------------------

    [Fact]
    public async Task DeletePublisherAsync_WhenUnreferenced_RemovesRow()
    {
        var ct = TestContext.Current.CancellationToken;
        var pubId = await SeedPublisherAsync("Unreferenced", ct);

        await _sut.DeletePublisherAsync(pubId, ct);

        await using var db = GetTrackingContext();
        var pub = await db.Publishers.FindAsync([pubId], ct);
        Assert.Null(pub);
    }

    [Fact]
    public async Task DeletePublisherAsync_WhenReferenced_Throws()
    {
        var ct = TestContext.Current.CancellationToken;
        var pubId = await SeedPublisherAsync("Used Publisher", ct);
        await SeedBookAsync(ct, publisherId: pubId);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.DeletePublisherAsync(pubId, ct));
        Assert.Contains("Cannot delete Publisher", ex.Message);
        Assert.Contains("1 books", ex.Message);
    }

    [Fact]
    public async Task DeletePersonAsync_WhenUnreferenced_RemovesRow()
    {
        var ct = TestContext.Current.CancellationToken;
        var personId = await SeedPersonAsync("Alice", ct);

        await _sut.DeletePersonAsync(personId, ct);

        await using var db = GetTrackingContext();
        var person = await db.People.FindAsync([personId], ct);
        Assert.Null(person);
    }

    [Fact]
    public async Task DeletePersonAsync_WhenReferenced_Throws()
    {
        var ct = TestContext.Current.CancellationToken;
        var personId = await SeedPersonAsync("Bob", ct);
        var roleId = await SeedContributorRoleAsync(ct);
        var bookId = await SeedBookAsync(ct, title: "Bob's Book");
        await SeedBookContributorAsync(bookId, personId, roleId, ct);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.DeletePersonAsync(personId, ct));
        Assert.Contains("Cannot delete Person", ex.Message);
        Assert.Contains("1 books", ex.Message);
    }

    // -----------------------------------------------------------------------
    // Merge tests
    // -----------------------------------------------------------------------

    [Fact]
    public async Task MergePublishersAsync_RepointsAllBooksAndDeletesSource()
    {
        var ct = TestContext.Current.CancellationToken;
        var sourceId = await SeedPublisherAsync("Source Publisher", ct);
        var targetId = await SeedPublisherAsync("Target Publisher", ct);
        await SeedBookAsync(ct, publisherId: sourceId, title: "Book A");
        await SeedBookAsync(ct, publisherId: sourceId, title: "Book B");

        await _sut.MergePublishersAsync(sourceId, targetId, ct);

        await using var db = GetTrackingContext();
        var sourceExists = await db.Publishers.AnyAsync(p => p.PublisherId == sourceId, ct);
        var booksAtTarget = await db.Books.CountAsync(b => b.PublisherId == targetId, ct);
        Assert.False(sourceExists);
        Assert.Equal(2, booksAtTarget);
    }

    [Fact]
    public async Task MergePublishersAsync_SourceEqualsTarget_NoOp()
    {
        var ct = TestContext.Current.CancellationToken;
        var pubId = await SeedPublisherAsync("Same Publisher", ct);
        await SeedBookAsync(ct, publisherId: pubId, title: "Test Book");

        await _sut.MergePublishersAsync(pubId, pubId, ct);

        await using var db = GetTrackingContext();
        var pub = await db.Publishers.FindAsync([pubId], ct);
        Assert.NotNull(pub);
    }

    [Fact]
    public async Task MergePersonsAsync_RepointsContributorsAndDeletesSource()
    {
        var ct = TestContext.Current.CancellationToken;
        var sourceId = await SeedPersonAsync("Source Person", ct);
        var targetId = await SeedPersonAsync("Target Person", ct);
        var roleId = await SeedContributorRoleAsync(ct);
        var bookId = await SeedBookAsync(ct, title: "Contributed Book");
        await SeedBookContributorAsync(bookId, sourceId, roleId, ct);

        await _sut.MergePersonsAsync(sourceId, targetId, ct);

        await using var db = GetTrackingContext();
        var sourceExists = await db.People.AnyAsync(p => p.PersonId == sourceId, ct);
        var targetContrib = await db.BookContributors.CountAsync(bc => bc.PersonId == targetId && bc.BookId == bookId, ct);
        Assert.False(sourceExists);
        Assert.Equal(1, targetContrib);
    }

    [Fact]
    public async Task MergePersonsAsync_DeduplicatesOnRoleCollision()
    {
        var ct = TestContext.Current.CancellationToken;
        var sourceId = await SeedPersonAsync("Source Author", ct);
        var targetId = await SeedPersonAsync("Target Author", ct);
        var roleId = await SeedContributorRoleAsync(ct);
        var bookId = await SeedBookAsync(ct, title: "Shared Book");
        // Both source and target are already authors on the same book
        await SeedBookContributorAsync(bookId, sourceId, roleId, ct);
        await SeedBookContributorAsync(bookId, targetId, roleId, ct);

        await _sut.MergePersonsAsync(sourceId, targetId, ct);

        await using var db = GetTrackingContext();
        var rowCount = await db.BookContributors.CountAsync(
            bc => bc.PersonId == targetId && bc.BookId == bookId && bc.ContributorRoleId == roleId, ct);
        Assert.Equal(1, rowCount);
    }

    [Fact]
    public async Task MergePersonsAsync_SourceEqualsTarget_NoOp()
    {
        var ct = TestContext.Current.CancellationToken;
        var personId = await SeedPersonAsync("Same Person", ct);

        await _sut.MergePersonsAsync(personId, personId, ct);

        await using var db = GetTrackingContext();
        var person = await db.People.FindAsync([personId], ct);
        Assert.NotNull(person);
    }

    // -----------------------------------------------------------------------
    // Cleanup scan/apply tests
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ScanPersonNameCleanupAsync_ExcludesUnchangedPersons()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedPersonAsync("Alice", ct);
        await SeedPersonAsync("by Alice.", ct);

        var (proposals, _) = await _sut.ScanPersonNameCleanupAsync(ct);

        Assert.Single(proposals);
        Assert.Equal("by Alice.", proposals[0].CurrentDisplayName);
        Assert.Equal("Alice", proposals[0].ProposedDisplayName);
    }

    [Fact]
    public async Task ScanPersonNameCleanupAsync_GeneratesCorrectSortName()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedPersonAsync("by Stephen King.", ct);

        var (proposals, _) = await _sut.ScanPersonNameCleanupAsync(ct);

        Assert.Single(proposals);
        Assert.Equal("King, Stephen", proposals[0].SuggestedSortName);
    }

    [Fact]
    public async Task ApplyPersonNameCleanupAsync_UpdatesDisplayNameAndSortName()
    {
        var ct = TestContext.Current.CancellationToken;
        var personId = await SeedPersonAsync("by Jane Smith (editor).", ct);

        var (proposals, _) = await _sut.ScanPersonNameCleanupAsync(ct);
        await _sut.ApplyPersonNameCleanupAsync(proposals, ct);

        await using var db = GetTrackingContext();
        var person = await db.People.FindAsync([personId], ct);
        Assert.Equal("Jane Smith", person!.DisplayName);
        Assert.Equal("Smith, Jane", person.SortName);
    }

    [Fact]
    public async Task ApplyPersonNameCleanupAsync_UnlistedPersonUntouched()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedPersonAsync("Alice", ct);
        await SeedPersonAsync("by Jane Smith.", ct);

        var (proposals, _) = await _sut.ScanPersonNameCleanupAsync(ct);
        var filtered = proposals.Where(p => p.CurrentDisplayName == "by Jane Smith.").ToList().AsReadOnly();
        await _sut.ApplyPersonNameCleanupAsync(filtered, ct);

        await using var db = GetTrackingContext();
        var alice = await db.People.FirstOrDefaultAsync(p => p.DisplayName == "Alice", ct);
        Assert.NotNull(alice);
    }

    // -----------------------------------------------------------------------
    // Gap 3: ScanPersonNameCleanupAsync split detection
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ScanPersonNameCleanupAsync_SlashSeparator_ReturnsSplitProposal()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedPersonAsync("Smith, John / Jones, Mary", ct);

        var (renames, splits) = await _sut.ScanPersonNameCleanupAsync(ct);

        Assert.Single(splits);
        Assert.Equal("Smith, John / Jones, Mary", splits[0].CurrentDisplayName);
        Assert.Equal(2, splits[0].Fragments.Count);
        Assert.Contains(splits[0].Fragments, f => f.ProposedDisplayName == "Smith, John");
        Assert.Contains(splits[0].Fragments, f => f.ProposedDisplayName == "Jones, Mary");
    }

    [Fact]
    public async Task ScanPersonNameCleanupAsync_SemicolonSeparator_ReturnsSplitProposal()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedPersonAsync("Alice; Bob", ct);

        var (_, splits) = await _sut.ScanPersonNameCleanupAsync(ct);

        Assert.Single(splits);
        Assert.Equal(2, splits[0].Fragments.Count);
        Assert.Contains(splits[0].Fragments, f => f.ProposedDisplayName == "Alice");
        Assert.Contains(splits[0].Fragments, f => f.ProposedDisplayName == "Bob");
    }

    [Fact]
    public async Task ScanPersonNameCleanupAsync_PipeSeparator_ReturnsSplitProposal()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedPersonAsync("Alice | Bob", ct);

        var (_, splits) = await _sut.ScanPersonNameCleanupAsync(ct);

        Assert.Single(splits);
        Assert.Equal(2, splits[0].Fragments.Count);
        Assert.Contains(splits[0].Fragments, f => f.ProposedDisplayName == "Alice");
        Assert.Contains(splits[0].Fragments, f => f.ProposedDisplayName == "Bob");
    }

    [Fact]
    public async Task ScanPersonNameCleanupAsync_SquishedPersonNotReturnedAsRename()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedPersonAsync("Smith, John / Jones, Mary", ct);

        var (renames, splits) = await _sut.ScanPersonNameCleanupAsync(ct);

        Assert.Empty(renames);
        Assert.Single(splits);
    }

    [Fact]
    public async Task ScanPersonNameCleanupAsync_FragmentsHaveCleanedNamesAndSortNames()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedPersonAsync("Smith, John / by Jones, Mary.", ct);

        var (_, splits) = await _sut.ScanPersonNameCleanupAsync(ct);

        Assert.Single(splits);
        var fragments = splits[0].Fragments;
        var jonesFragment = fragments.FirstOrDefault(f => f.ProposedDisplayName == "Jones, Mary");
        Assert.NotNull(jonesFragment);
        var smithFragment = fragments.FirstOrDefault(f => f.ProposedDisplayName == "Smith, John");
        Assert.NotNull(smithFragment);
        Assert.Equal("Smith, John", smithFragment!.SuggestedSortName);
    }

    // -----------------------------------------------------------------------
    // Gap 4: ApplySplitProposalAsync
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ApplySplitProposalAsync_DeletesOriginalPersonAndCreatesNewPersons()
    {
        var ct = TestContext.Current.CancellationToken;
        var originalId = await SeedPersonAsync("Smith, John / Jones, Mary", ct);

        var proposal = new SplitProposal(
            originalId,
            "Smith, John / Jones, Mary",
            new[]
            {
                new SplitFragment("Smith, John", "Smith, John"),
                new SplitFragment("Jones, Mary", "Jones, Mary")
            });

        await _sut.ApplySplitProposalAsync([proposal], ct);

        await using var db = GetTrackingContext();
        var original = await db.People.FindAsync([originalId], ct);
        Assert.Null(original);

        var smithExists = await db.People.AnyAsync(p => p.DisplayName == "Smith, John", ct);
        var jonesExists = await db.People.AnyAsync(p => p.DisplayName == "Jones, Mary", ct);
        Assert.True(smithExists);
        Assert.True(jonesExists);
    }

    [Fact]
    public async Task ApplySplitProposalAsync_RepointsBookContributorsToNewPersons()
    {
        var ct = TestContext.Current.CancellationToken;
        var originalId = await SeedPersonAsync("Smith, John / Jones, Mary", ct);
        var roleId = await SeedContributorRoleAsync(ct);
        var bookId = await SeedBookAsync(ct, title: "Shared Book");
        await SeedBookContributorAsync(bookId, originalId, roleId, ct);

        var proposal = new SplitProposal(
            originalId,
            "Smith, John / Jones, Mary",
            new[]
            {
                new SplitFragment("Smith, John", "Smith, John"),
                new SplitFragment("Jones, Mary", "Jones, Mary")
            });

        await _sut.ApplySplitProposalAsync([proposal], ct);

        await using var db = GetTrackingContext();
        var originalContribs = await db.BookContributors.CountAsync(bc => bc.PersonId == originalId, ct);
        Assert.Equal(0, originalContribs);

        var smithId = (await db.People.FirstAsync(p => p.DisplayName == "Smith, John", ct)).PersonId;
        var jonesId = (await db.People.FirstAsync(p => p.DisplayName == "Jones, Mary", ct)).PersonId;

        var smithContrib = await db.BookContributors.CountAsync(bc => bc.PersonId == smithId && bc.BookId == bookId, ct);
        var jonesContrib = await db.BookContributors.CountAsync(bc => bc.PersonId == jonesId && bc.BookId == bookId, ct);
        Assert.Equal(1, smithContrib);
        Assert.Equal(1, jonesContrib);
    }

    // -----------------------------------------------------------------------
    // UpdatePersonBioAsync tests
    // -----------------------------------------------------------------------

    [Fact]
    public async Task UpdatePersonBioAsync_PersistsAllSixFields()
    {
        var ct = TestContext.Current.CancellationToken;
        var personId = await SeedPersonAsync("Bio Author", ct);

        await _sut.UpdatePersonBioAsync(
            personId,
            bio: "A novelist.",
            birthDate: "1970-01-01",
            birthPlace: "London",
            deathDate: null,
            deathPlace: null,
            website: "https://example.com",
            ct);

        await using var db = GetTrackingContext();
        var person = await db.People.FindAsync([personId], ct);
        Assert.Equal("A novelist.", person!.Bio);
        Assert.Equal("1970-01-01", person.BirthDate);
        Assert.Equal("London", person.BirthPlace);
        Assert.Null(person.DeathDate);
        Assert.Null(person.DeathPlace);
        Assert.Equal("https://example.com", person.Website);
    }

    [Fact]
    public async Task UpdatePersonBioAsync_TrimsWhitespace()
    {
        var ct = TestContext.Current.CancellationToken;
        var personId = await SeedPersonAsync("Trim Author", ct);

        await _sut.UpdatePersonBioAsync(
            personId,
            bio: "  bio text  ",
            birthDate: " 1985 ",
            birthPlace: " Paris ",
            deathDate: null,
            deathPlace: null,
            website: " https://example.org ",
            ct);

        await using var db = GetTrackingContext();
        var person = await db.People.FindAsync([personId], ct);
        Assert.Equal("bio text", person!.Bio);
        Assert.Equal("1985", person.BirthDate);
        Assert.Equal("Paris", person.BirthPlace);
        Assert.Equal("https://example.org", person.Website);
    }

    [Fact]
    public async Task UpdatePersonBioAsync_UnknownPersonId_Throws()
    {
        var ct = TestContext.Current.CancellationToken;

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.UpdatePersonBioAsync(
                999999,
                bio: null, birthDate: null, birthPlace: null,
                deathDate: null, deathPlace: null, website: null,
                ct));
        Assert.Contains("999999", ex.Message);
    }

    // -----------------------------------------------------------------------
    // Category INamedLookup — entity interface compliance
    // -----------------------------------------------------------------------

    [Fact]
    public void Category_ImplementsINamedLookup()
    {
        var category = new Category { CategoryId = 42, Name = "Fiction" };

        // Category must implement INamedLookup
        var lookup = (BookDB.Models.Interfaces.INamedLookup)category;

        Assert.Equal(42, lookup.Id);
        Assert.Equal("Fiction", lookup.Name);
    }

    [Fact]
    public void PurchasePlace_ImplementsINamedLookup()
    {
        var place = new PurchasePlace { PurchasePlaceId = 7, Name = "Amazon" };

        // PurchasePlace must implement INamedLookup
        var lookup = (BookDB.Models.Interfaces.INamedLookup)place;

        Assert.Equal(7, lookup.Id);
        Assert.Equal("Amazon", lookup.Name);
    }

    // -----------------------------------------------------------------------
    // Category service methods
    // -----------------------------------------------------------------------

    private async Task<int> SeedCategoryAsync(string name, CancellationToken ct)
    {
        await using var db = GetTrackingContext();
        var cat = new Category { Name = name };
        db.Categories.Add(cat);
        await db.SaveChangesAsync(ct);
        return cat.CategoryId;
    }

    private async Task SeedBookCategoryAsync(int bookId, int categoryId, CancellationToken ct)
    {
        await using var db = GetTrackingContext();
        var bc = new BookCategory { BookId = bookId, CategoryId = categoryId };
        db.BookCategories.Add(bc);
        await db.SaveChangesAsync(ct);
    }

    private async Task<int> SeedPurchasePlaceAsync(string name, CancellationToken ct)
    {
        await using var db = GetTrackingContext();
        var place = new PurchasePlace { Name = name };
        db.PurchasePlaces.Add(place);
        await db.SaveChangesAsync(ct);
        return place.PurchasePlaceId;
    }

    [Fact]
    public async Task GetCategoryBookCountAsync_ReturnsCorrectCount()
    {
        var ct = TestContext.Current.CancellationToken;
        var catId = await SeedCategoryAsync("Fantasy", ct);
        var bookId1 = await SeedBookAsync(ct, title: "Book 1");
        var bookId2 = await SeedBookAsync(ct, title: "Book 2");
        await SeedBookCategoryAsync(bookId1, catId, ct);
        await SeedBookCategoryAsync(bookId2, catId, ct);

        var count = await _sut.GetCategoryBookCountAsync(catId, ct);

        Assert.Equal(2, count);
    }

    [Fact]
    public async Task GetCategoryBookCountAsync_UnusedCategory_ReturnsZero()
    {
        var ct = TestContext.Current.CancellationToken;
        var catId = await SeedCategoryAsync("Unused", ct);
        var count = await _sut.GetCategoryBookCountAsync(catId, ct);
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task AddCategoryAsync_PersistsNewCategory()
    {
        var ct = TestContext.Current.CancellationToken;
        var newId = await _sut.AddCategoryAsync("TestCategory_Unique_28", ct);

        await using var db = GetTrackingContext();
        var cat = await db.Categories.FindAsync([newId], ct);
        Assert.NotNull(cat);
        Assert.Equal("TestCategory_Unique_28", cat!.Name);
    }

    [Fact]
    public async Task AddCategoryAsync_DuplicateName_Throws()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedCategoryAsync("Mystery", ct);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.AddCategoryAsync("Mystery", ct));
    }

    [Fact]
    public async Task AddCategoryAsync_DuplicateNameCaseInsensitive_Throws()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedCategoryAsync("Mystery", ct);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.AddCategoryAsync("MYSTERY", ct));
    }

    [Fact]
    public async Task RenameCategoryAsync_UpdatesName()
    {
        var ct = TestContext.Current.CancellationToken;
        var catId = await SeedCategoryAsync("Old Category", ct);

        await _sut.RenameCategoryAsync(catId, "New Category", ct);

        await using var db = GetTrackingContext();
        var cat = await db.Categories.FindAsync([catId], ct);
        Assert.Equal("New Category", cat!.Name);
    }

    [Fact]
    public async Task DeleteCategoryAsync_WhenUnreferenced_RemovesRow()
    {
        var ct = TestContext.Current.CancellationToken;
        var catId = await SeedCategoryAsync("Unused Category", ct);

        await _sut.DeleteCategoryAsync(catId, ct);

        await using var db = GetTrackingContext();
        var cat = await db.Categories.FindAsync([catId], ct);
        Assert.Null(cat);
    }

    [Fact]
    public async Task DeleteCategoryAsync_WhenReferenced_Throws()
    {
        var ct = TestContext.Current.CancellationToken;
        var catId = await SeedCategoryAsync("Used Category", ct);
        var bookId = await SeedBookAsync(ct, title: "Some Book");
        await SeedBookCategoryAsync(bookId, catId, ct);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.DeleteCategoryAsync(catId, ct));
        Assert.Contains("Cannot delete Category", ex.Message);
    }

    // -----------------------------------------------------------------------
    // PurchasePlace service methods
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetPurchasePlaceBookCountAsync_ReturnsCorrectCount()
    {
        var ct = TestContext.Current.CancellationToken;
        var placeId = await SeedPurchasePlaceAsync("Amazon", ct);
        await SeedBookAsync(ct, title: "Bought Online");

        // Seed books with this purchase place
        await using var db = GetTrackingContext();
        var book1 = await db.Books.FirstAsync(b => b.Title == "Bought Online", ct);
        book1.PurchasePlaceId = placeId;
        await db.SaveChangesAsync(ct);

        var count = await _sut.GetPurchasePlaceBookCountAsync(placeId, ct);
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task GetPurchasePlaceBookCountAsync_UnusedPlace_ReturnsZero()
    {
        var ct = TestContext.Current.CancellationToken;
        var placeId = await SeedPurchasePlaceAsync("Unused Place", ct);
        var count = await _sut.GetPurchasePlaceBookCountAsync(placeId, ct);
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task AddPurchasePlaceAsync_PersistsNewPlace()
    {
        var ct = TestContext.Current.CancellationToken;
        var newId = await _sut.AddPurchasePlaceAsync("Local Bookshop", ct);

        await using var db = GetTrackingContext();
        var place = await db.PurchasePlaces.FindAsync([newId], ct);
        Assert.NotNull(place);
        Assert.Equal("Local Bookshop", place!.Name);
    }

    [Fact]
    public async Task RenamePurchasePlaceAsync_UpdatesName()
    {
        var ct = TestContext.Current.CancellationToken;
        var placeId = await SeedPurchasePlaceAsync("Old Place", ct);

        await _sut.RenamePurchasePlaceAsync(placeId, "New Place", ct);

        await using var db = GetTrackingContext();
        var place = await db.PurchasePlaces.FindAsync([placeId], ct);
        Assert.Equal("New Place", place!.Name);
    }

    [Fact]
    public async Task DeletePurchasePlaceAsync_WhenUnreferenced_RemovesRow()
    {
        var ct = TestContext.Current.CancellationToken;
        var placeId = await SeedPurchasePlaceAsync("Unused Place", ct);

        await _sut.DeletePurchasePlaceAsync(placeId, ct);

        await using var db = GetTrackingContext();
        var place = await db.PurchasePlaces.FindAsync([placeId], ct);
        Assert.Null(place);
    }

    [Fact]
    public async Task DeletePurchasePlaceAsync_WhenReferenced_Throws()
    {
        var ct = TestContext.Current.CancellationToken;
        var placeId = await SeedPurchasePlaceAsync("Used Place", ct);
        await using (var db = GetTrackingContext())
        {
            db.Books.Add(new Book { Title = "Ref Book", PurchasePlaceId = placeId });
            await db.SaveChangesAsync(ct);
        }
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.DeletePurchasePlaceAsync(placeId, ct));
        Assert.Contains("Cannot delete", ex.Message);
    }

    [Fact]
    public async Task MergePurchasePlacesAsync_RepointsAllBooksAndDeletesSource()
    {
        var ct = TestContext.Current.CancellationToken;
        var sourceId = await SeedPurchasePlaceAsync("Source Place", ct);
        var targetId = await SeedPurchasePlaceAsync("Target Place", ct);

        // Seed two books with sourceId
        await using (var db = GetTrackingContext())
        {
            db.Books.AddRange(
                new Book { Title = "Book A", PurchasePlaceId = sourceId },
                new Book { Title = "Book B", PurchasePlaceId = sourceId });
            await db.SaveChangesAsync(ct);
        }

        await _sut.MergePurchasePlacesAsync(sourceId, targetId, ct);

        await using var dbCheck = GetTrackingContext();
        var sourceExists = await dbCheck.PurchasePlaces.AnyAsync(p => p.PurchasePlaceId == sourceId, ct);
        var booksAtTarget = await dbCheck.Books.CountAsync(b => b.PurchasePlaceId == targetId, ct);
        Assert.False(sourceExists);
        Assert.Equal(2, booksAtTarget);
    }

    [Fact]
    public async Task MergePurchasePlacesAsync_SourceEqualsTarget_NoOp()
    {
        var ct = TestContext.Current.CancellationToken;
        var placeId = await SeedPurchasePlaceAsync("Same Place", ct);

        await _sut.MergePurchasePlacesAsync(placeId, placeId, ct);

        await using var db = GetTrackingContext();
        var place = await db.PurchasePlaces.FindAsync([placeId], ct);
        Assert.NotNull(place);
    }

    // -----------------------------------------------------------------------
    // Collections
    // -----------------------------------------------------------------------

    private async Task<int> SeedCollectionAsync(string name, int sortOrder, CancellationToken ct)
    {
        await using var db = GetTrackingContext();
        var c = new Collection { Name = name, SortOrder = sortOrder };
        db.Collections.Add(c);
        await db.SaveChangesAsync(ct);
        return c.CollectionId;
    }

    [Fact]
    public async Task AddCollectionAsync_AppendsWithNextSortOrder()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedCollectionAsync("Existing", 100, ct);

        var id = await _sut.AddCollectionAsync("New Collection", ct);

        await using var db = GetTrackingContext();
        var added = await db.Collections.FindAsync([id], ct);
        Assert.NotNull(added);
        Assert.Equal("New Collection", added!.Name);
        Assert.Equal(101, added.SortOrder);
    }

    [Fact]
    public async Task AddCollectionAsync_DuplicateName_Throws()
    {
        var ct = TestContext.Current.CancellationToken;
        await _sut.AddCollectionAsync("Duplicate", ct);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.AddCollectionAsync("duplicate", ct)); // case-insensitive
    }

    [Fact]
    public async Task RenameCollectionAsync_ChangesName()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = await SeedCollectionAsync("Old", 0, ct);

        await _sut.RenameCollectionAsync(id, "Renamed", ct);

        await using var db = GetTrackingContext();
        var c = await db.Collections.FindAsync([id], ct);
        Assert.Equal("Renamed", c!.Name);
    }

    [Fact]
    public async Task GetCollectionBookCountAsync_CountsBooksInCollection()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = await SeedCollectionAsync("WithBooks", 0, ct);
        await using (var db = GetTrackingContext())
        {
            db.Books.Add(new Book { Title = "B1", CollectionId = id });
            db.Books.Add(new Book { Title = "B2", CollectionId = id });
            await db.SaveChangesAsync(ct);
        }

        Assert.Equal(2, await _sut.GetCollectionBookCountAsync(id, ct));
    }

    [Fact]
    public async Task DeleteCollectionAsync_WhenEmpty_Removes()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = await SeedCollectionAsync("Empty", 0, ct);

        await _sut.DeleteCollectionAsync(id, ct);

        await using var db = GetTrackingContext();
        Assert.Null(await db.Collections.FindAsync([id], ct));
    }

    [Fact]
    public async Task DeleteCollectionAsync_WhenHasBooks_Throws()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = await SeedCollectionAsync("HasBooks", 0, ct);
        await using (var db = GetTrackingContext())
        {
            db.Books.Add(new Book { Title = "B", CollectionId = id });
            await db.SaveChangesAsync(ct);
        }

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.DeleteCollectionAsync(id, ct));
    }

    [Fact]
    public async Task ReorderCollectionsAsync_SetsSortOrderByPosition()
    {
        var ct = TestContext.Current.CancellationToken;
        var a = await SeedCollectionAsync("A", 0, ct);
        var b = await SeedCollectionAsync("B", 1, ct);
        var c = await SeedCollectionAsync("C", 2, ct);

        await _sut.ReorderCollectionsAsync(new[] { c, a, b }, ct);

        await using var db = GetTrackingContext();
        Assert.Equal(0, (await db.Collections.FindAsync([c], ct))!.SortOrder);
        Assert.Equal(1, (await db.Collections.FindAsync([a], ct))!.SortOrder);
        Assert.Equal(2, (await db.Collections.FindAsync([b], ct))!.SortOrder);
    }

    [Fact]
    public async Task MergeCollectionsAsync_MovesBooksToTargetAndDeletesSource()
    {
        var ct = TestContext.Current.CancellationToken;
        var source = await SeedCollectionAsync("Source", 0, ct);
        var target = await SeedCollectionAsync("Target", 1, ct);
        await using (var db = GetTrackingContext())
        {
            db.Books.Add(new Book { Title = "B1", CollectionId = source });
            db.Books.Add(new Book { Title = "B2", CollectionId = source });
            await db.SaveChangesAsync(ct);
        }

        await _sut.MergeCollectionsAsync(source, target, ct);

        await using var verify = GetTrackingContext();
        Assert.Null(await verify.Collections.FindAsync([source], ct));
        Assert.Equal(2, await verify.Books.CountAsync(b => b.CollectionId == target, ct));
    }
}
