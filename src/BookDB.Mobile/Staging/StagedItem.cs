using System;
using System.Collections.Generic;
using BookDB.Contracts;

namespace BookDB.Mobile.Staging;

/// <summary>
/// What the tray knows about a staged set without reading its photos back off disk: the number, which covers
/// it has, and a small thumbnail. The full-size JPEGs stay on disk until an upload asks for them — a tray of
/// fifty books would otherwise hold fifty full-resolution covers in memory.
/// </summary>
public sealed class StagedItem
{
    public required string ClientItemId { get; init; }

    public required string Isbn { get; init; }

    public required IReadOnlyList<ScanImageType> ImageTypes { get; init; }

    /// <summary>Orders the tray, and survives an edit so a re-taken book keeps its place in the queue.</summary>
    public required DateTimeOffset StagedAt { get; init; }

    /// <summary>Tray-sized JPEG of the front cover — or of whatever cover there is. Null for an ISBN-only set.</summary>
    public byte[]? Thumbnail { get; init; }
}
