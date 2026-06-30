using System;
using BookDB.Data.Interfaces;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;

namespace BookDB.Data.MySql;

/// <summary>
/// MySQL/MariaDB constraint classification (see <see cref="IConstraintViolationClassifier"/>). Deleting a row a
/// foreign key still references surfaces as a <see cref="MySqlException"/> with error <c>1451</c>
/// (ER_ROW_IS_REFERENCED_2 → <see cref="MySqlErrorCode.RowIsReferenced2"/>) — the borrower loan-history delete
/// guard depends on this.
/// </summary>
public sealed class MySqlConstraintViolationClassifier : IConstraintViolationClassifier
{
    public bool IsForeignKeyViolation(Exception exception) =>
        exception is DbUpdateException
        {
            InnerException: MySqlException { ErrorCode: MySqlErrorCode.RowIsReferenced2 }
        };
}
