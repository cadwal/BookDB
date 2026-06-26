using System;
using System.Threading;
using System.Threading.Tasks;
using BookDB.Data.Interfaces;
using BookDB.Models;
using Microsoft.Extensions.Hosting;

namespace BookDB.Data;

public sealed class DatabaseStartupService : IHostedService
{
    private readonly IDbUpRunner _runner;
    private readonly IStartupProgressReporter _progress;

    public DatabaseStartupService(IDbUpRunner runner, IStartupProgressReporter progress)
    {
        _runner = runner;
        _progress = progress;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var progress = new SynchronousProgress<(int applied, int total)>(
            value => _progress.Report(StartupStage.ApplyingMigrations, value.applied, value.total));

        await _runner.RunAsync(progress, cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    // Forwards inline rather than posting to a captured SynchronizationContext, so progress reaches the
    // splash reporter on the same thread and timing as before the runner was extracted.
    private sealed class SynchronousProgress<T> : IProgress<T>
    {
        private readonly Action<T> _handler;

        public SynchronousProgress(Action<T> handler) => _handler = handler;

        public void Report(T value) => _handler(value);
    }
}
