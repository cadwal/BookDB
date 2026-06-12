using BookDB.Models.Interfaces;

namespace BookDB.Data;

/// <summary>
/// In-memory <see cref="IDataChangeTracker"/> for the process lifetime. A single bool flag; writes and reads
/// can come from different threads (EF on a background thread, the backup on shutdown), so it is volatile.
/// </summary>
public sealed class DataChangeTracker : IDataChangeTracker
{
    private volatile bool _hasChanges;

    public bool HasChanges => _hasChanges;

    public void MarkChanged() => _hasChanges = true;

    public void Reset() => _hasChanges = false;
}
