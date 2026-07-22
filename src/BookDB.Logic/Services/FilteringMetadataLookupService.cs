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
    private readonly IGoogleBooksApiKeyAccessor _googleApiKey;
    private readonly ILogger<FilteringMetadataLookupService> _logger;

    public FilteringMetadataLookupService(
        IEnumerable<IMetadataSource> sources,
        ISettingsService settingsService,
        IGoogleBooksApiKeyAccessor googleApiKey,
        ILogger<FilteringMetadataLookupService> logger)
    {
        _sources = sources;
        _settingsService = settingsService;
        _googleApiKey = googleApiKey;
        _logger = logger;
    }

    public async Task<MetadataLookupResult> FetchAllSourcesAsync(
        string isbn, CancellationToken ct = default)
    {
        var normalizedIsbn = IsbnNormalizer.Normalize(isbn);

        // Refresh the Google Books key from settings so the client picks up changes without a restart.
        var apiKey = (await _settingsService.GetAsync("LookupApiKey.GoogleBooks", ct))?.Trim();
        _googleApiKey.ApiKey = string.IsNullOrEmpty(apiKey) ? null : apiKey;

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

        var outcomes = await Task.WhenAll(
            enabledSources.Select(s => MetadataSourceFetch.SafeAsync(s, normalizedIsbn, _logger, ct)));
        var statuses = outcomes.Select(o => o.Status).ToList();
        return new MetadataLookupResult(
            outcomes.Where(o => o.Result is not null).Select(o => o.Result!).ToList(),
            enabledSources.Count,
            statuses.Count(s => s.Outcome is SourceLookupOutcome.Error or SourceLookupOutcome.RateLimited),
            statuses);
    }

    private static bool ParseBool(string? value, bool defaultValue)
        => value is null ? defaultValue : bool.TryParse(value, out var result) ? result : defaultValue;
}
