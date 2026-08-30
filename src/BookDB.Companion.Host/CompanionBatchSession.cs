using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Channels;
using BookDB.Contracts;

namespace BookDB.Companion.Host;

/// <summary>
/// What is known about one upload: where each item got to, which queue item is cataloguing it, and whoever
/// is currently watching. In memory only — after a desktop restart a phone re-reads outcomes from the
/// library instead.
/// </summary>
public sealed class CompanionBatchSession
{
    private readonly object _gate = new();
    private readonly Dictionary<string, BatchItemStatus> _items = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _queueItems = new(StringComparer.Ordinal);
    private readonly List<Channel<BatchItemStatus>> _watchers = [];

    public CompanionBatchSession(string batchId, DateTimeOffset createdUtc)
    {
        BatchId = batchId;
        LastActivityUtc = createdUtc;
    }

    public string BatchId { get; }

    public DateTimeOffset LastActivityUtc { get; private set; }

    /// <summary>
    /// Records where an item has got to and tells every watcher, unless nothing actually changed —
    /// polling would otherwise send the phone the same row every second.
    /// </summary>
    public void Report(
        string clientItemId,
        BatchItemState state,
        DateTimeOffset now,
        string? isbn = null,
        int? bookId = null,
        string? title = null,
        BatchItemFailure failure = BatchItemFailure.None,
        string? failureDetail = null)
    {
        BatchItemStatus status;

        lock (_gate)
        {
            LastActivityUtc = now;
            _items.TryGetValue(clientItemId, out var previous);

            status = new BatchItemStatus
            {
                ClientItemId = clientItemId,
                Isbn = isbn ?? previous?.Isbn ?? "",
                State = state,
                BookId = bookId ?? previous?.BookId,
                Title = title ?? previous?.Title,
                Failure = failure == BatchItemFailure.None ? previous?.Failure ?? failure : failure,
                FailureDetail = failureDetail ?? previous?.FailureDetail,
            };

            if (previous is not null && Same(previous, status))
            {
                return;
            }

            _items[clientItemId] = status;

            foreach (var watcher in _watchers)
            {
                watcher.Writer.TryWrite(status);
            }
        }
    }

    public void TrackQueueItem(string clientItemId, int batchQueueItemId)
    {
        lock (_gate)
        {
            _queueItems[clientItemId] = batchQueueItemId;
        }
    }

    public IReadOnlyList<BatchItemStatus> Snapshot()
    {
        lock (_gate)
        {
            return [.. _items.Values];
        }
    }

    public IReadOnlyList<(string ClientItemId, int BatchQueueItemId)> TrackedQueueItems()
    {
        lock (_gate)
        {
            return [.. _queueItems.Select(pair => (pair.Key, pair.Value))];
        }
    }

    /// <summary>True once every tracked item has an outcome, so there is nothing left worth polling for.</summary>
    public bool IsSettled()
    {
        lock (_gate)
        {
            return _queueItems.Keys.All(id =>
                _items.TryGetValue(id, out var status) && status.State is
                    BatchItemState.Done or BatchItemState.NeedsReview or BatchItemState.Failed);
        }
    }

    public Watcher Watch() => new(this);

    private void Remove(Channel<BatchItemStatus> channel)
    {
        lock (_gate)
        {
            _watchers.Remove(channel);
        }
    }

    private static bool Same(BatchItemStatus a, BatchItemStatus b)
        => a.State == b.State
        && a.BookId == b.BookId
        && a.Failure == b.Failure
        && string.Equals(a.Isbn, b.Isbn, StringComparison.Ordinal)
        && string.Equals(a.Title, b.Title, StringComparison.Ordinal)
        && string.Equals(a.FailureDetail, b.FailureDetail, StringComparison.Ordinal);

    /// <summary>A single connected phone. Subscribing before the snapshot is read is what stops an update
    /// slipping through the gap between the two.</summary>
    public sealed class Watcher : IDisposable
    {
        private readonly CompanionBatchSession _session;
        private readonly Channel<BatchItemStatus> _channel = Channel.CreateUnbounded<BatchItemStatus>();

        internal Watcher(CompanionBatchSession session)
        {
            _session = session;
            lock (session._gate)
            {
                session._watchers.Add(_channel);
            }
        }

        public ChannelReader<BatchItemStatus> Reader => _channel.Reader;

        public void Dispose() => _session.Remove(_channel);
    }
}
