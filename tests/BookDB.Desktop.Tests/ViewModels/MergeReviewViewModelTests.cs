using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BookDB.Desktop.ViewModels;
using BookDB.Models.Metadata;
using Xunit;

namespace BookDB.Desktop.Tests.ViewModels;

public class MergeReviewViewModelTests
{
    private static BookMetadata MakeSource(
        string sourceName,
        string? title = null,
        string? publisher = null,
        string? pubDate = null,
        IReadOnlyList<string>? authors = null) =>
        new BookMetadata(
            Title: title,
            Subtitle: null,
            Authors: authors ?? new List<string>(),
            Publisher: publisher,
            PubDate: pubDate,
            Language: null,
            Isbn: "9780451526538",
            Pages: null,
            Description: null,
            CoverImageUrl: null,
            Series: null,
            SeriesNumber: null,
            SourceName: sourceName);

    [Fact]
    public void TwoSourcesWithDifferentTitles_ShowsOneFieldDiffRow()
    {
        var sources = new List<BookMetadata>
        {
            MakeSource("GoogleBooks", title: "Nineteen Eighty-Four"),
            MakeSource("OpenLibrary", title: "1984")
        };

        var vm = new MergeReviewViewModel(
            sources: sources,
            currentBook: null,
            coverOptions: new List<CoverOption>(),
            bookMetadataService: null!,
            messenger: null!,
            existingBookId: null,
            collectionId: null,
            closeDialog: _ => { });

        Assert.Single(vm.FieldDiffs);
        Assert.Equal("Title", vm.FieldDiffs[0].RawKey);
        Assert.Equal(2, vm.FieldDiffs[0].SourceValues.Count);
    }

    [Fact]
    public void AgreeingSources_StillExposeBookIdentity_TitleAndIsbn()
    {
        // All sources agree -> no field-diff rows, so the identity header is the only thing telling the
        // user which book they are reviewing.
        var sources = new List<BookMetadata>
        {
            MakeSource("GoogleBooks", title: "1984"),
            MakeSource("OpenLibrary", title: "1984")
        };

        var vm = new MergeReviewViewModel(
            sources: sources,
            currentBook: null,
            coverOptions: new List<CoverOption>(),
            bookMetadataService: null!,
            messenger: null!,
            existingBookId: null,
            collectionId: null,
            closeDialog: _ => { });

        Assert.True(vm.HasNoConflicts);
        Assert.Equal("1984", vm.IdentityTitle);
        Assert.True(vm.HasIdentityIsbn);
        Assert.Equal("9780451526538", vm.IdentityIsbn);
        Assert.Contains("9780451526538", vm.IdentityIsbnDisplay);
    }

    [Fact]
    public void RateLimitedSources_SurfaceAsAWarningNote_NamingTheSources()
    {
        var sources = new List<BookMetadata> { MakeSource("GoogleBooks", title: "1984") };

        var vm = new MergeReviewViewModel(
            sources: sources,
            currentBook: null,
            coverOptions: new List<CoverOption>(),
            bookMetadataService: null!,
            messenger: null!,
            existingBookId: null,
            collectionId: null,
            closeDialog: _ => { },
            rateLimitedSources: ["OpenLibrary"]);

        Assert.True(vm.HasRateLimitedNote);
        Assert.Contains("OpenLibrary", vm.RateLimitedNote);
    }

    [Fact]
    public void NoRateLimitedSources_LeavesTheNoteHidden()
    {
        var sources = new List<BookMetadata> { MakeSource("GoogleBooks", title: "1984") };

        var vm = new MergeReviewViewModel(
            sources: sources,
            currentBook: null,
            coverOptions: new List<CoverOption>(),
            bookMetadataService: null!,
            messenger: null!,
            existingBookId: null,
            collectionId: null,
            closeDialog: _ => { });

        Assert.False(vm.HasRateLimitedNote);
        Assert.Null(vm.RateLimitedNote);
        Assert.False(vm.HasErroredNote);
        Assert.Null(vm.ErroredNote);
        Assert.False(vm.HasNoResultNote);
        Assert.Null(vm.NoResultNote);
    }

    [Fact]
    public void ErroredSources_SurfaceAsAnErrorNote_NamingTheSources()
    {
        var sources = new List<BookMetadata> { MakeSource("GoogleBooks", title: "1984") };

        var vm = new MergeReviewViewModel(
            sources: sources,
            currentBook: null,
            coverOptions: new List<CoverOption>(),
            bookMetadataService: null!,
            messenger: null!,
            existingBookId: null,
            collectionId: null,
            closeDialog: _ => { },
            erroredSources: ["LibrisKB"]);

        Assert.True(vm.HasErroredNote);
        Assert.Contains("LibrisKB", vm.ErroredNote);
        Assert.False(vm.HasNoResultNote);
    }

    [Fact]
    public void NoResultSources_SurfaceAsAnInfoNote_NamingTheSources()
    {
        var sources = new List<BookMetadata> { MakeSource("GoogleBooks", title: "1984") };

        var vm = new MergeReviewViewModel(
            sources: sources,
            currentBook: null,
            coverOptions: new List<CoverOption>(),
            bookMetadataService: null!,
            messenger: null!,
            existingBookId: null,
            collectionId: null,
            closeDialog: _ => { },
            noResultSources: ["IsbnSearchOrg"]);

        Assert.True(vm.HasNoResultNote);
        Assert.Contains("IsbnSearchOrg", vm.NoResultNote);
        Assert.False(vm.HasErroredNote);
    }

    [Fact]
    public void SelectSourceValue_MarksChosenSelected_ClearsOthers()
    {
        var sources = new List<BookMetadata>
        {
            MakeSource("GoogleBooks", title: "Nineteen Eighty-Four"),
            MakeSource("OpenLibrary", title: "1984")
        };

        var vm = new MergeReviewViewModel(
            sources: sources,
            currentBook: null,
            coverOptions: new List<CoverOption>(),
            bookMetadataService: null!,
            messenger: null!,
            existingBookId: null,
            collectionId: null,
            closeDialog: _ => { });

        var row = vm.FieldDiffs[0];
        var secondOption = row.SourceValues[1];
        vm.SelectSourceValue(row, secondOption);

        Assert.True(row.SourceValues[1].IsSelected);
        Assert.False(row.SourceValues[0].IsSelected);
    }

    [Fact]
    public void AcceptAllFromSource_SelectsAllValuesFromThatSource()
    {
        var sources = new List<BookMetadata>
        {
            MakeSource("GoogleBooks", title: "Nineteen Eighty-Four", publisher: "Secker and Warburg"),
            MakeSource("OpenLibrary", title: "1984", publisher: "Signet Classic")
        };

        var vm = new MergeReviewViewModel(
            sources: sources,
            currentBook: null,
            coverOptions: new List<CoverOption>(),
            bookMetadataService: null!,
            messenger: null!,
            existingBookId: null,
            collectionId: null,
            closeDialog: _ => { });

        vm.AcceptAllFromSourceCommand.Execute("OpenLibrary");

        foreach (var row in vm.FieldDiffs)
        {
            var selected = row.SourceValues.Find(sv => sv.IsSelected);
            Assert.NotNull(selected);
            Assert.Equal("OpenLibrary", selected.SourceName);
        }
    }

    [Fact]
    public void IsNewBook_TrueWhenExistingBookIdIsNull()
    {
        var sources = new List<BookMetadata>
        {
            MakeSource("GoogleBooks", title: "Nineteen Eighty-Four"),
            MakeSource("OpenLibrary", title: "1984")
        };

        var vm = new MergeReviewViewModel(
            sources: sources,
            currentBook: null,
            coverOptions: new List<CoverOption>(),
            bookMetadataService: null!,
            messenger: null!,
            existingBookId: null,
            collectionId: null,
            closeDialog: _ => { });

        Assert.True(vm.IsNewBook);
    }

    [Fact]
    public void BuildMergedMetadata_ReturnsSelectedValues()
    {
        var sources = new List<BookMetadata>
        {
            MakeSource("GoogleBooks", title: "Nineteen Eighty-Four"),
            MakeSource("OpenLibrary", title: "1984")
        };

        var vm = new MergeReviewViewModel(
            sources: sources,
            currentBook: null,
            coverOptions: new List<CoverOption>(),
            bookMetadataService: null!,
            messenger: null!,
            existingBookId: null,
            collectionId: null,
            closeDialog: _ => { });

        // Select the OpenLibrary value for Title
        var titleRow = vm.FieldDiffs[0];
        var openLibOption = titleRow.SourceValues.Find(sv => sv.SourceName == "OpenLibrary");
        Assert.NotNull(openLibOption);
        vm.SelectSourceValue(titleRow, openLibOption);

        var merged = vm.BuildMergedMetadata();

        Assert.Equal("1984", merged.Title);
    }

    [Fact]
    public void AuthorRows_SeedFromThePickedAuthorsColumn_AndReseedOnRepick()
    {
        var sources = new List<BookMetadata>
        {
            MakeSource("GoogleBooks", title: "1984", authors: ["George Orwell", "Extra Name"]),
            MakeSource("OpenLibrary", title: "1984", authors: ["George Orwell"])
        };

        var vm = new MergeReviewViewModel(
            sources: sources,
            currentBook: null,
            coverOptions: new List<CoverOption>(),
            bookMetadataService: null!,
            messenger: null!,
            existingBookId: null,
            collectionId: null,
            closeDialog: _ => { });

        var authorsRow = vm.FieldDiffs.Single(r => r.RawKey == "Authors");
        Assert.Equal(new[] { "George Orwell", "Extra Name" }, vm.AuthorRows.Select(r => r.SearchText));

        var openLib = authorsRow.SourceValues.Find(sv => sv.SourceName == "OpenLibrary");
        Assert.NotNull(openLib);
        vm.SelectSourceValue(authorsRow, openLib);

        Assert.Equal(new[] { "George Orwell" }, vm.AuthorRows.Select(r => r.SearchText));
    }

    [Fact]
    public void AuthorRows_SeedFromTheAgreedAuthors_WhenThereIsNoAuthorsDiff()
    {
        var sources = new List<BookMetadata>
        {
            MakeSource("GoogleBooks", title: "1984", authors: ["George Orwell"]),
            MakeSource("OpenLibrary", title: "Nineteen Eighty-Four", authors: ["George Orwell"])
        };

        var vm = new MergeReviewViewModel(
            sources: sources,
            currentBook: null,
            coverOptions: new List<CoverOption>(),
            bookMetadataService: null!,
            messenger: null!,
            existingBookId: null,
            collectionId: null,
            closeDialog: _ => { });

        Assert.DoesNotContain(vm.FieldDiffs, r => r.RawKey == "Authors");
        Assert.Equal(new[] { "George Orwell" }, vm.AuthorRows.Select(r => r.SearchText));
    }

    [Fact]
    public void BuildMergedMetadata_TakesAuthorsFromTheEditedRows_NotTheRawPick()
    {
        var sources = new List<BookMetadata>
        {
            MakeSource("GoogleBooks", title: "1984", authors: ["George Orwel", "Drop Me"]),
            MakeSource("OpenLibrary", title: "1984", authors: ["G. Orwell"])
        };

        var vm = new MergeReviewViewModel(
            sources: sources,
            currentBook: null,
            coverOptions: new List<CoverOption>(),
            bookMetadataService: null!,
            messenger: null!,
            existingBookId: null,
            collectionId: null,
            closeDialog: _ => { });

        // Fix a spelling, drop a row, add one — the merged metadata must carry the edits.
        vm.AuthorRows[0].SearchText = "George Orwell";
        vm.RemoveAuthorRowCommand.Execute(vm.AuthorRows[1]);
        vm.AddAuthorRowCommand.Execute(null);
        vm.AuthorRows[^1].SearchText = "  Added Author  ";

        var merged = vm.BuildMergedMetadata();

        Assert.Equal(new[] { "George Orwell", "Added Author" }, merged.Authors);
    }

    [Fact]
    public void AllSourcesAgree_ProducesEmptyFieldDiffs()
    {
        var sources = new List<BookMetadata>
        {
            MakeSource("GoogleBooks", title: "1984"),
            MakeSource("OpenLibrary", title: "1984")
        };

        var vm = new MergeReviewViewModel(
            sources: sources,
            currentBook: null,
            coverOptions: new List<CoverOption>(),
            bookMetadataService: null!,
            messenger: null!,
            existingBookId: null,
            collectionId: null,
            closeDialog: _ => { });

        Assert.Empty(vm.FieldDiffs);
    }
}
