using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BookDB.Data.Interfaces;
using BookDB.Desktop.ViewModels;
using Xunit;

namespace BookDB.Desktop.Tests.ViewModels;

public sealed class StartupFailureViewModelTests
{
    /// <summary>Returns queued probe results in order, then repeats the last one.</summary>
    private sealed class QueuedConnector
    {
        private readonly Queue<ConnectionProbeResult> _results;
        private ConnectionProbeResult _last;
        public int Calls { get; private set; }

        public QueuedConnector(params ConnectionProbeResult[] results)
        {
            _results = new Queue<ConnectionProbeResult>(results);
            _last = results[^1];
        }

        public Task<ConnectionProbeResult> ConnectAsync(CancellationToken ct)
        {
            Calls++;
            if (_results.Count > 0)
                _last = _results.Dequeue();
            return Task.FromResult(_last);
        }
    }

    private static ConnectionProbeResult Refused() =>
        ConnectionProbeResult.Failed(ConnectionProbeStatus.ConnectionRefused, "refused");

    private static ConnectionProbeResult Success() => ConnectionProbeResult.Succeeded("16.3", null);

    [Fact]
    public void InitialMessage_ReflectsTheFailureAndCountsTheStartupAttempt()
    {
        var vm = new StartupFailureViewModel(Refused(), new QueuedConnector(Refused()).ConnectAsync);

        Assert.False(string.IsNullOrWhiteSpace(vm.Message));
        Assert.Equal(1, vm.FailedAttempts);
        Assert.True(vm.CanRetry);
        Assert.False(vm.RetriesExhausted);
        Assert.Null(vm.Outcome);
    }

    [Fact]
    public async Task Retry_WhenServerComesBack_ProceedsAndCloses()
    {
        var connector = new QueuedConnector(Success());
        var vm = new StartupFailureViewModel(Refused(), connector.ConnectAsync);
        var closed = false;
        vm.CloseDialog = () => closed = true;

        await vm.RetryCommand.ExecuteAsync(null);

        Assert.Equal(StartupFailureOutcome.Proceed, vm.Outcome);
        Assert.True(closed);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task Retry_StillFailing_IncrementsAttempts_AndDampensAfterThree()
    {
        var connector = new QueuedConnector(Refused(), Refused(), Refused());
        var vm = new StartupFailureViewModel(Refused(), connector.ConnectAsync);

        await vm.RetryCommand.ExecuteAsync(null); // attempts -> 2
        Assert.True(vm.CanRetry);
        await vm.RetryCommand.ExecuteAsync(null); // attempts -> 3

        Assert.Equal(StartupFailureViewModel.MaxRetries, vm.FailedAttempts);
        Assert.True(vm.RetriesExhausted);
        Assert.False(vm.CanRetry);
        Assert.False(vm.RetryCommand.CanExecute(null));
        Assert.Null(vm.Outcome); // never proceeds on failure
    }

    [Fact]
    public void OpenSettings_SetsOutcomeAndCloses()
    {
        var vm = new StartupFailureViewModel(Refused(), new QueuedConnector(Refused()).ConnectAsync);
        var closed = false;
        vm.CloseDialog = () => closed = true;

        vm.OpenSettingsCommand.Execute(null);

        Assert.Equal(StartupFailureOutcome.OpenSettings, vm.Outcome);
        Assert.True(closed);
    }

    [Fact]
    public void Quit_SetsOutcomeAndCloses()
    {
        var vm = new StartupFailureViewModel(Refused(), new QueuedConnector(Refused()).ConnectAsync);
        var closed = false;
        vm.CloseDialog = () => closed = true;

        vm.QuitCommand.Execute(null);

        Assert.Equal(StartupFailureOutcome.Quit, vm.Outcome);
        Assert.True(closed);
    }

    [Fact]
    public void DifferentStatuses_ProduceDistinctMessages()
    {
        var auth = new StartupFailureViewModel(
            ConnectionProbeResult.Failed(ConnectionProbeStatus.AuthenticationFailed, null),
            new QueuedConnector(Refused()).ConnectAsync);
        var timeout = new StartupFailureViewModel(
            ConnectionProbeResult.Failed(ConnectionProbeStatus.Timeout, null),
            new QueuedConnector(Refused()).ConnectAsync);

        Assert.NotEqual(auth.Message, timeout.Message);
    }
}
