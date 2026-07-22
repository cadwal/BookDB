using System.Collections.Generic;
using System.Linq;

namespace BookDB.Logic.Services;

/// <summary>
/// Thread-safe LRU implementation of <see cref="ICoverCache"/>, bounded by total byte size.
/// </summary>
public sealed class BoundedCoverCache : ICoverCache
{
    private readonly long _maxTotalBytes;
    private readonly object _gate = new();
    private readonly Dictionary<(int ItemId, string Source), LinkedListNode<Entry>> _entries = [];
    private readonly LinkedList<Entry> _lru = new();   // head = most recently used
    private long _totalBytes;

    private sealed record Entry(int ItemId, string Source, byte[] Bytes);

    public BoundedCoverCache(long maxTotalBytes = 32 * 1024 * 1024)
    {
        _maxTotalBytes = maxTotalBytes;
    }

    public byte[]? TryGet(int batchQueueItemId, string sourceName)
    {
        lock (_gate)
        {
            if (!_entries.TryGetValue((batchQueueItemId, sourceName), out var node))
                return null;
            _lru.Remove(node);
            _lru.AddFirst(node);
            return node.Value.Bytes;
        }
    }

    public void Set(int batchQueueItemId, string sourceName, byte[] bytes)
    {
        if (bytes.LongLength > _maxTotalBytes) return;

        lock (_gate)
        {
            var key = (batchQueueItemId, sourceName);
            if (_entries.Remove(key, out var existing))
            {
                _totalBytes -= existing.Value.Bytes.LongLength;
                _lru.Remove(existing);
            }

            _entries[key] = _lru.AddFirst(new Entry(batchQueueItemId, sourceName, bytes));
            _totalBytes += bytes.LongLength;

            while (_totalBytes > _maxTotalBytes && _lru.Last is { } oldest)
            {
                _entries.Remove((oldest.Value.ItemId, oldest.Value.Source));
                _totalBytes -= oldest.Value.Bytes.LongLength;
                _lru.RemoveLast();
            }
        }
    }

    public void RemoveItem(int batchQueueItemId)
    {
        lock (_gate)
        {
            foreach (var key in _entries.Keys.Where(k => k.ItemId == batchQueueItemId).ToList())
            {
                var node = _entries[key];
                _totalBytes -= node.Value.Bytes.LongLength;
                _lru.Remove(node);
                _entries.Remove(key);
            }
        }
    }
}
