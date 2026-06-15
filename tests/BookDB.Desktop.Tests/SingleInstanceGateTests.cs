using System;
using System.IO;
using System.Threading;
using BookDB.Desktop;
using Xunit;

namespace BookDB.Desktop.Tests;

public sealed class SingleInstanceGateTests
{
    [Fact]
    public void TryAcquire_FirstCaller_IsPrimary()
    {
        var dir = NewTempDir();
        try
        {
            using var gate = SingleInstanceGate.TryAcquire(dir);
            Assert.True(gate.IsPrimary);
        }
        finally
        {
            CleanUp(dir);
        }
    }

    [Fact]
    public void TryAcquire_WhileLockHeld_IsNotPrimary()
    {
        var dir = NewTempDir();
        try
        {
            using var primary = SingleInstanceGate.TryAcquire(dir);
            using var secondary = SingleInstanceGate.TryAcquire(dir);

            Assert.True(primary.IsPrimary);
            Assert.False(secondary.IsPrimary);
        }
        finally
        {
            CleanUp(dir);
        }
    }

    [Fact]
    public void TryAcquire_AfterPrimaryDisposed_IsPrimaryAgain()
    {
        var dir = NewTempDir();
        try
        {
            var first = SingleInstanceGate.TryAcquire(dir);
            Assert.True(first.IsPrimary);
            first.Dispose();

            using var second = SingleInstanceGate.TryAcquire(dir);
            Assert.True(second.IsPrimary);
        }
        finally
        {
            CleanUp(dir);
        }
    }

    [Fact]
    public void SecondInstance_SignalsPrimaryToActivate()
    {
        var dir = NewTempDir();
        try
        {
            using var activated = new ManualResetEventSlim(false);
            using var primary = SingleInstanceGate.TryAcquire(dir);
            primary.SetActivationHandler(() => activated.Set());

            using var secondary = SingleInstanceGate.TryAcquire(dir);
            Assert.False(secondary.IsPrimary);

            Assert.True(activated.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken),
                "Primary instance was not signalled to activate within the timeout.");
        }
        finally
        {
            CleanUp(dir);
        }
    }

    private static string NewTempDir()
        => Path.Combine(Path.GetTempPath(), $"bookdb_si_{Guid.NewGuid():N}");

    private static void CleanUp(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
        catch
        {
            // Best-effort temp cleanup — a still-open lock handle on a failed test must not mask the failure.
        }
    }
}
