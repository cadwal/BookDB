using BookDB.Models;
using MySqlConnector;
using Xunit;

namespace BookDB.Data.MySql.Tests;

/// <summary>
/// Unit tests for the connection-string factory: it maps the config.json server parameters onto a MySqlConnector
/// connection string, injects the password only when supplied, and redacts the password before logging.
/// </summary>
public sealed class MySqlConnectionStringFactoryTests
{
    private static readonly MySqlOptions SampleOptions = new()
    {
        Host = "db.example.com",
        Port = 3307,
        Database = "library",
        Username = "bookdb_user",
        SslMode = "Required",
    };

    [Fact]
    public void Build_MapsServerParameters_AndSslMode()
    {
        var cs = MySqlConnectionStringFactory.Build(SampleOptions);

        var parsed = new MySqlConnectionStringBuilder(cs);
        Assert.Equal("db.example.com", parsed.Server);
        Assert.Equal(3307u, parsed.Port);
        Assert.Equal("library", parsed.Database);
        Assert.Equal("bookdb_user", parsed.UserID);
        Assert.Equal(MySqlSslMode.Required, parsed.SslMode);
        Assert.True(parsed.ConnectionTimeout > 0, "An explicit connect timeout must be set.");
    }

    [Fact]
    public void Build_OmitsPassword_WhenNoneSupplied()
    {
        var cs = MySqlConnectionStringFactory.Build(SampleOptions);

        Assert.True(string.IsNullOrEmpty(new MySqlConnectionStringBuilder(cs).Password));
    }

    [Fact]
    public void Build_IncludesPassword_WhenSupplied()
    {
        var cs = MySqlConnectionStringFactory.Build(SampleOptions, "s3cr3t");

        Assert.Equal("s3cr3t", new MySqlConnectionStringBuilder(cs).Password);
    }

    [Fact]
    public void Build_FallsBackToPreferred_OnUnknownSslMode()
    {
        var options = new MySqlOptions { Host = "h", Username = "u", SslMode = "nonsense" };

        var cs = MySqlConnectionStringFactory.Build(options);

        Assert.Equal(MySqlSslMode.Preferred, new MySqlConnectionStringBuilder(cs).SslMode);
    }

    [Fact]
    public void Sanitize_RedactsPassword()
    {
        var cs = MySqlConnectionStringFactory.Build(SampleOptions, "topsecret");

        var sanitized = MySqlConnectionStringFactory.Sanitize(cs);

        Assert.DoesNotContain("topsecret", sanitized);
        Assert.NotEqual("topsecret", new MySqlConnectionStringBuilder(sanitized).Password);
        // The non-secret parameters survive so the log line stays useful.
        Assert.Equal("db.example.com", new MySqlConnectionStringBuilder(sanitized).Server);
    }

    [Fact]
    public void Sanitize_LeavesPasswordlessStringUnchanged()
    {
        var cs = MySqlConnectionStringFactory.Build(SampleOptions);

        var sanitized = MySqlConnectionStringFactory.Sanitize(cs);

        Assert.True(string.IsNullOrEmpty(new MySqlConnectionStringBuilder(sanitized).Password));
    }

    [Theory]
    [InlineData("Server=h;Password=secretpw;Database=d")]
    [InlineData("Server=h;Pwd=secretpw;Database=d")]
    public void Sanitize_RedactsPasswordAndAliases(string raw)
    {
        var sanitized = MySqlConnectionStringFactory.Sanitize(raw);

        Assert.DoesNotContain("secretpw", sanitized);
    }
}
