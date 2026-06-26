using System;
using BookDB.Data.Interfaces;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace BookDB.Data.Sqlite;

/// <summary>
/// SQLite constraint classification (see <see cref="IConstraintViolationClassifier"/>). A foreign-key violation
/// surfaces as SQLITE_CONSTRAINT (primary result code 19); on the delete paths this seam guards, that constraint
/// can only be a dependent reference such as loan history.
/// </summary>
public sealed class SqliteConstraintViolationClassifier : IConstraintViolationClassifier
{
    public bool IsForeignKeyViolation(Exception exception) =>
        exception is DbUpdateException { InnerException: SqliteException { SqliteErrorCode: 19 } };
}
