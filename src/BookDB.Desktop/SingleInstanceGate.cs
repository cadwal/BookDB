using System;
using System.IO;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BookDB.Desktop;

/// <summary>
/// Enforces a single running instance per user. The first process to start holds an exclusive lock
/// on a file in the app-data directory and listens on a named pipe; later processes fail to take the
/// lock, signal the holder over the pipe so it can raise its window, and exit.
/// </summary>
/// <remarks>
/// The lock file — not a named mutex — is the authoritative primitive: the OS releases an exclusive
/// <see cref="FileShare.None"/> handle when the process dies (even on a crash or kill), so there is no
/// stale lock to clean up, and .NET named mutexes are not reliably system-wide across processes on Linux.
/// The named pipe is only the activation channel; .NET backs it with a unix domain socket on Linux, so a
/// single abstraction works on Windows and the Raspberry Pi alike.
/// </remarks>
internal sealed class SingleInstanceGate : IDisposable
{
    /// <summary>Command-line flag marking a process spawned by the forced restart; it waits for the outgoing instance to release the lock.</summary>
    internal const string RelaunchArgument = "--relaunch";

    private readonly string _pipeName;
    private readonly FileStream? _lockStream;
    private readonly CancellationTokenSource? _cts;
    private readonly object _sync = new();
    private Action? _onActivate;
    private bool _pendingActivation;
    private bool _disposed;

    public bool IsPrimary { get; }

    private SingleInstanceGate(string pipeName, FileStream? lockStream, bool isPrimary)
    {
        _pipeName = pipeName;
        _lockStream = lockStream;
        IsPrimary = isPrimary;

        if (isPrimary)
        {
            _cts = new CancellationTokenSource();
            _ = Task.Run(() => ListenAsync(_cts.Token));
        }
    }

    /// <summary>
    /// Attempts to become the single instance for <paramref name="appDataPath"/>. When this returns a
    /// non-primary gate, the running instance has already been signalled to activate and the caller should
    /// exit. Never throws — an unexpected error fails open (the caller starts as an unguarded primary)
    /// rather than blocking the user from launching their app. <paramref name="waitForLock"/> is non-zero
    /// only for a relaunch: it retries the lock while the outgoing instance shuts down, instead of treating
    /// the still-held lock as a rival instance and exiting.
    /// </summary>
    public static SingleInstanceGate TryAcquire(string appDataPath, TimeSpan waitForLock = default)
    {
        var pipeName = "BookDB-SingleInstance-" + ShortHash(appDataPath);

        try
        {
            Directory.CreateDirectory(appDataPath);
            var lockPath = Path.Combine(appDataPath, ".instance.lock");
            var deadline = DateTime.UtcNow + waitForLock;
            while (true)
            {
                try
                {
                    var lockStream = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                    return new SingleInstanceGate(pipeName, lockStream, isPrimary: true);
                }
                catch (IOException) when (DateTime.UtcNow < deadline)
                {
                    Thread.Sleep(100);
                }
            }
        }
        catch (IOException)
        {
            // Sharing violation — another instance holds the lock. Tell it to come to the front, then exit.
            TrySignalPrimary(pipeName);
            return new SingleInstanceGate(pipeName, lockStream: null, isPrimary: false);
        }
        catch
        {
            // Unexpected (permissions, disk) — do not trap the user out of their app; run unguarded.
            return new SingleInstanceGate(pipeName, lockStream: null, isPrimary: true);
        }
    }

    /// <summary>
    /// Registers the action invoked when another instance asks this one to activate. If a request already
    /// arrived before the handler was set (a second launch during startup), it fires immediately.
    /// </summary>
    public void SetActivationHandler(Action handler)
    {
        bool fireNow;
        lock (_sync)
        {
            _onActivate = handler;
            fireNow = _pendingActivation;
            _pendingActivation = false;
        }

        if (fireNow)
            handler();
    }

    private async Task ListenAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(
                    _pipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

                await server.WaitForConnectionAsync(ct).ConfigureAwait(false);
                FireActivation();
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // Transient pipe error — pause briefly so a persistent failure can't spin a hot loop.
                try { await Task.Delay(200, ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
            }
        }
    }

    private void FireActivation()
    {
        Action? handler;
        lock (_sync)
        {
            handler = _onActivate;
            if (handler is null)
                _pendingActivation = true;
        }

        handler?.Invoke();
    }

    private static void TrySignalPrimary(string pipeName)
    {
        try
        {
            using var client = new NamedPipeClientStream(
                ".", pipeName, PipeDirection.Out, PipeOptions.CurrentUserOnly);
            client.Connect(2000);
            client.WriteByte(1);
            client.Flush();
        }
        catch
        {
            // Holder is mid-startup or gone; the second instance exits regardless of whether it could signal.
        }
    }

    private static string ShortHash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value.ToLowerInvariant()));
        return Convert.ToHexString(bytes, 0, 8);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        _cts?.Cancel();
        _cts?.Dispose();
        _lockStream?.Dispose();
    }
}
