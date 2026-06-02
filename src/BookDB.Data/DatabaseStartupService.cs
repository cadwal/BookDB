using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using BookDB.Models;
using DbUp;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BookDB.Data;

public sealed class DatabaseStartupService : IHostedService
{
    private readonly string _connectionString;
    private readonly ILogger<DatabaseStartupService> _logger;
    private readonly IStartupProgressReporter _progress;

    public DatabaseStartupService(
        string connectionString,
        ILogger<DatabaseStartupService> logger,
        IStartupProgressReporter progress)
    {
        _connectionString = connectionString;
        _logger = logger;
        _progress = progress;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
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
                Assembly.GetAssembly(typeof(DatabaseStartupService))!,
                name => name.Contains(".Migrations."))
            .LogTo(new SerilogDbUpLog(_logger))
            .Build();

        var pendingCount = upgrader.GetScriptsToExecute().Count;
        _progress.Report(StartupStage.ApplyingMigrations, 0, pendingCount);

        // PerformUpgrade is synchronous and CPU/IO-bound. Run it off the calling thread so the
        // splash screen (which lives on the UI thread) keeps animating while migrations apply.
        var result = await Task.Run(() => upgrader.PerformUpgrade(), cancellationToken);
        if (!result.Successful)
        {
            throw new InvalidOperationException(
                $"Database upgrade failed: {result.Error?.Message}",
                result.Error);
        }

        _progress.Report(StartupStage.ApplyingMigrations, pendingCount, pendingCount);

        _logger.LogWarning(
            "Database migration complete — {ScriptsExecuted} script(s) applied",
            result.Scripts.Count());
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
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
