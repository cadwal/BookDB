using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BookDB.Data;
using BookDB.Data.DbContexts;
using BookDB.Data.Interfaces;
using BookDB.Models;
using BookDB.Models.Entities;
using BookDB.Models.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BookDB.Data.MySql.Tests;

/// <summary>
/// Verifies the MySQL/MariaDB maintenance provider against a live container: the sanity/connectivity check
/// (version + per-table counts, no PRAGMA), the OPTIMIZE + ANALYZE TABLE optimize pass, and that
/// information_schema yields a non-zero size — each via the real AddMySqlProvider registration. Run on both
/// engines via the subclasses at the bottom.
/// </summary>
public abstract class MySqlMaintenanceProviderTests
{
    private readonly MySqlTestDbFixture _fixture;

    protected MySqlMaintenanceProviderTests(MySqlTestDbFixture fixture) => _fixture = fixture;

    // Synchronous IProgress so reported steps are observable immediately (Progress<T> marshals asynchronously).
    private sealed class StepCollector : IProgress<MaintenanceStep>
    {
        public List<MaintenanceStep> Steps { get; } = new();
        public void Report(MaintenanceStep value) => Steps.Add(value);
    }

    private async Task<(ServiceProvider sp, IDbContextFactory<BookDbContext> factory, IMaintenanceProvider provider)>
        BuildAsync(CancellationToken ct)
    {
        var runner = new MySqlDbUpRunner(_fixture.ConnectionString, NullLogger<DatabaseStartupService>.Instance);
        await runner.RunAsync(new Progress<(int applied, int total)>(), ct);

        var services = new ServiceCollection();
        services.AddSingleton<IDataChangeTracker, DataChangeTracker>();
        services.AddMySqlProvider(_fixture.ConnectionString);
        var sp = services.BuildServiceProvider();
        return (sp, sp.GetRequiredService<IDbContextFactory<BookDbContext>>(), sp.GetRequiredService<IMaintenanceProvider>());
    }

    [Fact]
    public async Task CheckIntegrity_ReturnsOk_WithVersionAndCounts()
    {
        Assert.SkipUnless(_fixture.IsAvailable, _fixture.SkipReason);
        var ct = TestContext.Current.CancellationToken;
        var (sp, _, provider) = await BuildAsync(ct);
        await using var scope = sp;

        var steps = new StepCollector();
        var result = await provider.CheckIntegrityAsync(steps, ct);

        Assert.Equal(MaintenanceCheckStatus.Ok, result.Status);
        Assert.Empty(result.ForeignKeyViolations);
        Assert.Contains(result.IntegrityMessages, m => m.StartsWith("Server version:", StringComparison.Ordinal));
        // CHECK TABLE reports a sound table as "OK" — Book is a base table and must be present and healthy.
        Assert.Contains(result.IntegrityMessages, m => m.Equals("Book: OK", StringComparison.Ordinal));
        Assert.Contains(MaintenanceStep.CheckingIntegrity, steps.Steps);
        // The covered base tables are reported so the UI can list how many were checked.
        Assert.Contains("Book", result.TablesChecked);
    }

    [Fact]
    public async Task OptimizeAndRepair_Succeeds_ReportsStep_AndNonZeroSize()
    {
        Assert.SkipUnless(_fixture.IsAvailable, _fixture.SkipReason);
        var ct = TestContext.Current.CancellationToken;
        var (sp, factory, provider) = await BuildAsync(ct);
        await using var scope = sp;

        // Seed a row so the schema has data — keeps the optimize pass realistic on a fresh container.
        await using (var db = await factory.CreateDbContextAsync(ct))
        {
            db.Books.Add(new Book { Title = $"Maintenance {Guid.NewGuid():N}" });
            await db.SaveChangesAsync(ct);
        }

        var steps = new StepCollector();
        var result = await provider.OptimizeAndRepairAsync(steps, ct);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Null(result.ErrorMessage);
        Assert.True(result.SizeBeforeBytes > 0, "information_schema size should be non-zero before optimize.");
        Assert.True(result.SizeAfterBytes > 0, "information_schema size should be non-zero after optimize.");
        Assert.Contains(MaintenanceStep.Vacuum, steps.Steps);
        // The optimized base tables are reported so the UI can list how many were optimized.
        Assert.Contains("Book", result.TablesOptimized);
    }
}

public sealed class MySqlServerMaintenanceProviderTests : MySqlMaintenanceProviderTests, IClassFixture<MySqlServerFixture>
{
    public MySqlServerMaintenanceProviderTests(MySqlServerFixture fixture) : base(fixture) { }
}

public sealed class MariaDbMaintenanceProviderTests : MySqlMaintenanceProviderTests, IClassFixture<MariaDbFixture>
{
    public MariaDbMaintenanceProviderTests(MariaDbFixture fixture) : base(fixture) { }
}
