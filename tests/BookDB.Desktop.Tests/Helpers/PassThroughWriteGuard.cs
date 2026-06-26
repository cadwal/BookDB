using System;
using System.Threading;
using System.Threading.Tasks;
using BookDB.Desktop.Services;

namespace BookDB.Desktop.Tests.Helpers;

/// <summary>
/// A write guard that simply runs the write — no connection-loss interception. Used by view-model tests that
/// exercise save behaviour against an in-memory store where the connection can never drop.
/// </summary>
public sealed class PassThroughWriteGuard : IRemoteWriteGuard
{
    public async Task<WriteResult> ExecuteAsync(Func<CancellationToken, Task> write, CancellationToken ct = default)
    {
        await write(ct);
        return WriteResult.Saved;
    }
}
