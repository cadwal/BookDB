using System;
using System.Collections.Generic;
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
        string? pubDate = null) =>
        new BookMetadata(
            Title: title,
            Subtitle: null,
            Authors: new List<string>(),
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
