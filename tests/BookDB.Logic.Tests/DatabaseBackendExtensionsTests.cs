using BookDB.Models;
using Xunit;

namespace BookDB.Logic.Tests;

public sealed class DatabaseBackendExtensionsTests
{
    [Theory]
    [InlineData(DatabaseBackend.Sqlite, false)]
    [InlineData(DatabaseBackend.PostgreSql, true)]
    [InlineData(DatabaseBackend.MySql, true)]
    public void IsRemote_classifies_backends(DatabaseBackend backend, bool expected)
    {
        Assert.Equal(expected, backend.IsRemote());
    }
}
