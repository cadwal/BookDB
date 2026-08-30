using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BookDB.Isbn;
using BookDB.MetadataSources.Services;
using BookDB.Models;

namespace BookDB.Logic.Services;

/// <summary>
/// Answers a scanned ISBN without touching the batch queue: the library is asked first, and only an ISBN
/// the library does not have goes out to the metadata sources.
/// </summary>
public sealed class CompanionPreviewService : ICompanionPreviewService
{
    /// <summary>
    /// The phone is holding a camera open while this runs, so a slow source is dropped rather than waited
    /// out — a missing title costs the confidence line, nothing more.
    /// </summary>
    private static readonly TimeSpan LookupTimeout = TimeSpan.FromSeconds(6);

    private const int CachedLookupLimit = 128;

    private readonly IBookMetadataService _metadata;
    private readonly IBookService _books;
    private readonly IMetadataLookupService _lookup;

    private readonly object _gate = new();
    private readonly Dictionary<string, LinkedListNode<CachedLookup>> _cached = [];
    private readonly LinkedList<CachedLookup> _recency = new();

    private sealed record CachedLookup(string Isbn, string? Title, string? Authors);

    public CompanionPreviewService(
        IBookMetadataService metadata,
        IBookService books,
        IMetadataLookupService lookup)
    {
        _metadata = metadata;
        _books = books;
        _lookup = lookup;
    }

    public async Task<IsbnPreviewResult> PreviewIsbnAsync(string isbn, CancellationToken ct = default)
    {
        string normalized = IsbnNormalizer.Normalize(isbn ?? "");
        if (normalized.Length == 0)
        {
            return new IsbnPreviewResult(false, null, null, null, IsbnPreviewOrigin.None);
        }

        // Never cached: the answer changes the moment the user adds the book, which is exactly what the
        // phone is doing between two scans of the same shelf.
        var owned = await _metadata.FindBookByIsbnAsync(normalized, ct).ConfigureAwait(false);
        if (owned is not null)
        {
            var (rows, _, _) = await _books
                .GetBooksAsync(null, [owned.BookId], null, null, true, 0, 1, false, ct)
                .ConfigureAwait(false);
            var row = rows.FirstOrDefault();

            return new IsbnPreviewResult(
                true, owned.BookId, row?.Title ?? owned.Title, row?.AuthorDisplay, IsbnPreviewOrigin.Library);
        }

        if (TryGetCachedLookup(normalized, out var cached))
        {
            return new IsbnPreviewResult(false, null, cached.Title, cached.Authors, IsbnPreviewOrigin.Lookup);
        }

        var looked = await LookUpAsync(normalized, ct).ConfigureAwait(false);
        if (looked is null)
        {
            return new IsbnPreviewResult(false, null, null, null, IsbnPreviewOrigin.None);
        }

        Cache(looked);
        return new IsbnPreviewResult(false, null, looked.Title, looked.Authors, IsbnPreviewOrigin.Lookup);
    }

    private async Task<CachedLookup?> LookUpAsync(string isbn, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(LookupTimeout);

        try
        {
            var result = await _lookup.FetchAllSourcesAsync(isbn, timeout.Token).ConfigureAwait(false);
            var best = result.Results.FirstOrDefault(r => !string.IsNullOrWhiteSpace(r.Title));
            if (best is null)
            {
                return null;
            }

            string? authors = best.Authors.Count > 0 ? string.Join(", ", best.Authors) : null;
            return new CachedLookup(isbn, best.Title, authors);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // The lookup outran its own timeout; the caller is still waiting and gets "no title".
            return null;
        }
    }

    private bool TryGetCachedLookup(string isbn, out CachedLookup cached)
    {
        lock (_gate)
        {
            if (!_cached.TryGetValue(isbn, out var node))
            {
                cached = default!;
                return false;
            }

            _recency.Remove(node);
            _recency.AddFirst(node);
            cached = node.Value;
            return true;
        }
    }

    private void Cache(CachedLookup lookup)
    {
        lock (_gate)
        {
            if (_cached.Remove(lookup.Isbn, out var existing))
            {
                _recency.Remove(existing);
            }

            _cached[lookup.Isbn] = _recency.AddFirst(lookup);

            while (_cached.Count > CachedLookupLimit && _recency.Last is { } oldest)
            {
                _cached.Remove(oldest.Value.Isbn);
                _recency.RemoveLast();
            }
        }
    }
}
