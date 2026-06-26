using System;
using System.Threading;
using System.Threading.Tasks;

namespace BookDB.Data.Interfaces;

/// <summary>
/// Applies the active provider's own embedded migration script set. Each implementation scans exactly
/// one assembly — its own — so a provider's scripts can never run against another engine.
/// </summary>
public interface IDbUpRunner
{
    Task RunAsync(IProgress<(int applied, int total)> progress, CancellationToken ct);
}
