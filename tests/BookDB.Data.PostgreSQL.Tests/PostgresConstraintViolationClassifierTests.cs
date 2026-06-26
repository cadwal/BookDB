using System;
using BookDB.Data.PostgreSQL;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace BookDB.Data.PostgreSQL.Tests;

public sealed class PostgresConstraintViolationClassifierTests
{
    private readonly PostgresConstraintViolationClassifier _classifier = new();

    private static DbUpdateException Wrap(string sqlState) =>
        new("update failed", new PostgresException("violation", "ERROR", "ERROR", sqlState));

    [Fact]
    public void ForeignKeyViolation_IsDetected()
        => Assert.True(_classifier.IsForeignKeyViolation(Wrap(PostgresErrorCodes.ForeignKeyViolation)));

    [Fact]
    public void UniqueViolation_IsNotForeignKey()
        => Assert.False(_classifier.IsForeignKeyViolation(Wrap(PostgresErrorCodes.UniqueViolation)));

    [Fact]
    public void BarePostgresException_IsNotForeignKey_BecauseItIsNotAnEfUpdateFailure()
        => Assert.False(_classifier.IsForeignKeyViolation(
            new PostgresException("violation", "ERROR", "ERROR", PostgresErrorCodes.ForeignKeyViolation)));

    [Fact]
    public void OrdinaryException_IsNotForeignKey()
        => Assert.False(_classifier.IsForeignKeyViolation(new InvalidOperationException()));
}
