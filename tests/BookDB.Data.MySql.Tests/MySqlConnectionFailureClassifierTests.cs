using System;
using System.IO;
using System.Net.Sockets;
using System.Security.Authentication;
using BookDB.Data.Interfaces;
using BookDB.Data.MySql;
using Xunit;

namespace BookDB.Data.MySql.Tests;

/// <summary>
/// Unit coverage for the transport-type branches of the MySQL connection-failure classifier (the
/// MySqlException error-code branches need a real server and are proven by the live container tests).
/// </summary>
public sealed class MySqlConnectionFailureClassifierTests
{
    private readonly MySqlConnectionFailureClassifier _classifier = new();

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
    public void EndOfStreamException_IsConnectionLoss()
        => Assert.True(_classifier.IsConnectionLoss(new EndOfStreamException()));

    [Fact]
    public void WrappedSocketException_IsConnectionLoss()
        => Assert.True(_classifier.IsConnectionLoss(
            new Exception("outer", new SocketException((int)SocketError.TimedOut))));

    [Fact]
    public void OrdinaryException_IsNotConnectionLoss()
        => Assert.False(_classifier.IsConnectionLoss(new InvalidOperationException()));

    [Fact]
    public void Classify_Timeout()
        => Assert.Equal(ConnectionProbeStatus.Timeout, _classifier.Classify(new TimeoutException()));

    [Fact]
    public void Classify_ConnectionRefused()
        => Assert.Equal(
            ConnectionProbeStatus.ConnectionRefused,
            _classifier.Classify(new SocketException((int)SocketError.ConnectionRefused)));

    [Fact]
    public void Classify_TlsError()
        => Assert.Equal(ConnectionProbeStatus.TlsError, _classifier.Classify(new AuthenticationException()));

    [Fact]
    public void Classify_Unknown_ForUnrecognisedFailure()
        => Assert.Equal(ConnectionProbeStatus.Unknown, _classifier.Classify(new InvalidOperationException()));
}
