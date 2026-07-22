using System.Threading;
using System.Threading.Tasks;
using BookDB.Logic.Services;
using BookDB.MetadataSources.Sources;
using BookDB.Models.Metadata;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BookDB.Logic.Tests.Services;

public sealed class FilteringMetadataLookupServiceTests
{
    private sealed class FixedSettingsService(string? value) : ISettingsService
    {
        public Task<string?> GetAsync(string key, CancellationToken ct = default) => Task.FromResult(value);
        public Task SetAsync(string key, string? value, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class TrackingSource(string sourceName) : IMetadataSource
    {
        public bool WasCalled { get; private set; }
        public string SourceName { get; } = sourceName;

        public Task<BookMetadata?> FetchAsync(string isbn, CancellationToken ct = default)
        {
            WasCalled = true;
            return Task.FromResult<BookMetadata?>(null);
        }
    }

    [Fact]
    public async Task FetchAllSourcesAsync_AllSourcesDisabled_QueriesNothing_AndReportsZeroQueried()
    {
        var libris = new TrackingSource("LibrisKB");
        var google = new TrackingSource("GoogleBooks");
        var service = new FilteringMetadataLookupService(
            [libris, google],
            new FixedSettingsService("false"),
            new GoogleBooksApiKeyAccessor(),
            NullLogger<FilteringMetadataLookupService>.Instance);

        var lookup = await service.FetchAllSourcesAsync("9780451526538", CancellationToken.None);

        Assert.Empty(lookup.Results);
        Assert.Equal(0, lookup.SourcesQueried);
        Assert.Equal(0, lookup.SourcesFailed);
        Assert.False(libris.WasCalled);
        Assert.False(google.WasCalled);
    }

    [Fact]
    public async Task FetchAllSourcesAsync_NoSettingsStored_QueriesEverySourceByDefault()
    {
        var libris = new TrackingSource("LibrisKB");
        var google = new TrackingSource("GoogleBooks");
        var service = new FilteringMetadataLookupService(
            [libris, google],
            new FixedSettingsService(null),
            new GoogleBooksApiKeyAccessor(),
            NullLogger<FilteringMetadataLookupService>.Instance);

        var lookup = await service.FetchAllSourcesAsync("9780451526538", CancellationToken.None);

        Assert.Equal(2, lookup.SourcesQueried);
        Assert.True(libris.WasCalled);
        Assert.True(google.WasCalled);
    }
}
