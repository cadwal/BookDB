using System;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Threading.Tasks;
using BookDB.Data.Interfaces;
using BookDB.Data.PostgreSQL;
using BookDB.Models;
using Npgsql;
using Xunit;

namespace BookDB.Data.PostgreSQL.Tests;

public sealed class PostgresConnectionProberClassifyTests
{
    [Fact]
    public void Classify_Timeout_ReturnsTimeout()
        => Assert.Equal(ConnectionProbeStatus.Timeout, PostgresConnectionProber.Classify(new TimeoutException()));

    [Fact]
    public void Classify_ConnectionRefused_ReturnsConnectionRefused()
        => Assert.Equal(
            ConnectionProbeStatus.ConnectionRefused,
            PostgresConnectionProber.Classify(new SocketException((int)SocketError.ConnectionRefused)));

    [Fact]
    public void Classify_AuthenticationException_ReturnsTlsError()
        => Assert.Equal(ConnectionProbeStatus.TlsError, PostgresConnectionProber.Classify(new AuthenticationException()));

    [Fact]
    public void Classify_WrappedTimeout_IsFoundInInnerChain()
        => Assert.Equal(
            ConnectionProbeStatus.Timeout,
            PostgresConnectionProber.Classify(new Exception("outer", new TimeoutException())));

    [Fact]
    public void Classify_UnrecognisedException_ReturnsUnknown()
        => Assert.Equal(ConnectionProbeStatus.Unknown, PostgresConnectionProber.Classify(new InvalidOperationException()));

    [Theory]
    [InlineData(11, 11)] // PostgreSQL 11 lacks the stored generated column the schema needs
    [InlineData(9, 6)]
    public void IsSupportedVersion_BelowMinimum_IsFalse(int major, int minor)
        => Assert.False(PostgresConnectionProber.IsSupportedVersion(new Version(major, minor)));

    [Theory]
    [InlineData(12, 0)] // first version with GENERATED ALWAYS AS … STORED
    [InlineData(16, 3)]
    public void IsSupportedVersion_AtOrAboveMinimum_IsTrue(int major, int minor)
        => Assert.True(PostgresConnectionProber.IsSupportedVersion(new Version(major, minor)));
}

public sealed class PostgresConnectionProberContainerTests : IClassFixture<PostgresTestDbFixture>
{
    private readonly PostgresTestDbFixture _fixture;

    public PostgresConnectionProberContainerTests(PostgresTestDbFixture fixture) => _fixture = fixture;

    private (PostgresOptions options, string? password) OptionsFromFixture()
    {
        var builder = new NpgsqlConnectionStringBuilder(_fixture.ConnectionString);
        var options = new PostgresOptions
        {
            Host = builder.Host!,
            Port = builder.Port,
            Database = builder.Database!,
            Username = builder.Username!,
            SslMode = "Disable", // the test container does not serve TLS
        };
        return (options, builder.Password);
    }

    [Fact]
    public async Task ProbeAsync_AgainstContainer_Succeeds_WithVersionAndNoBookCount()
    {
        Assert.SkipUnless(_fixture.IsAvailable, _fixture.SkipReason);
        var (options, password) = OptionsFromFixture();

        var result = await new PostgresConnectionProber()
            .ProbeAsync(options, password, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.False(string.IsNullOrWhiteSpace(result.ServerVersion));
        Assert.Null(result.BookCount); // bare container has no BookDB schema yet
    }

    [Fact]
    public async Task ProbeAsync_WithWrongPassword_ReturnsAuthenticationFailed()
    {
        Assert.SkipUnless(_fixture.IsAvailable, _fixture.SkipReason);
        var (options, password) = OptionsFromFixture();

        var result = await new PostgresConnectionProber()
            .ProbeAsync(options, password + "-wrong", TestContext.Current.CancellationToken);

        Assert.Equal(ConnectionProbeStatus.AuthenticationFailed, result.Status);
    }
}
