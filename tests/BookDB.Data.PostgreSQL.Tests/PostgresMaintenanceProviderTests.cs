using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BookDB.Data;
using BookDB.Data.DbContexts;
using BookDB.Data.Interfaces;
using BookDB.Models;
using BookDB.Models.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BookDB.Data.PostgreSQL.Tests;

/// <summary>
/// Verifies the PostgreSQL maintenance provider against a live container: the sanity/connectivity
/// check, the VACUUM (ANALYZE) optimize pass, and that pg_database_size yields a non-zero size — each via the
/// real AddPostgresProvider registration.
/// </summary>
public sealed class PostgresMaintenanceProviderTests : IClassFixture<PostgresTestDbFixture>
{
    private readonly PostgresTestDbFixture _fixture;

    public PostgresMaintenanceProviderTests(PostgresTestDbFixture fixture) => _fixture = fixture;

    // Synchronous IProgress so reported steps are observable immediately (Progress<T> marshals asynchronously).
    private sealed class StepCollector : IProgress<MaintenanceStep>
    {
        public List<MaintenanceStep> Steps { get; } = new();
        public void Report(MaintenanceStep value) => Steps.Add(value);
    }

    private async Task<(ServiceProvider sp, IMaintenanceProvider provider)> BuildAsync(CancellationToken ct)
    {
        var runner = new PostgresDbUpRunner(_fixture.ConnectionString, NullLogger<DatabaseStartupService>.Instance);
        await runner.RunAsync(new Progress<(int applied, int total)>(), ct);

        var services = new ServiceCollection();
        services.AddSingleton<IDataChangeTracker, DataChangeTracker>();
        services.AddPostgresProvider(_fixture.ConnectionString);
        var sp = services.BuildServiceProvider();
        return (sp, sp.GetRequiredService<IMaintenanceProvider>());
    }

    [Fact]
    public async Task CheckIntegrity_ReturnsOk_WithVersionAndCounts()
    {
        Assert.SkipUnless(_fixture.IsAvailable, _fixture.SkipReason);
        var ct = TestContext.Current.CancellationToken;
        var (sp, provider) = await BuildAsync(ct);
        await using var scope = sp;

        var steps = new StepCollector();
        var result = await provider.CheckIntegrityAsync(steps, ct);

        Assert.Equal(MaintenanceCheckStatus.Ok, result.Status);
        Assert.Empty(result.ForeignKeyViolations);
        Assert.Contains(result.IntegrityMessages, m => m.Contains("PostgreSQL", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.IntegrityMessages, m => m.StartsWith("Book:", StringComparison.Ordinal));
        Assert.Contains(MaintenanceStep.CheckingIntegrity, steps.Steps);
    }

    [Fact]
    public async Task OptimizeAndRepair_Succeeds_ReportsVacuum_AndNonZeroSize()
    {
        Assert.SkipUnless(_fixture.IsAvailable, _fixture.SkipReason);
        var ct = TestContext.Current.CancellationToken;
        var (sp, provider) = await BuildAsync(ct);
        await using var scope = sp;

        var steps = new StepCollector();
        var result = await provider.OptimizeAndRepairAsync(steps, ct);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Null(result.ErrorMessage);
        Assert.True(result.SizeBeforeBytes > 0, "pg_database_size should be non-zero before VACUUM.");
        Assert.True(result.SizeAfterBytes > 0, "pg_database_size should be non-zero after VACUUM.");
        Assert.Contains(MaintenanceStep.Vacuum, steps.Steps);
    }
}
