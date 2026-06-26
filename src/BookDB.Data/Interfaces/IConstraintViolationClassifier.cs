using System;

namespace BookDB.Data.Interfaces;

/// <summary>
/// Decides whether a failed write was rejected by a foreign-key (referential) constraint — e.g. deleting a
/// borrower that still has loan history. The engine-specific error code lives behind this seam so
/// <c>BookDB.Logic</c> need not reference any database driver. Registered per backend.
/// </summary>
public interface IConstraintViolationClassifier
{
    /// <summary>True when the exception chain indicates a foreign-key violation (a dependent row blocks the write).</summary>
    bool IsForeignKeyViolation(Exception exception);
}
