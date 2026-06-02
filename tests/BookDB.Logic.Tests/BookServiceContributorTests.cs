using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using BookDB.Data;
using BookDB.Data.DbContexts;
using BookDB.Logic.Services;
using BookDB.Models.Entities;
using DbUp;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BookDB.Logic.Tests;

/// <summary>
/// Tests for BookService all-role contributor overload and GetPeopleNamesAsync.
/// Uses a real temp-file SQLite database so FTS5 is available (in-memory SQLite
/// does not support the FTS5 module). DbUp runs all migrations before each test.
/// </summary>
public sealed class BookServiceContributorTests : IDisposable
{
    private readonly string _dbPath;
    private readonly string _connectionString;
    private readonly TestBookDbContextFactory _factory;
    private readonly BookService _sut;

    public BookServiceContributorTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"bookdb_contrib_test_{Guid.NewGuid():N}.db");
        _connectionString = $"Data Source={_dbPath}";

        var upgrader = SqliteExtensions.SqliteDatabase(DeployChanges.To, _connectionString)
            .WithScriptsEmbeddedInAssembly(
                Assembly.GetAssembly(typeof(BookDbContext))!,
                name => name.Contains(".Migrations."))
            .LogToNowhere()
            .Build();

        var result = upgrader.PerformUpgrade();
        if (!result.Successful)
            throw new InvalidOperationException($"DbUp migration failed: {result.Error}");

        var options = new DbContextOptionsBuilder<BookDbContext>()
            .UseSqlite(_connectionString)
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
            .Options;

        _factory = new TestBookDbContextFactory(options);
        _sut = new BookService(_factory);
    }

    public void Dispose()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        try { File.Delete(_dbPath); } catch { /* best effort */ }
    }

    // ---------------------------------------------------------------------------
    // Test 1: update_all_roles
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task UpdateBookContributors_AllRoles_DeletesOldRowsAndInsertsNew()
    {
        // Seed the book and two existing contributors (Author + Editor)
        await using var setup = await _factory.CreateDbContextAsync(TestContext.Current.CancellationToken);
        var book = new Book { Title = "Role Test Book" };
        setup.Books.Add(book);
        await setup.SaveChangesAsync(TestContext.Current.CancellationToken);

        var authorRole = await setup.ContributorRoles.FirstAsync(r => r.Code == "Author", TestContext.Current.CancellationToken);
        var editorRole = await setup.ContributorRoles.FirstAsync(r => r.Code == "Editor", TestContext.Current.CancellationToken);

        var originalAuthor = new Person { DisplayName = "Original Author", SortName = "Author, Original" };
        var originalEditor = new Person { DisplayName = "Original Editor", SortName = "Editor, Original" };
        setup.People.Add(originalAuthor);
        setup.People.Add(originalEditor);
        await setup.SaveChangesAsync(TestContext.Current.CancellationToken);

        setup.BookContributors.Add(new BookContributor
        {
            BookId = book.BookId,
            PersonId = originalAuthor.PersonId,
            ContributorRoleId = authorRole.ContributorRoleId,
            SortOrder = 0
        });
        setup.BookContributors.Add(new BookContributor
        {
            BookId = book.BookId,
            PersonId = originalEditor.PersonId,
            ContributorRoleId = editorRole.ContributorRoleId,
            SortOrder = 1
        });
        await setup.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Verify pre-condition: 2 rows
        var priorCount = await setup.BookContributors.CountAsync(bc => bc.BookId == book.BookId, TestContext.Current.CancellationToken);
        Assert.Equal(2, priorCount);

        // Find a Translator role to use in new set
        var translatorRole = await setup.ContributorRoles.FirstAsync(r => r.Code == "Translator", TestContext.Current.CancellationToken);

        // Act: replace with new contributor list (Author "Foo" + Translator "Bar")
        var newContributors = new List<(string personName, int? roleId)>
        {
            ("Foo Author", authorRole.ContributorRoleId),
            ("Bar Translator", translatorRole.ContributorRoleId)
        };
        await _sut.UpdateBookContributorsAsync(book.BookId, newContributors, TestContext.Current.CancellationToken);

        // Assert: original rows gone, two new rows present
        await using var verify = await _factory.CreateDbContextAsync(TestContext.Current.CancellationToken);
        var remaining = await verify.BookContributors
            .Include(bc => bc.Person)
            .Include(bc => bc.ContributorRole)
            .Where(bc => bc.BookId == book.BookId)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, remaining.Count);
        Assert.Contains(remaining, bc => bc.Person?.DisplayName == "Foo Author" && bc.ContributorRoleId == authorRole.ContributorRoleId);
        Assert.Contains(remaining, bc => bc.Person?.DisplayName == "Bar Translator" && bc.ContributorRoleId == translatorRole.ContributorRoleId);
        Assert.DoesNotContain(remaining, bc => bc.Person?.DisplayName == "Original Author");
        Assert.DoesNotContain(remaining, bc => bc.Person?.DisplayName == "Original Editor");
    }

    // ---------------------------------------------------------------------------
    // Test 2: creates_new_people
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task UpdateBookContributors_AllRoles_CreatesNewPersonIfNotExists()
    {
        await using var setup = await _factory.CreateDbContextAsync(TestContext.Current.CancellationToken);
        var book = new Book { Title = "New Person Book" };
        setup.Books.Add(book);
        await setup.SaveChangesAsync(TestContext.Current.CancellationToken);

        var authorRole = await setup.ContributorRoles.FirstAsync(r => r.Code == "Author", TestContext.Current.CancellationToken);

        // Ensure no such person exists
        var priorPerson = await setup.People.FirstOrDefaultAsync(p => p.DisplayName == "Brand New Author", TestContext.Current.CancellationToken);
        Assert.Null(priorPerson);

        // Act
        var contributors = new List<(string personName, int? roleId)>
        {
            ("Brand New Author", authorRole.ContributorRoleId)
        };
        await _sut.UpdateBookContributorsAsync(book.BookId, contributors, TestContext.Current.CancellationToken);

        // Assert: person was created
        await using var verify = await _factory.CreateDbContextAsync(TestContext.Current.CancellationToken);
        var person = await verify.People.FirstOrDefaultAsync(p => p.DisplayName == "Brand New Author", TestContext.Current.CancellationToken);
        Assert.NotNull(person);
        Assert.Equal("Brand New Author", person!.DisplayName);
    }

    // ---------------------------------------------------------------------------
    // Test 3: skips_empty_rows
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task UpdateBookContributors_AllRoles_SkipsNullAndWhitespaceNames()
    {
        await using var setup = await _factory.CreateDbContextAsync(TestContext.Current.CancellationToken);
        var book = new Book { Title = "Skip Empty Book" };
        setup.Books.Add(book);
        await setup.SaveChangesAsync(TestContext.Current.CancellationToken);

        var authorRole = await setup.ContributorRoles.FirstAsync(r => r.Code == "Author", TestContext.Current.CancellationToken);

        // Input includes empty/whitespace names and a null roleId
        var contributors = new List<(string personName, int? roleId)>
        {
            ("", authorRole.ContributorRoleId),        // empty name — skip
            ("   ", authorRole.ContributorRoleId),     // whitespace name — skip
            ("Valid Author", null),                     // null roleId — skip
            ("Real Author", authorRole.ContributorRoleId) // valid — keep
        };

        await _sut.UpdateBookContributorsAsync(book.BookId, contributors, TestContext.Current.CancellationToken);

        await using var verify = await _factory.CreateDbContextAsync(TestContext.Current.CancellationToken);
        var saved = await verify.BookContributors
            .Where(bc => bc.BookId == book.BookId)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Single(saved);
    }

    // ---------------------------------------------------------------------------
    // Test 4: preserves_sort_order
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task UpdateBookContributors_AllRoles_SortOrderMatchesInputIndex()
    {
        await using var setup = await _factory.CreateDbContextAsync(TestContext.Current.CancellationToken);
        var book = new Book { Title = "Sort Order Book" };
        setup.Books.Add(book);
        await setup.SaveChangesAsync(TestContext.Current.CancellationToken);

        var authorRole = await setup.ContributorRoles.FirstAsync(r => r.Code == "Author", TestContext.Current.CancellationToken);
        var editorRole = await setup.ContributorRoles.FirstAsync(r => r.Code == "Editor", TestContext.Current.CancellationToken);

        var contributors = new List<(string personName, int? roleId)>
        {
            ("First Person", authorRole.ContributorRoleId),
            ("Second Person", editorRole.ContributorRoleId),
            ("Third Person", authorRole.ContributorRoleId)
        };

        await _sut.UpdateBookContributorsAsync(book.BookId, contributors, TestContext.Current.CancellationToken);

        await using var verify = await _factory.CreateDbContextAsync(TestContext.Current.CancellationToken);
        var saved = await verify.BookContributors
            .Include(bc => bc.Person)
            .Where(bc => bc.BookId == book.BookId)
            .OrderBy(bc => bc.SortOrder)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(3, saved.Count);
        Assert.Equal(0, saved[0].SortOrder);
        Assert.Equal("First Person", saved[0].Person?.DisplayName);
        Assert.Equal(1, saved[1].SortOrder);
        Assert.Equal("Second Person", saved[1].Person?.DisplayName);
        Assert.Equal(2, saved[2].SortOrder);
        Assert.Equal("Third Person", saved[2].Person?.DisplayName);
    }

    // ---------------------------------------------------------------------------
    // Test 5: get_people_names_no_prefix
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task GetPeopleNamesAsync_NoPrefix_ReturnsAllNamesAlphabetically()
    {
        await using var setup = await _factory.CreateDbContextAsync(TestContext.Current.CancellationToken);
        setup.People.Add(new Person { DisplayName = "Charlie Brown", SortName = "Brown, Charlie" });
        setup.People.Add(new Person { DisplayName = "Alice Smith", SortName = "Smith, Alice" });
        setup.People.Add(new Person { DisplayName = "Bob Jones", SortName = "Jones, Bob" });
        await setup.SaveChangesAsync(TestContext.Current.CancellationToken);

        var names = await _sut.GetPeopleNamesAsync(null, TestContext.Current.CancellationToken);

        Assert.Contains("Alice Smith", names);
        Assert.Contains("Bob Jones", names);
        Assert.Contains("Charlie Brown", names);

        // Verify alphabetical order for the three seeded names
        var relevantNames = names.Where(n => n == "Alice Smith" || n == "Bob Jones" || n == "Charlie Brown").ToList();
        Assert.Equal(new[] { "Alice Smith", "Bob Jones", "Charlie Brown" }, relevantNames);
    }

    // ---------------------------------------------------------------------------
    // Test 6: get_people_names_prefix
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task GetPeopleNamesAsync_WithPrefix_ReturnsMatchingNamesOnly()
    {
        await using var setup = await _factory.CreateDbContextAsync(TestContext.Current.CancellationToken);
        setup.People.Add(new Person { DisplayName = "Arnold Smith", SortName = "Smith, Arnold" });
        setup.People.Add(new Person { DisplayName = "Barbara Jones", SortName = "Jones, Barbara" });
        setup.People.Add(new Person { DisplayName = "Carlos Martin", SortName = "Martin, Carlos" });
        await setup.SaveChangesAsync(TestContext.Current.CancellationToken);

        var names = await _sut.GetPeopleNamesAsync("ar", TestContext.Current.CancellationToken);

        // "Arnold Smith" contains "ar" (case-insensitive default in SQLite LIKE)
        // "Carlos Martin" contains "ar"
        // "Barbara Jones" contains "ar" (barBARA)
        Assert.Contains("Arnold Smith", names);
        Assert.Contains("Barbara Jones", names);
        Assert.Contains("Carlos Martin", names);
    }

    // ---------------------------------------------------------------------------
    // Test 7: legacy_overload_unchanged
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task UpdateBookContributors_LegacyOverload_StillCompilesAndDeletesOnlyAuthorRows()
    {
        await using var setup = await _factory.CreateDbContextAsync(TestContext.Current.CancellationToken);
        var book = new Book { Title = "Legacy Overload Book" };
        setup.Books.Add(book);
        await setup.SaveChangesAsync(TestContext.Current.CancellationToken);

        var authorRole = await setup.ContributorRoles.FirstAsync(r => r.Code == "Author", TestContext.Current.CancellationToken);
        var editorRole = await setup.ContributorRoles.FirstAsync(r => r.Code == "Editor", TestContext.Current.CancellationToken);

        var authorPerson = new Person { DisplayName = "Legacy Author", SortName = "Author, Legacy" };
        var editorPerson = new Person { DisplayName = "Legacy Editor", SortName = "Editor, Legacy" };
        setup.People.Add(authorPerson);
        setup.People.Add(editorPerson);
        await setup.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Seed one Author row and one Editor row
        setup.BookContributors.Add(new BookContributor
        {
            BookId = book.BookId,
            PersonId = authorPerson.PersonId,
            ContributorRoleId = authorRole.ContributorRoleId,
            SortOrder = 0
        });
        setup.BookContributors.Add(new BookContributor
        {
            BookId = book.BookId,
            PersonId = editorPerson.PersonId,
            ContributorRoleId = editorRole.ContributorRoleId,
            SortOrder = 1
        });
        await setup.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act: use legacy Author-only overload with a new author name
        await _sut.UpdateBookContributorsAsync(book.BookId, new[] { "New Legacy Author" }, TestContext.Current.CancellationToken);

        // Assert: old Author row gone, new Author row present, Editor row preserved
        await using var verify = await _factory.CreateDbContextAsync(TestContext.Current.CancellationToken);
        var remaining = await verify.BookContributors
            .Include(bc => bc.Person)
            .Include(bc => bc.ContributorRole)
            .Where(bc => bc.BookId == book.BookId)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, remaining.Count);
        Assert.Contains(remaining, bc => bc.Person?.DisplayName == "New Legacy Author" && bc.ContributorRole?.Code == "Author");
        Assert.Contains(remaining, bc => bc.Person?.DisplayName == "Legacy Editor" && bc.ContributorRole?.Code == "Editor");
        Assert.DoesNotContain(remaining, bc => bc.Person?.DisplayName == "Legacy Author");
    }

    [Fact]
    public async Task AddBookWithContributorsAsync_MapsRoleSuffixesToExistingContributorRoles()
    {
        var book = new Book { Title = "Explicit Role Book" };

        await _sut.AddBookWithContributorsAsync(book,
            new[] { "Alice [Translator]", "Bob (Editor)" },
            TestContext.Current.CancellationToken);

        await using var verify = await _factory.CreateDbContextAsync(TestContext.Current.CancellationToken);
        var contributors = await verify.BookContributors
            .Include(bc => bc.Person)
            .Include(bc => bc.ContributorRole)
            .Where(bc => bc.BookId == book.BookId)
            .OrderBy(bc => bc.SortOrder)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, contributors.Count);
        Assert.Contains(contributors, bc => bc.Person?.DisplayName == "Alice" && bc.ContributorRole?.Code == "Translator");
        Assert.Contains(contributors, bc => bc.Person?.DisplayName == "Bob" && bc.ContributorRole?.Code == "Editor");
    }

    [Fact]
    public async Task UpdateBookContributors_LegacyOverload_MapsRoleSuffixesToExistingContributorRoles()
    {
        await using var setup = await _factory.CreateDbContextAsync(TestContext.Current.CancellationToken);
        var book = new Book { Title = "Legacy Explicit Role Book" };
        setup.Books.Add(book);
        await setup.SaveChangesAsync(TestContext.Current.CancellationToken);

        await _sut.UpdateBookContributorsAsync(book.BookId,
            new[] { "Alice [Translator]", "Bob (Editor)" },
            TestContext.Current.CancellationToken);

        await using var verify = await _factory.CreateDbContextAsync(TestContext.Current.CancellationToken);
        var contributors = await verify.BookContributors
            .Include(bc => bc.Person)
            .Include(bc => bc.ContributorRole)
            .Where(bc => bc.BookId == book.BookId)
            .OrderBy(bc => bc.SortOrder)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, contributors.Count);
        Assert.Contains(contributors, bc => bc.Person?.DisplayName == "Alice" && bc.ContributorRole?.Code == "Translator");
        Assert.Contains(contributors, bc => bc.Person?.DisplayName == "Bob" && bc.ContributorRole?.Code == "Editor");
    }
}
