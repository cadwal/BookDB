using System;
using System.Collections.Generic;
using BookDB.Contracts;

namespace BookDB.Mobile.Staging;

/// <summary>
/// The books scanned but not yet accepted by a computer. It is the app's only durable state besides the
/// pairing identity: a whole stack can be scanned with no network at all, and closing the app — or having it
/// killed — must not lose any of it.
/// </summary>
public interface IStagingStore
{
    /// <summary>Oldest first, which is the order the tray shows and an upload sends.</summary>
    IReadOnlyList<StagedItem> Items { get; }

    event EventHandler? Changed;

    /// <summary>Stages a set, or replaces one already staged under the same id — which is what re-taking a
    /// photo on an existing book does. A replaced set keeps its place in the queue.</summary>
    void Save(StagedBook book);

    void Remove(string clientItemId);

    /// <summary>Reads one full-size cover back, for an upload. Null when the set has no such cover.</summary>
    byte[]? ReadImage(string clientItemId, ScanImageType type);
}
