using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BookDB.MetadataSources.Services;
using BookDB.MetadataSources.Sources;
using BookDB.Models;
using BookDB.Models.Metadata;
using Microsoft.Extensions.Logging;

namespace BookDB.Logic.Services;

/// <summary>
/// Wraps IMetadataLookupService to honour the LookupEnabled.* settings flags.
/// Lives in BookDB.Logic so it can reference both ISettingsService (Logic) and
/// IMetadataSource (MetadataSources) without creating a circular dependency.
/// </summary>
public sealed class FilteringMetadataLookupService : IMetadataLookupService
{
    private readonly IEnumerable<IMetadataSource> _sources;
    private readonly ISettingsService _settingsService;
    private readonly ILogger<FilteringMetadataLookupService> _logger;

    public FilteringMetadataLookupService(
        IEnumerable<IMetadataSource> sources,
        ISettingsService settingsService,
        ILogger<FilteringMetadataLookupService> logger)
    {
        _sources = sources;
        _settingsService = settingsService;
        _logger = logger;
    }

    public async Task<IReadOnlyList<BookMetadata>> FetchAllSourcesAsync(
        string isbn, CancellationToken ct = default)
    {
        var normalizedIsbn = IsbnNormalizer.Normalize(isbn);

        var librisEnabled     = ParseBool(await _settingsService.GetAsync("LookupEnabled.LibrisKB",      ct), defaultValue: true);
        var googleEnabled     = ParseBool(await _settingsService.GetAsync("LookupEnabled.GoogleBooks",  ct), defaultValue: true);
        var openLibEnabled    = ParseBool(await _settingsService.GetAsync("LookupEnabled.OpenLibrary",  ct), defaultValue: true);
        var isbnSearchEnabled = ParseBool(await _settingsService.GetAsync("LookupEnabled.IsbnSearchOrg", ct), defaultValue: true);

        var enabledSources = _sources.Where(s => s.SourceName switch
        {
            "LibrisKB"      => librisEnabled,
            "GoogleBooks"   => googleEnabled,
            "OpenLibrary"   => openLibEnabled,
            "IsbnSearchOrg" => isbnSearchEnabled,
            _               => true
        }).ToList();

        var tasks = enabledSources.Select(s => FetchSafeAsync(s, normalizedIsbn, ct));
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
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Metadata source {Source} failed for ISBN {Isbn}: {Message}", source.SourceName, isbn, ex.Message);
            return null;
        }
    }

    private static bool ParseBool(string? value, bool defaultValue)
        => value is null ? defaultValue : bool.TryParse(value, out var result) ? result : defaultValue;
}
