using System;
using BookDB.Data.Interfaces;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BookDB.Data.PostgreSQL;

/// <summary>
/// PostgreSQL constraint classification (see <see cref="IConstraintViolationClassifier"/>). A foreign-key
/// violation surfaces as a <see cref="PostgresException"/> with SQLSTATE <c>23503</c>.
/// </summary>
public sealed class PostgresConstraintViolationClassifier : IConstraintViolationClassifier
{
    public bool IsForeignKeyViolation(Exception exception) =>
        exception is DbUpdateException
        {
            InnerException: PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation }
        };
}
