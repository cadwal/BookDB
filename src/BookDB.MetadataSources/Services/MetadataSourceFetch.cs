using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BookDB.Models.Metadata;
using BookDB.MetadataSources.Sources;
using Microsoft.Extensions.Logging;

namespace BookDB.MetadataSources.Services;

/// <summary>
/// Shared "fetch one source without letting it throw" logic used by every <see cref="IMetadataLookupService"/>
/// implementation, so the outcome classification — especially detecting HTTP 429 — cannot drift between them.
/// </summary>
public static class MetadataSourceFetch
{
    public static async Task<(BookMetadata? Result, SourceLookupStatus Status)> SafeAsync(
        IMetadataSource source, string isbn, ILogger logger, CancellationToken ct)
    {
        try
        {
            logger.LogDebug("Fetching ISBN {Isbn} from {Source}", isbn, source.SourceName);
            var result = await source.FetchAsync(isbn, ct);
            if (result is null)
                logger.LogInformation("Source {Source} returned no result for ISBN {Isbn}", source.SourceName, isbn);
            else
                logger.LogInformation("Source {Source} returned result for ISBN {Isbn}: {Title}", source.SourceName, isbn, result.Title);
            return (result, new SourceLookupStatus(source.SourceName,
                result is null ? SourceLookupOutcome.NoResult : SourceLookupOutcome.Success));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // propagate — either item timeout or batch cancel
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.TooManyRequests)
        {
            // 429 after the source's own retry pipeline gave up: record it distinctly so the source and the
            // "rate limited" reason survive instead of collapsing into a generic failure.
            logger.LogWarning("Metadata source {Source} was rate-limited (HTTP 429) for ISBN {Isbn}", source.SourceName, isbn);
            return (null, new SourceLookupStatus(source.SourceName, SourceLookupOutcome.RateLimited));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Metadata source {Source} failed for ISBN {Isbn}: {Message}", source.SourceName, isbn, ex.Message);
            return (null, new SourceLookupStatus(source.SourceName, SourceLookupOutcome.Error));
        }
    }
}
