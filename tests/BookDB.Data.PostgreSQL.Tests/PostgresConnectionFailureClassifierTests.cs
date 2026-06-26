using System;
using System.IO;
using System.Net.Sockets;
using BookDB.Data.PostgreSQL;
using Npgsql;
using Xunit;

namespace BookDB.Data.PostgreSQL.Tests;

public sealed class PostgresConnectionFailureClassifierTests
{
    private readonly PostgresConnectionFailureClassifier _classifier = new();

    [Fact]
    public void SocketException_IsConnectionLoss()
        => Assert.True(_classifier.IsConnectionLoss(new SocketException((int)SocketError.ConnectionReset)));

    [Fact]
    public void TimeoutException_IsConnectionLoss()
        => Assert.True(_classifier.IsConnectionLoss(new TimeoutException()));

    [Fact]
    public void IOException_IsConnectionLoss()
        => Assert.True(_classifier.IsConnectionLoss(new IOException("broken pipe")));

    [Fact]
    public void WrappedSocketException_IsConnectionLoss()
        => Assert.True(_classifier.IsConnectionLoss(
            new Exception("outer", new SocketException((int)SocketError.TimedOut))));

    [Fact]
    public void PostgresException_IsNotConnectionLoss_BecauseTheServerReplied()
    {
        // A unique-constraint violation: the connection is alive, so it must not trip the connection-loss path.
        var pg = new PostgresException("duplicate key", "ERROR", "ERROR", PostgresErrorCodes.UniqueViolation);
        Assert.False(_classifier.IsConnectionLoss(pg));
    }

    [Fact]
    public void OrdinaryException_IsNotConnectionLoss()
        => Assert.False(_classifier.IsConnectionLoss(new InvalidOperationException()));

    [Fact]
    public void Classify_DelegatesToProber()
        => Assert.Equal(
            BookDB.Data.Interfaces.ConnectionProbeStatus.Timeout,
            _classifier.Classify(new TimeoutException()));
}
