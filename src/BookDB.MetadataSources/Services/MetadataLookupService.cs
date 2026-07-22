using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BookDB.Models;
using BookDB.Models.Metadata;
using BookDB.MetadataSources.Sources;
using Microsoft.Extensions.Logging;

namespace BookDB.MetadataSources.Services;

public sealed class MetadataLookupService : IMetadataLookupService
{
    private readonly IEnumerable<IMetadataSource> _sources;
    private readonly ILogger<MetadataLookupService> _logger;

    public MetadataLookupService(
        IEnumerable<IMetadataSource> sources,
        ILogger<MetadataLookupService> logger)
    {
        _sources = sources;
        _logger = logger;
    }

    public async Task<MetadataLookupResult> FetchAllSourcesAsync(
        string isbn, CancellationToken ct = default)
    {
        var normalizedIsbn = IsbnNormalizer.Normalize(isbn);
        var sources = _sources.ToList();
        var outcomes = await Task.WhenAll(
            sources.Select(s => MetadataSourceFetch.SafeAsync(s, normalizedIsbn, _logger, ct)));
        var statuses = outcomes.Select(o => o.Status).ToList();
        return new MetadataLookupResult(
            outcomes.Where(o => o.Result is not null).Select(o => o.Result!).ToList(),
            sources.Count,
            statuses.Count(s => s.Outcome is SourceLookupOutcome.Error or SourceLookupOutcome.RateLimited),
            statuses);
    }
}
