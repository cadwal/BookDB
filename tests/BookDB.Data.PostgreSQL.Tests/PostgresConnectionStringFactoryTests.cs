using BookDB.Models;
using Npgsql;
using Xunit;

namespace BookDB.Data.PostgreSQL.Tests;

/// <summary>
/// Unit tests for the connection-string factory: it maps the config.json server parameters onto an Npgsql
/// connection string, injects the password only when supplied, and redacts the password before logging.
/// </summary>
public sealed class PostgresConnectionStringFactoryTests
{
    private static readonly PostgresOptions SampleOptions = new()
    {
        Host = "db.example.com",
        Port = 6543,
        Database = "library",
        Username = "bookdb_user",
        SslMode = "Require",
    };

    [Fact]
    public void Build_MapsServerParameters_AndDefaultSslMode()
    {
        var cs = PostgresConnectionStringFactory.Build(SampleOptions);

        var parsed = new NpgsqlConnectionStringBuilder(cs);
        Assert.Equal("db.example.com", parsed.Host);
        Assert.Equal(6543, parsed.Port);
        Assert.Equal("library", parsed.Database);
        Assert.Equal("bookdb_user", parsed.Username);
        Assert.Equal(SslMode.Require, parsed.SslMode);
        Assert.True(parsed.Timeout > 0, "An explicit connect timeout must be set.");
    }

    [Fact]
    public void Build_OmitsPassword_WhenNoneSupplied()
    {
        var cs = PostgresConnectionStringFactory.Build(SampleOptions);

        Assert.True(string.IsNullOrEmpty(new NpgsqlConnectionStringBuilder(cs).Password));
    }

    [Fact]
    public void Build_IncludesPassword_WhenSupplied()
    {
        var cs = PostgresConnectionStringFactory.Build(SampleOptions, "s3cr3t");

        Assert.Equal("s3cr3t", new NpgsqlConnectionStringBuilder(cs).Password);
    }

    [Fact]
    public void Build_FallsBackToRequire_OnUnknownSslMode()
    {
        var options = new PostgresOptions { Host = "h", Username = "u", SslMode = "nonsense" };

        var cs = PostgresConnectionStringFactory.Build(options);

        Assert.Equal(SslMode.Require, new NpgsqlConnectionStringBuilder(cs).SslMode);
    }

    [Fact]
    public void Sanitize_RedactsPassword()
    {
        var cs = PostgresConnectionStringFactory.Build(SampleOptions, "topsecret");

        var sanitized = PostgresConnectionStringFactory.Sanitize(cs);

        Assert.DoesNotContain("topsecret", sanitized);
        Assert.NotEqual("topsecret", new NpgsqlConnectionStringBuilder(sanitized).Password);
        // The non-secret parameters survive so the log line stays useful.
        Assert.Equal("db.example.com", new NpgsqlConnectionStringBuilder(sanitized).Host);
    }

    [Fact]
    public void Sanitize_LeavesPasswordlessStringUnchanged()
    {
        var cs = PostgresConnectionStringFactory.Build(SampleOptions);

        var sanitized = PostgresConnectionStringFactory.Sanitize(cs);

        Assert.True(string.IsNullOrEmpty(new NpgsqlConnectionStringBuilder(sanitized).Password));
    }

    [Theory]
    [InlineData("Host=h;Password=secretpw;Database=d")]
    [InlineData("Host=h;Pwd=secretpw;Database=d")]
    public void Sanitize_RedactsPasswordAndAliases(string raw)
    {
        var sanitized = PostgresConnectionStringFactory.Sanitize(raw);

        Assert.DoesNotContain("secretpw", sanitized);
    }
}
