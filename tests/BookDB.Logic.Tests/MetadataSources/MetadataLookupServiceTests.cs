using System;
using System.Collections.Generic;
using System.Net.Http;
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

        var lookup = await service.FetchAllSourcesAsync("1234567890", CancellationToken.None);

        Assert.Equal(2, lookup.Results.Count);
        Assert.Equal(2, lookup.SourcesQueried);
        Assert.Equal(0, lookup.SourcesFailed);
    }

    [Fact]
    public async Task FetchAllSourcesAsync_OneSourceThrows_ReturnsPartialResults_AndCountsTheFailure()
    {
        var source1 = new StubMetadataSource("Source1", MakeResult("Source1"));
        var source2 = new ThrowingMetadataSource("Source2");
        var service = new MetadataLookupService(
            [source1, source2],
            NullLogger<MetadataLookupService>.Instance);

        var lookup = await service.FetchAllSourcesAsync("1234567890", CancellationToken.None);

        Assert.Single(lookup.Results);
        Assert.Equal("Source1", lookup.Results[0].SourceName);
        Assert.Equal(2, lookup.SourcesQueried);
        Assert.Equal(1, lookup.SourcesFailed);
    }

    [Fact]
    public async Task FetchAllSourcesAsync_SourceReturnsNoHit_IsNotCountedAsFailed()
    {
        var source1 = new StubMetadataSource("Source1", result: null);
        var source2 = new ThrowingMetadataSource("Source2");
        var service = new MetadataLookupService(
            [source1, source2],
            NullLogger<MetadataLookupService>.Instance);

        var lookup = await service.FetchAllSourcesAsync("1234567890", CancellationToken.None);

        // A source that answered "no hit" is a valid response, distinct from the one that errored.
        Assert.Empty(lookup.Results);
        Assert.Equal(2, lookup.SourcesQueried);
        Assert.Equal(1, lookup.SourcesFailed);
    }

    [Fact]
    public async Task FetchAllSourcesAsync_SourceRateLimited_RecordsRateLimitOutcome_NotSilentSuccess()
    {
        var source1 = new StubMetadataSource("Source1", MakeResult("Source1"));
        var source2 = new RateLimitedMetadataSource("Source2");
        var service = new MetadataLookupService(
            [source1, source2],
            NullLogger<MetadataLookupService>.Instance);

        var lookup = await service.FetchAllSourcesAsync("1234567890", CancellationToken.None);

        Assert.Single(lookup.Results);
        Assert.Equal(1, lookup.SourcesFailed); // a 429 is a failure, not a silent success
        Assert.Equal(["Source2"], lookup.RateLimitedSources);
        Assert.Contains(lookup.SourceStatuses!, s => s.SourceName == "Source2" && s.Outcome == SourceLookupOutcome.RateLimited);
        Assert.Contains(lookup.SourceStatuses!, s => s.SourceName == "Source1" && s.Outcome == SourceLookupOutcome.Success);
    }

    [Fact]
    public async Task FetchAllSourcesAsync_ProjectsNoResultAndErroredSources_ByOutcome()
    {
        var ok = new StubMetadataSource("Ok", MakeResult("Ok"));
        var empty = new StubMetadataSource("Empty", result: null);
        var broken = new ThrowingMetadataSource("Broken");
        var service = new MetadataLookupService(
            [ok, empty, broken],
            NullLogger<MetadataLookupService>.Instance);

        var lookup = await service.FetchAllSourcesAsync("1234567890", CancellationToken.None);

        Assert.Equal(["Empty"], lookup.NoResultSources);
        Assert.Equal(["Broken"], lookup.ErroredSources);
        Assert.Empty(lookup.RateLimitedSources);
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

    private class RateLimitedMetadataSource(string sourceName) : IMetadataSource
    {
        public string SourceName { get; } = sourceName;

        public Task<BookMetadata?> FetchAsync(string isbn, CancellationToken ct = default)
            => throw new HttpRequestException("Simulated 429", null, System.Net.HttpStatusCode.TooManyRequests);
    }
}
