using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BookDB.MetadataSources.Services;
using BookDB.MetadataSources.Sources;
using BookDB.Models.Metadata;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BookDB.Logic.Tests.MetadataSources;

public class MetadataLookupServiceTests
{
    private static BookMetadata MakeResult(string sourceName)
        => new(
            Title: "Test Book",
            Subtitle: null,
            Authors: ["Author"],
            Publisher: null,
            PubDate: null,
            Language: null,
            Isbn: "1234567890",
            Pages: null,
            Description: null,
            CoverImageUrl: null,
            Series: null,
            SeriesNumber: null,
            SourceName: sourceName);

    [Fact]
    public async Task FetchAllSourcesAsync_AllSourcesReturn_ReturnsAllResults()
    {
        var source1 = new StubMetadataSource("Source1", MakeResult("Source1"));
        var source2 = new StubMetadataSource("Source2", MakeResult("Source2"));
        var service = new MetadataLookupService(
            [source1, source2],
            NullLogger<MetadataLookupService>.Instance);

        var results = await service.FetchAllSourcesAsync("1234567890", CancellationToken.None);

        Assert.Equal(2, results.Count);
    }

    [Fact]
    public async Task FetchAllSourcesAsync_OneSourceThrows_ReturnsPartialResults()
    {
        var source1 = new StubMetadataSource("Source1", MakeResult("Source1"));
        var source2 = new ThrowingMetadataSource("Source2");
        var service = new MetadataLookupService(
            [source1, source2],
            NullLogger<MetadataLookupService>.Instance);

        var results = await service.FetchAllSourcesAsync("1234567890", CancellationToken.None);

        Assert.Single(results);
        Assert.Equal("Source1", results[0].SourceName);
    }

    private class StubMetadataSource(string sourceName, BookMetadata? result) : IMetadataSource
    {
        private readonly BookMetadata? _result = result;

        public string SourceName { get; } = sourceName;

        public Task<BookMetadata?> FetchAsync(string isbn, CancellationToken ct = default)
            => Task.FromResult(_result);
    }

    private class ThrowingMetadataSource(string sourceName) : IMetadataSource
    {
        public string SourceName { get; } = sourceName;

        public Task<BookMetadata?> FetchAsync(string isbn, CancellationToken ct = default)
            => throw new InvalidOperationException("Simulated source failure");
    }
}
