using System;
using System.Collections.Generic;

namespace BookDB.Logic.Import;

/// <summary>
/// Bundles all 16 lookup caches used during a single import batch.
/// Populated by PreloadCachesAsync; consumed by BuildBookEntityAsync,
/// AddContributorsAsync, and AddCategoriesAsync.
/// </summary>
public sealed class ImportLookupCaches
{
    // FK lookups — 14 entity types
    public Dictionary<string, int> PublisherCache { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, int> SeriesCache { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, int> FormatCache { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, int> EditionCache { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, int> LanguageCache { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, int> ConditionCache { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, int> LocationCache { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, int> OwnerCache { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, int> StatusCache { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, int> SourceCache { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, int> PurchasePlaceCache { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, int> RatingCache { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, int> ReadingLevelCache { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, int> RoleCache { get; } = new(StringComparer.OrdinalIgnoreCase);

    // Contributor and category caches — 2
    public Dictionary<string, int> PersonCache { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, int> CategoryCache { get; } = new(StringComparer.OrdinalIgnoreCase);

    // Sentinel flag: PersonCache is populated lazily in AddContributorsAsync (not in PreloadCachesAsync)
    // because the People table may be large and is only needed when contributors are present.
    // Use this flag rather than Count == 0 to detect whether the bulk-load has already run.
    public bool PersonCacheLoaded { get; set; }
}
