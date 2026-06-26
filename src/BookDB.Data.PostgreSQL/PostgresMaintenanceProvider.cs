using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using BookDB.Data.DbContexts;
using BookDB.Data.Interfaces;
using BookDB.Models;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace BookDB.Data.PostgreSQL;

/// <summary>
/// PostgreSQL maintenance. Postgres has no <c>PRAGMA integrity_check</c> equivalent, so the
/// "check" is a connectivity + per-core-table <c>count(*)</c> sanity pass plus a server-version report; the
/// "optimize/repair" is <c>VACUUM (ANALYZE)</c> (not <c>VACUUM FULL</c>, which would take an exclusive lock on
/// a live shared database). Size before/after come from <c>pg_database_size</c>. The provider-neutral safety
/// backup is taken by <c>DatabaseMaintenanceService</c>, not here.
/// </summary>
public sealed class PostgresMaintenanceProvider : IMaintenanceProvider
{
    // Representative entity tables: querying these confirms the schema is connected and readable. Quoted to
    // match the PascalCase names the DDL created.
    private static readonly string[] CoreTables =
        ["Book", "BookImage", "BookContributor", "Person", "Publisher", "Borrower", "Loan"];

    private readonly IDbContextFactory<BookDbContext> _factory;

    public PostgresMaintenanceProvider(IDbContextFactory<BookDbContext> factory)
    {
        _factory = factory;
    }

    public async Task<MaintenanceCheckResult> CheckIntegrityAsync(
        IProgress<MaintenanceStep>? progress, CancellationToken ct)
    {
        var messages = new List<string>();
        try
        {
            await using var dbContext = await _factory.CreateDbContextAsync(ct);
            await dbContext.Database.OpenConnectionAsync(ct);
            var connection = dbContext.Database.GetDbConnection();

            progress?.Report(MaintenanceStep.CheckingIntegrity);

            messages.Add(await ScalarStringAsync(connection, "SELECT version()", ct));
            foreach (var table in CoreTables)
            {
                var count = await ScalarLongAsync(connection, $"SELECT count(*) FROM \"{table}\"", ct);
                messages.Add($"{table}: {count} rows");
            }

            Log.Information(
                "PostgresMaintenanceProvider: sanity check ok ({TableCount} core tables queried)", CoreTables.Length);

            // No PRAGMA foreign_key_check equivalent — the FK list stays empty; Postgres enforces FKs continuously.
            return new MaintenanceCheckResult(MaintenanceCheckStatus.Ok, messages, Array.Empty<string>());
        }
        catch (Exception ex)
        {
            Log.Error(ex, "PostgresMaintenanceProvider: CheckIntegrityAsync failed");
            messages.Add(ex.Message);
            return new MaintenanceCheckResult(MaintenanceCheckStatus.IntegrityFailed, messages, Array.Empty<string>());
        }
    }

    public async Task<MaintenanceRepairResult> OptimizeAndRepairAsync(
        IProgress<MaintenanceStep>? progress, CancellationToken ct)
    {
        long sizeBefore = 0, sizeAfter = 0;
        try
        {
            await using var dbContext = await _factory.CreateDbContextAsync(ct);
            await dbContext.Database.OpenConnectionAsync(ct);
            var connection = dbContext.Database.GetDbConnection();

            sizeBefore = await ScalarLongAsync(connection, "SELECT pg_database_size(current_database())", ct);

            // VACUUM cannot run inside a transaction block; the raw command on the open connection runs in
            // autocommit. ANALYZE refreshes planner statistics in the same pass.
            progress?.Report(MaintenanceStep.Vacuum);
            await ExecuteAsync(connection, "VACUUM (ANALYZE);", ct);

            sizeAfter = await ScalarLongAsync(connection, "SELECT pg_database_size(current_database())", ct);
            Log.Information(
                "PostgresMaintenanceProvider: VACUUM (ANALYZE) complete — {Before} -> {After} bytes",
                sizeBefore, sizeAfter);

            return new MaintenanceRepairResult(true, null, sizeBefore, sizeAfter, null);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "PostgresMaintenanceProvider: OptimizeAndRepairAsync failed");
            return new MaintenanceRepairResult(false, null, sizeBefore, sizeAfter, ex.Message);
        }
    }

    private static async Task<long> ScalarLongAsync(DbConnection connection, string sql, CancellationToken ct)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToInt64(await cmd.ExecuteScalarAsync(ct));
    }

    private static async Task<string> ScalarStringAsync(DbConnection connection, string sql, CancellationToken ct)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        return (await cmd.ExecuteScalarAsync(ct))?.ToString() ?? string.Empty;
    }

    private static async Task ExecuteAsync(DbConnection connection, string sql, CancellationToken ct)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
