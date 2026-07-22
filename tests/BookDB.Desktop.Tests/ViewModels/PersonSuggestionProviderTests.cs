using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BookDB.Desktop.ViewModels;
using BookDB.Models.Entities;
using Xunit;

namespace BookDB.Desktop.Tests.ViewModels;

/// <summary>
/// The shared person type-ahead (lend-flow semantics): matches both name orders
/// case-insensitively against the in-memory snapshot, appends the "new author" row only when no
/// exact match exists, and resolves typed text back to an existing person for save.
/// </summary>
public sealed class PersonSuggestionProviderTests
{
    private static PersonSuggestionProvider MakeProvider(params Person[] people)
    {
        var provider = new PersonSuggestionProvider();
        provider.LoadSnapshot(people);
        return provider;
    }

    private static Person Tolkien(int id = 1) =>
        new() { PersonId = id, DisplayName = "J.R.R. Tolkien", SortName = "Tolkien, J.R.R." };

    private static Person LeGuin(int id = 2) =>
        new() { PersonId = id, DisplayName = "Ursula K. Le Guin", SortName = "Le Guin, Ursula K." };

    private static async Task<IPersonSuggestion[]> PopulateAsync(
        PersonSuggestionProvider provider, string text)
    {
        var result = await provider.Populator(text, CancellationToken.None);
        return result?.Cast<IPersonSuggestion>().ToArray() ?? [];
    }

    [Fact]
    public async Task Populate_MatchesDisplayNameOrder_CaseInsensitively()
    {
        var provider = MakeProvider(Tolkien(), LeGuin());

        var suggestions = await PopulateAsync(provider, "j.r.r. tolk");

        var existing = Assert.Single(suggestions.OfType<ExistingPersonSuggestion>());
        Assert.Equal("J.R.R. Tolkien", existing.Person.DisplayName);
    }

    [Fact]
    public async Task Populate_MatchesSortNameOrder_CaseInsensitively()
    {
        var provider = MakeProvider(Tolkien(), LeGuin());

        var suggestions = await PopulateAsync(provider, "tolkien, j");

        var existing = Assert.Single(suggestions.OfType<ExistingPersonSuggestion>());
        Assert.Equal("J.R.R. Tolkien", existing.Person.DisplayName);
    }

    [Fact]
    public async Task Populate_AppendsNewRow_WhenNoExactMatch()
    {
        var provider = MakeProvider(Tolkien());

        var suggestions = await PopulateAsync(provider, "Tolk");

        Assert.Single(suggestions.OfType<ExistingPersonSuggestion>());
        var newRow = Assert.Single(suggestions.OfType<NewPersonSuggestion>());
        Assert.Equal("Tolk", newRow.ValueText);
    }

    [Fact]
    public async Task Populate_SuppressesNewRow_OnExactMatchInEitherNameOrder()
    {
        var provider = MakeProvider(Tolkien());

        var byDisplay = await PopulateAsync(provider, "j.r.r. tolkien");
        var bySort = await PopulateAsync(provider, "TOLKIEN, J.R.R.");

        Assert.Empty(byDisplay.OfType<NewPersonSuggestion>());
        Assert.Empty(bySort.OfType<NewPersonSuggestion>());
    }

    [Fact]
    public async Task Populate_EmptyText_YieldsNothing()
    {
        var provider = MakeProvider(Tolkien());

        var result = await provider.Populator(string.Empty, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public void Resolve_FindsExistingPerson_InBothNameOrders_IgnoringCase()
    {
        var provider = MakeProvider(Tolkien(), LeGuin());

        Assert.Equal(1, provider.Resolve("j.r.r. TOLKIEN")?.PersonId);
        Assert.Equal(1, provider.Resolve("tolkien, j.r.r.")?.PersonId);
        Assert.Equal(2, provider.Resolve(" ursula k. le guin ")?.PersonId);
    }

    [Fact]
    public void Resolve_ReturnsNull_ForUnknownOrEmptyText()
    {
        var provider = MakeProvider(Tolkien());

        Assert.Null(provider.Resolve("Tolk"));
        Assert.Null(provider.Resolve(""));
        Assert.Null(provider.Resolve("   "));
    }

    [Fact]
    public void RowViewModel_TracksExistingVersusNew_AsTextChanges()
    {
        var provider = MakeProvider(Tolkien());
        var row = new PersonSuggestionRowViewModel(provider);

        Assert.False(row.IsExistingPerson);
        Assert.False(row.IsNewPerson);

        row.SearchText = "j.r.r. tolkien";
        Assert.True(row.IsExistingPerson);
        Assert.False(row.IsNewPerson);
        Assert.Equal(1, row.ResolvedPerson?.PersonId);

        row.SearchText = "Somebody Unknown";
        Assert.False(row.IsExistingPerson);
        Assert.True(row.IsNewPerson);
        Assert.Null(row.ResolvedPerson);
    }

    [Fact]
    public void RowViewModel_NameToPersist_IsCanonicalDisplayName_ForAResolvedSortNameOrCaseVariant()
    {
        var provider = MakeProvider(Tolkien());
        var row = new PersonSuggestionRowViewModel(provider);

        // Typed in sort-name order and mixed case: reuse matches on DisplayName only, so the row must
        // persist the canonical DisplayName — otherwise the save mints a duplicate person.
        row.SearchText = "tolkien, j.r.r.";
        Assert.Equal("J.R.R. Tolkien", row.NameToPersist);

        row.SearchText = "J.R.R. TOLKIEN";
        Assert.Equal("J.R.R. Tolkien", row.NameToPersist);

        // An unresolved name persists the trimmed typed text (a new person).
        row.SearchText = "  Somebody Unknown  ";
        Assert.Equal("Somebody Unknown", row.NameToPersist);
    }
}
