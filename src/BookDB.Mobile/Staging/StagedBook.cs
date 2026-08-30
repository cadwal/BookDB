using System.Collections.Generic;
using BookDB.Contracts;

namespace BookDB.Mobile.Staging;

/// <summary>One cover, already sized to the computer's capture settings and ready to go on the wire as-is.</summary>
public sealed class StagedImage
{
    public required ScanImageType Type { get; init; }

    public required byte[] Jpeg { get; init; }
}

/// <summary>
/// A finished scan set: the ISBN and whatever covers were captured for it. Photos are optional — a book can
/// be staged on its ISBN alone — so an empty image list is a valid set, not an incomplete one.
/// </summary>
public sealed class StagedBook
{
    /// <summary>Minted here, when the set is staged, because it is the phone's side of the correlation: the
    /// upload's header, its images and the status rows that come back all carry it.</summary>
    public required string ClientItemId { get; init; }

    public required string Isbn { get; init; }

    public required IReadOnlyList<StagedImage> Images { get; init; }
}
