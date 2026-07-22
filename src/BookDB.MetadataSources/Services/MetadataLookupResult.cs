using System.Collections.Generic;
using System.Linq;
using BookDB.Models.Metadata;

namespace BookDB.MetadataSources.Services;

/// <summary>
/// Outcome of querying the metadata sources for one ISBN. Besides the successful results it carries
/// how many sources were actually queried and how many of those errored, so a caller can tell
/// "no source had the book" apart from "the sources were unreachable" or "nothing was queried at all".
/// <see cref="SourceStatuses"/> adds the per-source detail — notably which sources were rate-limited.
/// </summary>
public sealed record MetadataLookupResult(
    IReadOnlyList<BookMetadata> Results,
    int SourcesQueried,
    int SourcesFailed,
    IReadOnlyList<SourceLookupStatus>? SourceStatuses = null)
{
    /// <summary>Names of sources that returned HTTP 429 this lookup, in query order. Empty when none.</summary>
    public IReadOnlyList<string> RateLimitedSources => NamesWithOutcome(SourceLookupOutcome.RateLimited);

    /// <summary>Names of sources that answered but had no record for the ISBN, in query order. Empty when none.</summary>
    public IReadOnlyList<string> NoResultSources => NamesWithOutcome(SourceLookupOutcome.NoResult);

    /// <summary>Names of sources that errored (non-429) this lookup, in query order. Empty when none.</summary>
    public IReadOnlyList<string> ErroredSources => NamesWithOutcome(SourceLookupOutcome.Error);

    private IReadOnlyList<string> NamesWithOutcome(SourceLookupOutcome outcome) =>
        (SourceStatuses ?? [])
            .Where(s => s.Outcome == outcome)
            .Select(s => s.SourceName)
            .ToList();
}
