using System;
using System.Linq;
using System.Threading.Tasks;
using BookDB.Desktop.Tests.Helpers;
using BookDB.Desktop.ViewModels;
using BookDB.Models.Entities;
using CommunityToolkit.Mvvm.Messaging;
using Xunit;

namespace BookDB.Desktop.Tests.ViewModels;

/// <summary>
/// Behavioral tests for A-Z letter grouping of Author/Series/Publisher facets.
/// FilterPanelViewModel.LoadFacetsAsync groups IsGrouped=true facets by first letter.
/// </summary>
public sealed class FacetGroupingTests : IDisposable
{
    private readonly TestLookupServiceFactory _factory;
    private readonly FilterPanelViewModel _filterPanel;

    public FacetGroupingTests()
    {
        _factory = new TestLookupServiceFactory();
        var messenger = new WeakReferenceMessenger();
        _filterPanel = new FilterPanelViewModel(messenger, _factory.BookService, _factory.BookSearchService, new TestLookupServiceFactory.NullSettingsService(), new TestLookupServiceFactory.NullWindowService());
    }

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task LoadFacetsAsync_GroupedFacet_CreatesLetterGroups()
    {
        // Arrange — three publishers with names starting A, B, Z
        await using var db = _factory.DbContextFactory.CreateDbContext();

        var pubA = new Publisher { Name = "Alpha Press" };
        var pubB = new Publisher { Name = "Beta Books" };
        var pubZ = new Publisher { Name = "Zephyr Publishing" };
        db.Publishers.Add(pubA);
        db.Publishers.Add(pubB);
        db.Publishers.Add(pubZ);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        db.Books.Add(new Book { Title = "Book A", PublisherId = pubA.PublisherId });
        db.Books.Add(new Book { Title = "Book B", PublisherId = pubB.PublisherId });
        db.Books.Add(new Book { Title = "Book Z", PublisherId = pubZ.PublisherId });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act
        await _filterPanel.LoadFacetsAsync();

        // Assert
        var group = _filterPanel.FacetGroups.First(g => g.FacetKey == "Publisher");

        Assert.True(group.LetterGroups.Count >= 3,
            $"Expected at least 3 letter groups (A, B, Z) but got {group.LetterGroups.Count}");
        Assert.Contains(group.LetterGroups, lg => lg.Letter == "A");
        Assert.Contains(group.LetterGroups, lg => lg.Letter == "B");
        Assert.Contains(group.LetterGroups, lg => lg.Letter == "Z");
        // For grouped facets the values are stored in the letter groups (AllValues), not in group.Values
        Assert.Equal(3, group.LetterGroups.SelectMany(lg => lg.AllValues).Count());
    }

    [Fact]
    public async Task LoadFacetsAsync_EmptyFacetName_GroupedUnderHash()
    {
        // Arrange — publisher with empty name starts with digit → "#" bucket per impl:
        // string.IsNullOrEmpty(fv.Name) ? "#" : fv.Name[0].ToString().ToUpperInvariant()
        await using var db = _factory.DbContextFactory.CreateDbContext();

        var pubEmpty = new Publisher { Name = "" };
        db.Publishers.Add(pubEmpty);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        db.Books.Add(new Book { Title = "Book Empty Pub", PublisherId = pubEmpty.PublisherId });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act
        await _filterPanel.LoadFacetsAsync();

        // Assert
        var group = _filterPanel.FacetGroups.First(g => g.FacetKey == "Publisher");
        Assert.Contains(group.LetterGroups, lg => lg.Letter == "#");
    }

    [Fact]
    public async Task LoadFacetsAsync_NonGroupedFacet_HasNoLetterGroups()
    {
        // Arrange — Format facet is IsGrouped=false; seed some formats and books
        await using var db = _factory.DbContextFactory.CreateDbContext();

        var fmt1 = new Format { Name = "Hardcover" };
        var fmt2 = new Format { Name = "Paperback" };
        db.Formats.Add(fmt1);
        db.Formats.Add(fmt2);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        db.Books.Add(new Book { Title = "Book HC", FormatId = fmt1.FormatId });
        db.Books.Add(new Book { Title = "Book PB", FormatId = fmt2.FormatId });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act
        await _filterPanel.LoadFacetsAsync();

        // Assert — Format group should have values but NO letter groups
        var group = _filterPanel.FacetGroups.First(g => g.FacetKey == "Format");
        Assert.Empty(group.LetterGroups);
        Assert.True(group.Values.Count > 0,
            "Expected Format facet group to have values but found none");
    }
}
