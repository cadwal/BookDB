using System.Collections.Generic;
using BookDB.Models;
using BookDB.Models.Metadata;
using Xunit;

namespace BookDB.Logic.Tests.MetadataSources;

public class FieldDiffTests
{
    private static BookMetadata MakeSource(string sourceName, string? title = null, string? subtitle = null,
        string? publisher = null, string? pubDate = null, string? language = null,
        int? pages = null, string? description = null, string? series = null, string? seriesNumber = null)
        => new(
            Title: title,
            Subtitle: subtitle,
            Authors: [],
            Publisher: publisher,
            PubDate: pubDate,
            Language: language,
            Isbn: null,
            Pages: pages,
            Description: description,
            CoverImageUrl: null,
            Series: series,
            SeriesNumber: seriesNumber,
            SourceName: sourceName);

    [Fact]
    public void ComputeDiffs_TwoSourcesSameTitle_ExcludesTitleFromDiffs()
    {
        var sources = new List<BookMetadata>
        {
            MakeSource("Google", title: "Same Title"),
            MakeSource("OpenLibrary", title: "Same Title"),
        };

        var diffs = FieldDiffComputer.ComputeDiffs(sources);

        Assert.DoesNotContain(diffs, d => d.FieldName == "Title");
    }

    [Fact]
    public void ComputeDiffs_TwoSourcesDifferentTitle_IncludesTitleWithBothValues()
    {
        var sources = new List<BookMetadata>
        {
            MakeSource("Google", title: "Title From Google"),
            MakeSource("OpenLibrary", title: "Title From OpenLibrary"),
        };

        var diffs = FieldDiffComputer.ComputeDiffs(sources);

        var titleDiff = Assert.Single(diffs, d => d.FieldName == "Title");
        Assert.Equal(2, titleDiff.SourceValues.Count);
        Assert.Contains(titleDiff.SourceValues, sv => sv.SourceName == "Google" && sv.Value == "Title From Google");
        Assert.Contains(titleDiff.SourceValues, sv => sv.SourceName == "OpenLibrary" && sv.Value == "Title From OpenLibrary");
    }

    [Fact]
    public void ComputeDiffs_CurrentAndTwoSourcesAllIdentical_EmptyDiffs()
    {
        var sources = new List<BookMetadata>
        {
            MakeSource("Google", title: "Same"),
            MakeSource("OpenLibrary", title: "Same"),
        };
        var current = MakeSource("current", title: "Same");

        var diffs = FieldDiffComputer.ComputeDiffs(sources, current);

        Assert.Empty(diffs);
    }

    [Fact]
    public void ComputeDiffs_CurrentDiffersFromSources_IncludesCurrentValue()
    {
        var sources = new List<BookMetadata>
        {
            MakeSource("Google", title: "New Title"),
            MakeSource("OpenLibrary", title: "New Title"),
        };
        var current = MakeSource("current", title: "Old Title");

        var diffs = FieldDiffComputer.ComputeDiffs(sources, current);

        var titleDiff = Assert.Single(diffs, d => d.FieldName == "Title");
        Assert.Equal("Old Title", titleDiff.CurrentValue);
    }

    [Theory]
    [InlineData("March 15, 2005", "2005-03-15")]
    [InlineData("15 March 2005", "2005-03-15")]
    [InlineData("Mar 15, 2005", "2005-03-15")]
    [InlineData("2005-03-15", "2005-03-15")]
    [InlineData("March 2005", "2005-03")]
    [InlineData("Mar 2005", "2005-03")]
    [InlineData("2005-03", "2005-03")]
    [InlineData("2005/03", "2005-03")]
    [InlineData("2005", "2005")]
    [InlineData("unrecognizable", "unrecognizable")]
    [InlineData(null, null)]
    [InlineData("", "")]
    public void NormalizePubDate_VariousFormats_ReturnsNormalizedForm(string? input, string? expected)
    {
        var result = FieldDiffComputer.NormalizePubDate(input);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ComputeDiffs_PubDateDifferentFormats_SameDate_NoDiff()
    {
        // "2005-03-15" and "March 15, 2005" should normalize to the same value
        var sources = new List<BookMetadata>
        {
            MakeSource("Google", pubDate: "2005-03-15"),
            MakeSource("OpenLibrary", pubDate: "March 15, 2005"),
        };

        var diffs = FieldDiffComputer.ComputeDiffs(sources);

        // After normalization, both map to "2005-03-15" — no diff
        Assert.DoesNotContain(diffs, d => d.FieldName == "PubDate");
    }

    [Fact]
    public void ComputeDiffs_PubDateDifferentYears_HasDiff()
    {
        var sources = new List<BookMetadata>
        {
            MakeSource("Google", pubDate: "2005"),
            MakeSource("OpenLibrary", pubDate: "2006"),
        };

        var diffs = FieldDiffComputer.ComputeDiffs(sources);

        Assert.Contains(diffs, d => d.FieldName == "PubDate");
    }
}

public class IsbnNormalizerTests
{
    [Fact]
    public void Normalize_Isbn13WithHyphens_ReturnsDigitsOnly()
    {
        var result = IsbnNormalizer.Normalize("978-91-00-13740-3");
        Assert.Equal("9789100137403", result);
    }

    [Fact]
    public void Normalize_Isbn10WithHyphens_ReturnsDigitsOnly()
    {
        var result = IsbnNormalizer.Normalize("0-451-52653-8");
        Assert.Equal("0451526538", result);
    }

    [Fact]
    public void IsValid_ValidIsbn13_ReturnsTrue()
    {
        Assert.True(IsbnNormalizer.IsValid("9789100137403"));
    }

    [Fact]
    public void IsValid_TooShortIsbn_ReturnsFalse()
    {
        Assert.False(IsbnNormalizer.IsValid("123"));
    }

    [Fact]
    public void IsValid_ValidIsbn10_ReturnsTrue()
    {
        Assert.True(IsbnNormalizer.IsValid("0451526538"));
    }

    [Fact]
    public void TryConvertToIsbn13_Isbn10_ReturnsCorrectIsbn13()
    {
        var result = IsbnNormalizer.TryConvertToIsbn13("0451526538", out var isbn13);
        Assert.True(result);
        Assert.Equal("9780451526533", isbn13);
    }
}
