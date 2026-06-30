using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using BookDB.Data;
using BookDB.Data.Interfaces;
using DbUp;
using Microsoft.Extensions.Logging;

namespace BookDB.Data.MySql;

/// <summary>
/// Applies the embedded MySQL/MariaDB migration script set via DbUp. Scans only this assembly, so its scripts
/// can never run against another engine.
/// </summary>
public sealed class MySqlDbUpRunner : IDbUpRunner
{
    private readonly string _connectionString;
    private readonly ILogger<DatabaseStartupService> _logger;

    public MySqlDbUpRunner(string connectionString, ILogger<DatabaseStartupService> logger)
    {
        _connectionString = connectionString;
        _logger = logger;
    }

    public async Task RunAsync(IProgress<(int applied, int total)> progress, CancellationToken ct)
    {
        var upgrader = MySqlExtensions.MySqlDatabase(
                DeployChanges.To,
                _connectionString)
            .WithScriptsEmbeddedInAssembly(
                Assembly.GetAssembly(typeof(MySqlDbUpRunner))!,
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
}
