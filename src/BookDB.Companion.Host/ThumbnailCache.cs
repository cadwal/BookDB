using System.Collections.Generic;

namespace BookDB.Companion.Host;

/// <summary>
/// Keeps browse thumbnails that have already been read from the database and shrunk, so paging back and
/// forth costs nothing. Bounded by total bytes and dropped when the host stops; a cover replaced on the
/// desktop mid-session can therefore show stale until then, which is the price of never re-decoding.
/// </summary>
public sealed class ThumbnailCache
{
    private readonly long _maxTotalBytes;
    private readonly object _gate = new();
    private readonly Dictionary<(int BookId, int MaxLongEdgePx), LinkedListNode<Entry>> _entries = [];
    private readonly LinkedList<Entry> _recency = new();
    private long _totalBytes;

    private sealed record Entry(int BookId, int MaxLongEdgePx, byte[] Bytes);

    public ThumbnailCache(long maxTotalBytes = 8 * 1024 * 1024)
    {
        _maxTotalBytes = maxTotalBytes;
    }

    public byte[]? TryGet(int bookId, int maxLongEdgePx)
    {
        lock (_gate)
        {
            if (!_entries.TryGetValue((bookId, maxLongEdgePx), out var node))
            {
                return null;
            }

            _recency.Remove(node);
            _recency.AddFirst(node);
            return node.Value.Bytes;
        }
    }

    public void Set(int bookId, int maxLongEdgePx, byte[] bytes)
    {
        if (bytes.LongLength > _maxTotalBytes)
        {
            return;
        }

        lock (_gate)
        {
            var key = (bookId, maxLongEdgePx);
            if (_entries.Remove(key, out var existing))
            {
                _totalBytes -= existing.Value.Bytes.LongLength;
                _recency.Remove(existing);
            }

            _entries[key] = _recency.AddFirst(new Entry(bookId, maxLongEdgePx, bytes));
            _totalBytes += bytes.LongLength;

            while (_totalBytes > _maxTotalBytes && _recency.Last is { } oldest)
            {
                _entries.Remove((oldest.Value.BookId, oldest.Value.MaxLongEdgePx));
                _totalBytes -= oldest.Value.Bytes.LongLength;
                _recency.RemoveLast();
            }
        }
    }
}
