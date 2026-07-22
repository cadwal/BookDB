namespace BookDB.MetadataSources.Services;

/// <summary>How one metadata source answered a single ISBN lookup.</summary>
public enum SourceLookupOutcome
{
    /// <summary>The source returned metadata.</summary>
    Success,

    /// <summary>The source answered but had no record for the ISBN.</summary>
    NoResult,

    /// <summary>The source returned HTTP 429 after its retry pipeline gave up.</summary>
    RateLimited,

    /// <summary>The source errored (network failure, bad payload, non-429 HTTP error, …).</summary>
    Error,
}

/// <summary>Per-source outcome for one lookup, so a caller can tell <em>which</em> source did what —
/// in particular which were rate-limited — instead of only an aggregate failure count.</summary>
public sealed record SourceLookupStatus(string SourceName, SourceLookupOutcome Outcome);
