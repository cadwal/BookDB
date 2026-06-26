using System.Data.Common;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BookDB.Models.Interfaces;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace BookDB.Data.Interceptors;

/// <summary>
/// Flags the <see cref="IDataChangeTracker"/> whenever a command writes to the database
/// (<c>INSERT</c>/<c>UPDATE</c>/<c>DELETE</c>), so the auto-backup on exit knows something worth backing up
/// changed this session. Unlike a <c>SaveChanges</c> interceptor this sits at the ADO command layer, so it
/// also catches EF Core bulk <c>ExecuteUpdate</c>/<c>ExecuteDelete</c> calls, which bypass <c>SaveChanges</c>
/// entirely.
///
/// Only writes that actually change a row count: bulk statements report rows-affected, so a DELETE/UPDATE
/// that matched nothing (e.g. the batch-queue cleanup that runs on every startup) is ignored — otherwise just
/// opening and closing the app would look like a data change.
///
/// Every real write counts, including settings — re-saving an unchanged setting issues no SQL (see
/// <c>LookupService.SetSettingAsync</c>), so view-only chrome churn never reaches here. The backup's own
/// <c>AutoBackup.LastRun</c> write needs no special-casing: it happens only after a backup has run and is
/// immediately followed by <c>IDataChangeTracker.Reset()</c>.
/// </summary>
public sealed class DataChangeCommandInterceptor : DbCommandInterceptor
{
    // Matches a write verb at the start of the command or of any statement in a multi-statement batch.
    private static readonly Regex WriteStatement = new(
        @"(?:^|;)\s*(?:INSERT|UPDATE|DELETE)\b",
        RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);

    // The heartbeat writes its own ClientSession row on connect/refresh/exit; that is process presence, not
    // library data, so it must not look like a change worth backing up. Heartbeat commands target only this
    // table (a dedicated context, never batched with user writes), so a match means the whole command is one.
    private static readonly Regex SessionTableWrite = new(
        @"(?:INSERT\s+INTO|UPDATE|DELETE\s+FROM)\s+""?ClientSession""?\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly IDataChangeTracker _tracker;

    public DataChangeCommandInterceptor(IDataChangeTracker tracker) => _tracker = tracker;

    // Bulk ExecuteUpdate/ExecuteDelete, and any SaveChanges write not using a RETURNING clause. These report
    // the number of rows affected, so we flag at the Executed stage and only when a row actually changed — a
    // DELETE/UPDATE that matched nothing (e.g. the batch-queue cleanup that runs on every startup) is not a
    // data change.
    public override int NonQueryExecuted(
        DbCommand command, CommandExecutedEventData eventData, int result)
    {
        FlagIfWriteAffectedRows(command, result);
        return base.NonQueryExecuted(command, eventData, result);
    }

    public override ValueTask<int> NonQueryExecutedAsync(
        DbCommand command, CommandExecutedEventData eventData, int result,
        CancellationToken cancellationToken = default)
    {
        FlagIfWriteAffectedRows(command, result);
        return base.NonQueryExecutedAsync(command, eventData, result, cancellationToken);
    }

    // SaveChanges INSERT/UPDATE/DELETE with a RETURNING clause (the SQLite provider's default).
    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        Flag(command);
        return base.ReaderExecuting(command, eventData, result);
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        Flag(command);
        return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }

    private void Flag(DbCommand command)
    {
        if (WriteStatement.IsMatch(command.CommandText) && !SessionTableWrite.IsMatch(command.CommandText))
            _tracker.MarkChanged();
    }

    private void FlagIfWriteAffectedRows(DbCommand command, int rowsAffected)
    {
        if (rowsAffected > 0 && WriteStatement.IsMatch(command.CommandText) && !SessionTableWrite.IsMatch(command.CommandText))
            _tracker.MarkChanged();
    }
}
