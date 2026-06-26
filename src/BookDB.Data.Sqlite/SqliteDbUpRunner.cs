using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using BookDB.Data;
using BookDB.Data.Interfaces;
using DbUp;
using Microsoft.Extensions.Logging;

namespace BookDB.Data.Sqlite;

/// <summary>
/// Applies the embedded SQLite migration script set via DbUp. Scans only this assembly, so its scripts
/// can never run against another engine.
/// </summary>
public sealed class SqliteDbUpRunner : IDbUpRunner
{
    private readonly string _connectionString;
    private readonly ILogger<DatabaseStartupService> _logger;

    public SqliteDbUpRunner(string connectionString, ILogger<DatabaseStartupService> logger)
    {
        _connectionString = connectionString;
        _logger = logger;
    }

    public async Task RunAsync(IProgress<(int applied, int total)> progress, CancellationToken ct)
    {
        var dbPath = ParseDbPath(_connectionString);
        if (!string.IsNullOrEmpty(dbPath))
        {
            var dir = Path.GetDirectoryName(dbPath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }
        }

        var upgrader = SqliteExtensions.SqliteDatabase(
                DeployChanges.To,
                _connectionString)
            .WithScriptsEmbeddedInAssembly(
                Assembly.GetAssembly(typeof(SqliteDbUpRunner))!,
                name => name.Contains(".Migrations."))
            .LogTo(new SerilogDbUpLog(_logger))
            .Build();

        var pendingCount = upgrader.GetScriptsToExecute().Count;
        progress.Report((0, pendingCount));

        // PerformUpgrade is synchronous and CPU/IO-bound. Run it off the calling thread so the splash
        // screen (which lives on the UI thread) keeps animating while migrations apply.
        var result = await Task.Run(() => upgrader.PerformUpgrade(), ct);
        if (!result.Successful)
        {
            throw new InvalidOperationException(
                $"Database upgrade failed: {result.Error?.Message}",
                result.Error);
        }

        progress.Report((pendingCount, pendingCount));

        _logger.LogWarning(
            "Database migration complete — {ScriptsExecuted} script(s) applied",
            result.Scripts.Count());
    }

    private static string ParseDbPath(string connectionString)
    {
        foreach (var part in connectionString.Split(';'))
        {
            var trimmed = part.Trim();
            if (trimmed.StartsWith("Data Source=", StringComparison.OrdinalIgnoreCase))
            {
                return trimmed.Substring("Data Source=".Length);
            }
        }

        return string.Empty;
    }
}
