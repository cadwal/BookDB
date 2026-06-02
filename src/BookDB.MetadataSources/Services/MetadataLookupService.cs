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

    public async Task<IReadOnlyList<BookMetadata>> FetchAllSourcesAsync(
        string isbn, CancellationToken ct = default)
    {
        var normalizedIsbn = IsbnNormalizer.Normalize(isbn);
        var tasks = _sources.Select(s => FetchSafeAsync(s, normalizedIsbn, ct));
        var results = await Task.WhenAll(tasks);
        return results.Where(r => r is not null).Select(r => r!).ToList();
    }

    private async Task<BookMetadata?> FetchSafeAsync(
        IMetadataSource source, string isbn, CancellationToken ct)
    {
        try
        {
            _logger.LogDebug("Fetching ISBN {Isbn} from {Source}", isbn, source.SourceName);
            var result = await source.FetchAsync(isbn, ct);
            if (result is null)
                _logger.LogInformation("Source {Source} returned no result for ISBN {Isbn}", source.SourceName, isbn);
            else
                _logger.LogInformation("Source {Source} returned result for ISBN {Isbn}: {Title}", source.SourceName, isbn, result.Title);
            return result;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // propagate — either item timeout or batch cancel
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Metadata source {Source} failed for ISBN {Isbn}: {Message}", source.SourceName, isbn, ex.Message);
            return null;
        }
    }
}
