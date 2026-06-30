using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace BookDB.Logic.Tests;

/// <summary>
/// Permanent grep gate: provider-specific SQL idioms and drivers must stay inside their own provider project. A
/// SQLite collation (<c>NOCASE</c>) or a <c>UseSqlite</c>/<c>UseNpgsql</c>/<c>UseMySql</c> call — or the
/// <c>MySqlConnector</c> driver — leaking into Logic or the shared Data layer silently breaks the other backends,
/// exactly the case-insensitive lookup bug this guards against. Scoped to <c>src</c> (test fixtures legitimately
/// reference every provider).
/// </summary>
public sealed class ProviderIsolationGuardTests
{
    private const string SqliteProject = "BookDB.Data.Sqlite";
    private const string PostgresProject = "BookDB.Data.PostgreSQL";
    private const string MySqlProject = "BookDB.Data.MySql";

    public static IEnumerable<object[]> Gates() => new[]
    {
        // token, the only project allowed to contain it
        new object[] { "EF.Functions.Collate", SqliteProject },
        new object[] { "UseSqlite(", SqliteProject },
        new object[] { "EF.Functions.ILike", PostgresProject },
        new object[] { "UseNpgsql(", PostgresProject },
        new object[] { "UseMySql(", MySqlProject },
        new object[] { "MySqlConnector", MySqlProject },
    };

    [Theory]
    [MemberData(nameof(Gates))]
    public void ProviderToken_StaysInHomeProject(string token, string homeProject)
    {
        var srcDir = FindSourceDir();
        var offenders = Directory.EnumerateFiles(srcDir, "*.cs", SearchOption.AllDirectories)
            .Where(file => File.ReadAllText(file).Contains(token, StringComparison.Ordinal))
            .Select(file => Path.GetRelativePath(srcDir, file))
            .Where(rel => !rel.Replace('\\', '/').StartsWith(homeProject + "/", StringComparison.Ordinal))
            .ToList();

        Assert.True(offenders.Count == 0,
            $"'{token}' must only appear in {homeProject}. Provider-leaking files: {string.Join(", ", offenders)}");
    }

    private static string FindSourceDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var src = Path.Combine(dir.FullName, "src");
            if (Directory.Exists(Path.Combine(src, "BookDB.Desktop")))
                return src;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Could not locate the repository's src directory from the test base path.");
    }
}
