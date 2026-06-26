using System;
using System.Threading;
using System.Threading.Tasks;
using BookDB.Data.Interfaces;
using BookDB.Desktop.Services;
using BookDB.Logic.Services;
using NSubstitute;
using Xunit;

namespace BookDB.Desktop.Tests.ViewModels;

public sealed class RemoteWriteGuardTests
{
    private readonly IConnectionFailureClassifier _classifier = Substitute.For<IConnectionFailureClassifier>();
    private readonly IConnectionHealthMonitor _monitor = Substitute.For<IConnectionHealthMonitor>();
    private readonly IWindowService _windowService = Substitute.For<IWindowService>();

    private RemoteWriteGuard Create() => new(_classifier, _monitor, _windowService);

    [Fact]
    public async Task SuccessfulWrite_ReturnsSaved_NoDialog()
    {
        var guard = Create();

        var result = await guard.ExecuteAsync(_ => Task.CompletedTask, TestContext.Current.CancellationToken);

        Assert.Equal(WriteResult.Saved, result);
        await _windowService.DidNotReceive().ShowWriteFailureDialogAsync(Arg.Any<string>());
    }

    [Fact]
    public async Task NonConnectionException_PropagatesToCaller()
    {
        _classifier.IsConnectionLoss(Arg.Any<Exception>()).Returns(false);
        var guard = Create();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            guard.ExecuteAsync(_ => throw new InvalidOperationException("constraint"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ConnectionLoss_UserDiscards_ReturnsDiscarded_AndReportsToMonitor()
    {
        _classifier.IsConnectionLoss(Arg.Any<Exception>()).Returns(true);
        _windowService.ShowWriteFailureDialogAsync(Arg.Any<string>()).Returns(WriteFailureChoice.Discard);
        var guard = Create();

        var result = await guard.ExecuteAsync(
            _ => throw new TimeoutException(), TestContext.Current.CancellationToken);

        Assert.Equal(WriteResult.Discarded, result);
        _monitor.Received().ReportConnectionFailure();
    }

    [Fact]
    public async Task ConnectionLoss_UserRetries_ThenSecondAttemptSucceeds_ReturnsSaved()
    {
        _classifier.IsConnectionLoss(Arg.Any<Exception>()).Returns(true);
        _windowService.ShowWriteFailureDialogAsync(Arg.Any<string>()).Returns(WriteFailureChoice.Retry);
        var guard = Create();

        int attempts = 0;
        var result = await guard.ExecuteAsync(_ =>
        {
            attempts++;
            if (attempts == 1) throw new TimeoutException();
            return Task.CompletedTask;
        }, TestContext.Current.CancellationToken);

        Assert.Equal(WriteResult.Saved, result);
        Assert.Equal(2, attempts);
    }
}
