using System;
using System.Collections.Generic;
using System.Linq;

namespace BookDB.Companion.Host;

/// <summary>
/// The upload sessions this host run knows about. Bounded in both count and age: a phone that walks away
/// mid-upload must not leave the desktop holding its state for the rest of the day.
/// </summary>
public sealed class CompanionBatchSessions
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromHours(2);
    private const int MaxSessions = 16;

    private readonly TimeProvider _clock;
    private readonly object _gate = new();
    private readonly Dictionary<string, CompanionBatchSession> _sessions = new(StringComparer.Ordinal);

    public CompanionBatchSessions(TimeProvider clock) => _clock = clock;

    public CompanionBatchSession GetOrCreate(string batchId)
    {
        var now = _clock.GetUtcNow();

        lock (_gate)
        {
            Prune(now);

            if (!_sessions.TryGetValue(batchId, out var session))
            {
                session = new CompanionBatchSession(batchId, now);
                _sessions[batchId] = session;
            }

            return session;
        }
    }

    private void Prune(DateTimeOffset now)
    {
        foreach (var stale in _sessions
            .Where(pair => now - pair.Value.LastActivityUtc > Lifetime)
            .Select(pair => pair.Key)
            .ToArray())
        {
            _sessions.Remove(stale);
        }

        while (_sessions.Count >= MaxSessions)
        {
            var oldest = _sessions.OrderBy(pair => pair.Value.LastActivityUtc).First();
            _sessions.Remove(oldest.Key);
        }
    }
}
