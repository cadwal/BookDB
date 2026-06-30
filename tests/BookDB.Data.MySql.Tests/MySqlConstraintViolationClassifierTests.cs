using System;
using BookDB.Data.MySql;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BookDB.Data.MySql.Tests;

/// <summary>
/// Unit coverage for the non-foreign-key branches of the MySQL constraint classifier. The positive case (a real
/// MySqlException with code 1451) needs a live server and is proven by the live container tests.
/// </summary>
public sealed class MySqlConstraintViolationClassifierTests
{
    private readonly MySqlConstraintViolationClassifier _classifier = new();

    [Fact]
    public void OrdinaryException_IsNotForeignKey()
        => Assert.False(_classifier.IsForeignKeyViolation(new InvalidOperationException()));

    [Fact]
    public void DbUpdateException_WithNonMySqlInner_IsNotForeignKey()
        => Assert.False(_classifier.IsForeignKeyViolation(
            new DbUpdateException("update failed", new InvalidOperationException("not a driver error"))));
}
