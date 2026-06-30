using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BookDB.Data.DbContexts;
using BookDB.Data.Interfaces;
using BookDB.Models;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace BookDB.Data.MySql;

/// <summary>
/// MySQL/MariaDB maintenance. The read-only "check" runs <c>CHECK TABLE</c> over every base table — the real
/// analog to SQLite's <c>PRAGMA integrity_check</c> (Postgres has none) — plus a server-version report; a table
/// reporting an <c>error</c> row maps to <see cref="MaintenanceCheckStatus.IntegrityFailed"/>. The
/// "optimize/repair" is <c>OPTIMIZE TABLE</c> (InnoDB rebuilds the table to reclaim space) followed by
/// <c>ANALYZE TABLE</c> to refresh the optimizer statistics — the SQLite REINDEX/VACUUM and Postgres
/// VACUUM (ANALYZE) analog. No PRAGMA or VACUUM is ever emitted. Size before/after come from
/// <c>information_schema.tables</c>. The provider-neutral safety backup is taken by
/// <c>DatabaseMaintenanceService</c>, not here.
/// </summary>
public sealed class MySqlMaintenanceProvider : IMaintenanceProvider
{
    private readonly IDbContextFactory<BookDbContext> _factory;
    private readonly IConnectionFailureClassifier _connectionFailureClassifier;

    public MySqlMaintenanceProvider(
        IDbContextFactory<BookDbContext> factory, IConnectionFailureClassifier connectionFailureClassifier)
    {
        _factory = factory;
        _connectionFailureClassifier = connectionFailureClassifier;
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

            // VERSION() returns just the version string (e.g. "8.0.40" or "11.4.2-MariaDB"); label it so the log
            // line is self-describing.
            messages.Add($"Server version: {await ScalarStringAsync(connection, "SELECT VERSION()", ct)}");

            var tables = await ReadBaseTableNamesAsync(connection, ct);
            var integrityFailed = false;
            if (tables.Count > 0)
            {
                var tableList = string.Join(", ", tables.ConvertAll(t => $"`{t}`"));
                // CHECK TABLE returns one or more rows per table: (Table, Op, Msg_type, Msg_text). A sound table
                // reports Msg_type='status' / Msg_text='OK'; real corruption reports an 'error' row.
                await using var cmd = connection.CreateCommand();
                cmd.CommandText = $"CHECK TABLE {tableList}";
                await using var reader = await cmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    var table = Unqualify(reader.GetString(0));
                    var msgType = reader.GetString(2);
                    var msgText = reader.GetString(3);
                    messages.Add($"{table}: {msgText}");
                    if (string.Equals(msgType, "error", StringComparison.OrdinalIgnoreCase))
                        integrityFailed = true;
                }
            }

            var status = integrityFailed ? MaintenanceCheckStatus.IntegrityFailed : MaintenanceCheckStatus.Ok;
            Log.Information(
                "MySqlMaintenanceProvider: CHECK TABLE status {Status} ({TableCount} tables checked)",
                status, tables.Count);

            // No cross-table FK scan — InnoDB enforces FKs continuously, so dangling references can't exist; the
            // FK-violation list (the SQLite PRAGMA foreign_key_check analog) stays empty.
            return new MaintenanceCheckResult(status, messages, Array.Empty<string>()) { TablesChecked = tables };
        }
        catch (Exception ex)
        {
            Log.Error(ex, "MySqlMaintenanceProvider: CheckIntegrityAsync failed");
            // CHECK TABLE can abort the server on already-corrupt InnoDB data, dropping the connection mid-check.
            // A connection loss is not an integrity verdict — say so, so the detail doesn't imply on-disk
            // corruption. (Status stays IntegrityFailed: MaintenanceCheckStatus has no connection-loss member,
            // and a failed check is still a failure the caller must surface.)
            messages.Add(_connectionFailureClassifier.IsConnectionLoss(ex)
                ? $"The database connection was lost during the check; the result is inconclusive ({ex.Message})."
                : ex.Message);
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

            sizeBefore = await DatabaseSizeBytesAsync(connection, ct);

            // Optimize the live base tables (same source as the integrity check), not a hardcoded list that could
            // drift from the schema or name a table a partial migration hasn't created yet.
            var tables = await ReadBaseTableNamesAsync(connection, ct);
            if (tables.Count == 0)
                return new MaintenanceRepairResult(true, null, sizeBefore, sizeBefore, null);

            var tableList = string.Join(", ", tables.ConvertAll(t => $"`{t}`"));

            // OPTIMIZE TABLE on InnoDB recreates the table to reclaim free space (it permits concurrent DML via
            // online DDL); ANALYZE TABLE then refreshes the optimizer statistics.
            progress?.Report(MaintenanceStep.Vacuum);
            var errors = await RunTableMaintenanceAsync(connection, $"OPTIMIZE TABLE {tableList}", ct);
            errors.AddRange(await RunTableMaintenanceAsync(connection, $"ANALYZE TABLE {tableList}", ct));

            sizeAfter = await DatabaseSizeBytesAsync(connection, ct);

            if (errors.Count > 0)
            {
                var detail = string.Join("; ", errors);
                Log.Warning("MySqlMaintenanceProvider: OPTIMIZE/ANALYZE reported errors — {Errors}", detail);
                return new MaintenanceRepairResult(false, null, sizeBefore, sizeAfter, detail);
            }

            Log.Information(
                "MySqlMaintenanceProvider: OPTIMIZE + ANALYZE complete — {Before} -> {After} bytes ({Tables} tables)",
                sizeBefore, sizeAfter, tables.Count);
            return new MaintenanceRepairResult(true, null, sizeBefore, sizeAfter, null) { TablesOptimized = tables };
        }
        catch (Exception ex)
        {
            Log.Error(ex, "MySqlMaintenanceProvider: OptimizeAndRepairAsync failed");
            return new MaintenanceRepairResult(false, null, sizeBefore, sizeAfter, ex.Message);
        }
    }

    // OPTIMIZE/ANALYZE TABLE each return one or more rows per table (Table, Op, Msg_type, Msg_text). InnoDB emits a
    // benign Msg_type='note' ("Table does not support optimize, doing recreate + analyze instead"); only an 'error'
    // row is a real failure. Returns the error rows' messages (empty when every table is clean).
    private static async Task<List<string>> RunTableMaintenanceAsync(DbConnection connection, string sql, CancellationToken ct)
    {
        var errors = new List<string>();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            if (string.Equals(reader.GetString(2), "error", StringComparison.OrdinalIgnoreCase))
                errors.Add($"{Unqualify(reader.GetString(0))}: {reader.GetString(3)}");
        }
        return errors;
    }

    // Bytes used by the current schema, summed across all its tables (information_schema lags slightly behind
    // live writes but is the only portable size source; it is exact enough for a before/after delta).
    private static Task<long> DatabaseSizeBytesAsync(DbConnection connection, CancellationToken ct) =>
        ScalarLongAsync(
            connection,
            "SELECT COALESCE(SUM(`data_length` + `index_length`), 0) FROM `information_schema`.`tables` " +
            "WHERE `table_schema` = DATABASE()",
            ct);

    // Base tables in the current schema (excludes views), so CHECK TABLE never hits a non-checkable object.
    private static async Task<List<string>> ReadBaseTableNamesAsync(DbConnection connection, CancellationToken ct)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText =
            "SELECT `table_name` FROM `information_schema`.`tables` " +
            "WHERE `table_schema` = DATABASE() AND `table_type` = 'BASE TABLE'";
        var names = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            names.Add(reader.GetString(0));
        return names;
    }

    // CHECK TABLE reports the table schema-qualified (e.g. "bookdb.Book"); keep just the table name for display.
    private static string Unqualify(string qualifiedTable)
    {
        var dot = qualifiedTable.LastIndexOf('.');
        return dot >= 0 ? qualifiedTable[(dot + 1)..] : qualifiedTable;
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

}
