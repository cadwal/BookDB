using System;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Threading.Tasks;
using BookDB.Data.Interfaces;
using BookDB.Data.MySql;
using BookDB.Models;
using Microting.EntityFrameworkCore.MySql.Infrastructure;
using MySqlConnector;
using Xunit;

namespace BookDB.Data.MySql.Tests;

/// <summary>
/// DB-free coverage of the prober's failure classification and the two-family version floor (MySQL 8.0 /
/// MariaDB 10.6 have independent minimums, so the family must be taken into account).
/// </summary>
public sealed class MySqlConnectionProberClassifyTests
{
    [Fact]
    public void Classify_Timeout_ReturnsTimeout()
        => Assert.Equal(ConnectionProbeStatus.Timeout, MySqlConnectionProber.Classify(new TimeoutException()));

    [Fact]
    public void Classify_ConnectionRefused_ReturnsConnectionRefused()
        => Assert.Equal(
            ConnectionProbeStatus.ConnectionRefused,
            MySqlConnectionProber.Classify(new SocketException((int)SocketError.ConnectionRefused)));

    [Fact]
    public void Classify_AuthenticationException_ReturnsTlsError()
        => Assert.Equal(ConnectionProbeStatus.TlsError, MySqlConnectionProber.Classify(new AuthenticationException()));

    [Fact]
    public void Classify_WrappedTimeout_IsFoundInInnerChain()
        => Assert.Equal(
            ConnectionProbeStatus.Timeout,
            MySqlConnectionProber.Classify(new Exception("outer", new TimeoutException())));

    [Fact]
    public void Classify_UnrecognisedException_ReturnsUnknown()
        => Assert.Equal(ConnectionProbeStatus.Unknown, MySqlConnectionProber.Classify(new InvalidOperationException()));

    [Theory]
    [InlineData(5, 7)]   // MySQL 5.7 predates the InnoDB FULLTEXT/collation feature floor
    [InlineData(8, 0)]
    [InlineData(8, 4)]
    public void IsSupportedVersion_MySql(int major, int minor)
        => Assert.Equal(
            major > 8 || (major == 8 && minor >= 0),
            MySqlConnectionProber.IsSupportedVersion(ServerType.MySql, new Version(major, minor)));

    [Theory]
    [InlineData(10, 5, false)]  // MariaDB 10.5 is below the floor
    [InlineData(10, 6, true)]
    [InlineData(11, 4, true)]
    public void IsSupportedVersion_MariaDb(int major, int minor, bool expected)
        => Assert.Equal(expected, MySqlConnectionProber.IsSupportedVersion(ServerType.MariaDb, new Version(major, minor)));
}

/// <summary>
/// Probes a live container: a clean probe returns the version with no book count (bare server, no BookDB schema),
/// and a wrong password is classified as an authentication failure. Run on both engines via the subclasses.
/// </summary>
public abstract class MySqlConnectionProberContainerTests
{
    private readonly MySqlTestDbFixture _fixture;

    protected MySqlConnectionProberContainerTests(MySqlTestDbFixture fixture) => _fixture = fixture;

    private (MySqlOptions options, string? password) OptionsFromFixture()
    {
        var builder = new MySqlConnectionStringBuilder(_fixture.ConnectionString);
        var options = new MySqlOptions
        {
            Host = builder.Server,
            Port = (int)builder.Port,
            Database = builder.Database,
            Username = builder.UserID,
            SslMode = builder.SslMode.ToString(),
        };
        return (options, builder.Password);
    }

    [Fact]
    public async Task ProbeAsync_AgainstContainer_Succeeds_WithVersionAndNoBookCount()
    {
        Assert.SkipUnless(_fixture.IsAvailable, _fixture.SkipReason);
        var (options, password) = OptionsFromFixture();

        var result = await new MySqlConnectionProber()
            .ProbeAsync(options, password, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.ErrorDetail);
        Assert.False(string.IsNullOrWhiteSpace(result.ServerVersion));
        Assert.Null(result.BookCount); // bare container has no BookDB schema yet
    }

    [Fact]
    public async Task ProbeAsync_WithWrongPassword_ReturnsAuthenticationFailed()
    {
        Assert.SkipUnless(_fixture.IsAvailable, _fixture.SkipReason);
        var (options, password) = OptionsFromFixture();

        var result = await new MySqlConnectionProber()
            .ProbeAsync(options, password + "-wrong", TestContext.Current.CancellationToken);

        Assert.Equal(ConnectionProbeStatus.AuthenticationFailed, result.Status);
    }
}

public sealed class MySqlServerConnectionProberTests : MySqlConnectionProberContainerTests, IClassFixture<MySqlServerFixture>
{
    public MySqlServerConnectionProberTests(MySqlServerFixture fixture) : base(fixture) { }
}

public sealed class MariaDbConnectionProberTests : MySqlConnectionProberContainerTests, IClassFixture<MariaDbFixture>
{
    public MariaDbConnectionProberTests(MariaDbFixture fixture) : base(fixture) { }
}
