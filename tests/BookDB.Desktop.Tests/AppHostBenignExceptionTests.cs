using System;
using System.Threading.Tasks;
using Xunit;

namespace BookDB.Desktop.Tests;

public sealed class AppHostBenignExceptionTests
{
    // Exception.StackTrace is only populated by a real throw; override it to stand in for an exception
    // that escaped from Tmds.DBus connection teardown.
    private sealed class DBusTeardownTaskCanceledException : TaskCanceledException
    {
        public override string StackTrace =>
            "   at Avalonia.Threading.DispatcherOperation.Wait(TimeSpan timeout)\n" +
            "   at Tmds.DBus.Protocol.DBusConnection.Observer.Emit(Exception exception)\n" +
            "   at Tmds.DBus.Protocol.DBusConnection.Dispose()";
    }

    [Fact]
    public void TaskCanceled_WithTmdsDBusFrames_IsBenign()
    {
        Assert.True(AppHost.IsBenignDBusTeardownError(new DBusTeardownTaskCanceledException()));
    }

    [Fact]
    public void TaskCanceled_WithTmdsDBusFrames_InsideAggregate_IsBenign()
    {
        var agg = new AggregateException(new DBusTeardownTaskCanceledException());
        Assert.True(AppHost.IsBenignDBusTeardownError(agg));
    }

    [Fact]
    public void TaskCanceled_WithoutTmdsDBusFrames_IsNotBenign()
    {
        // A real (thrown) cancellation from app code must still reach the fatal handler.
        TaskCanceledException thrown;
        try
        {
            throw new TaskCanceledException();
        }
        catch (TaskCanceledException ex)
        {
            thrown = ex;
        }

        Assert.False(AppHost.IsBenignDBusTeardownError(thrown));
    }

    [Fact]
    public void NonCancellation_MentioningTmdsDBus_IsNotBenign()
    {
        // Only the cancellation signature is safe to suppress — a genuine DBus failure is not.
        Assert.False(AppHost.IsBenignDBusTeardownError(new InvalidOperationException("Tmds.DBus.Protocol failure")));
    }

    [Fact]
    public void Null_IsNotBenign()
    {
        Assert.False(AppHost.IsBenignDBusTeardownError(null));
    }

    [Fact]
    public void UnobservedTmdsDBusFailure_EvenInsideAggregate_IsBenignNoise()
    {
        // The finalizer surfaces these wrapped: AggregateException -> DBusException. The DBus message does
        // not always name the failing service, so the filter must match on the exception type alone.
        var agg = new AggregateException(new Tmds.DBus.Protocol.TestDBusException("The name is not activatable"));

        Assert.True(AppHost.IsBenignDesktopDBusError(agg));
    }

    [Fact]
    public void UnobservedNonDBusFailure_IsNotFiltered()
    {
        // A real defect in app code that mentions DBus in its message must still reach the log.
        var agg = new AggregateException(new InvalidOperationException("DBus-sounding but ours"));

        Assert.False(AppHost.IsBenignDesktopDBusError(agg));
    }

    [Fact]
    public void DBusFailureBuriedAsInnerException_IsStillBenign()
    {
        var wrapped = new InvalidOperationException(
            "outer", new Tmds.DBus.Protocol.TestDBusException("org.freedesktop.DBus.Error.ServiceUnknown"));

        Assert.True(AppHost.IsBenignDesktopDBusError(wrapped));
    }
}
