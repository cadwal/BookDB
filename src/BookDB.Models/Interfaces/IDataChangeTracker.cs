namespace BookDB.Models.Interfaces;

/// <summary>
/// Tracks whether anything has been written to the database this session. A process-lifetime singleton shared
/// by the EF Core command interceptor (which marks changes on any INSERT/UPDATE/DELETE) and the backup service
/// (which reads it and resets it after a backup). Used to decide whether the auto-backup on exit needs to run.
/// </summary>
public interface IDataChangeTracker
{
    /// <summary>True if a write has occurred since the last <see cref="Reset"/>.</summary>
    bool HasChanges { get; }

    /// <summary>Flags that data changed.</summary>
    void MarkChanged();

    /// <summary>Clears the flag — called after a successful backup.</summary>
    void Reset();
}
