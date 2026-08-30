using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BookDB.Contracts;
using BookDB.Isbn;
using BookDB.Mobile.Localization;
using BookDB.Mobile.Services;
using BookDB.Mobile.Staging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Grpc.Core;
using ProtoBuf.Grpc;

namespace BookDB.Mobile.ViewModels;

/// <summary>
/// The first step of the scan wizard: read an ISBN off a barcode, or type one in when the barcode is damaged.
/// A read shows the number, whether the batch already has it, and what the computer makes of it — or that
/// there was no computer to ask — then moves on by itself; Undo is there for the moment in between. Capture
/// never depends on the connection: offline the wizard is the same, only quieter and quicker.
/// </summary>
public sealed partial class IsbnScanViewModel : PageViewModel, IDisposable
{
    /// <summary>How long the read is shown <em>after</em> the confidence line lands, before the wizard moves
    /// on. Long enough to notice a wrong number and reach Undo, short enough that scanning a stack does not
    /// feel gated. 1.5 s and then 2 s were both tried on a handset and reported too short.</summary>
    public static readonly TimeSpan UndoWindow = TimeSpan.FromMilliseconds(3000);

    /// <summary>
    /// The longest the wizard will wait for a connected computer to answer. A computer that has not answered
    /// by now is not going to, and the stack of books is not worth stalling; the read is shown as unchecked
    /// instead. Also the deadline on the call itself, so the two cannot disagree.
    /// </summary>
    private static readonly TimeSpan PreviewTimeout = TimeSpan.FromSeconds(5);

    private readonly IBarcodeScanner _scanner;
    private readonly IConnectionService _connection;
    private readonly IStagingStore _staging;
    private readonly Action<string> _onIsbnAccepted;
    private readonly Func<CancellationToken, Task> _hold;
    private readonly TimeSpan _previewTimeout;

    private CancellationTokenSource? _pending;

    public IsbnScanViewModel(
        IBarcodeScanner scanner,
        IConnectionService connection,
        IStagingStore staging,
        IDeviceSettings deviceSettings,
        Action<string> onIsbnAccepted,
        Func<CancellationToken, Task>? hold = null,
        TimeSpan? previewTimeout = null)
    {
        _scanner = scanner;
        _connection = connection;
        _staging = staging;
        _onIsbnAccepted = onIsbnAccepted;
        _hold = hold ?? (ct => Task.Delay(UndoWindow, ct));
        _previewTimeout = previewTimeout ?? PreviewTimeout;
        CameraAccess = new CameraAccessViewModel(deviceSettings);
    }

    public override string Title => Resources.Scan_Title;

    /// <summary>Typing the number stays open whatever the camera does, so a refusal narrows the screen rather
    /// than ending it.</summary>
    public CameraAccessViewModel CameraAccess { get; }

    /// <summary>The shell disposes a page it navigates away from. Cancelling here also settles the hold: a
    /// wizard left mid-read must not advance to a book builder nobody asked for.</summary>
    public void Dispose()
    {
        _pending?.Cancel();
        _pending?.Dispose();
        _pending = null;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ManualValidity))]
    [NotifyPropertyChangedFor(nameof(ShowsManualValid))]
    [NotifyPropertyChangedFor(nameof(ShowsManualWarning))]
    [NotifyCanExecuteChangedFor(nameof(UseManualIsbnCommand))]
    private string _manualIsbn = string.Empty;

    [ObservableProperty]
    private bool _isScanning;

    /// <summary>The read, normalized — what would be staged and what the computer was asked about.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasResult))]
    [NotifyPropertyChangedFor(nameof(AcceptedValidity))]
    [NotifyPropertyChangedFor(nameof(ShowsAcceptedValid))]
    [NotifyPropertyChangedFor(nameof(ShowsAcceptedWarning))]
    [NotifyCanExecuteChangedFor(nameof(UndoCommand))]
    private string? _acceptedIsbn;

    /// <summary>Title (and authors) for the read, the notice that the library already holds it, or — when
    /// there was no computer to ask — that nothing was checked. Null while the answer is still coming.</summary>
    [ObservableProperty]
    private string? _previewLine;

    /// <summary>Set when the read is a book the batch is already carrying. Local and instant, so it is on
    /// screen for the whole undo window rather than arriving after it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDuplicate))]
    private string? _duplicateLine;

    [ObservableProperty]
    private bool _isAdvancing;

    [ObservableProperty]
    private string? _message;

    public bool HasResult => AcceptedIsbn is not null;

    public bool IsDuplicate => DuplicateLine is not null;

    public IsbnValidity ManualValidity => IsbnNormalizer.GetValidity(ManualIsbn);

    public bool ShowsManualValid => ManualValidity == IsbnValidity.Valid;

    /// <summary>A failed check digit is worth flagging but never blocks: misprinted ISBNs exist on real books.</summary>
    public bool ShowsManualWarning => ManualValidity == IsbnValidity.Invalid;

    /// <summary>The check digit of the read itself. A scan is not automatically a book number — a cover can
    /// carry other barcodes — so the read is marked with what it actually is rather than with a tick.</summary>
    public IsbnValidity AcceptedValidity => IsbnNormalizer.GetValidity(AcceptedIsbn);

    public bool ShowsAcceptedValid => AcceptedValidity == IsbnValidity.Valid;

    public bool ShowsAcceptedWarning => AcceptedValidity == IsbnValidity.Invalid;

    [RelayCommand(CanExecute = nameof(CanScan))]
    private async Task ScanAsync()
    {
        IsScanning = true;
        Message = null;
        try
        {
            var scan = await _scanner.ScanOnceAsync(BarcodeKind.BookNumber);
            if (CameraAccess.Explains(scan))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(scan.Text))
            {
                Message = Resources.Scan_NothingRead;
                return;
            }

            await AcceptAsync(scan.Text!);
        }
        finally
        {
            IsScanning = false;
        }
    }

    private bool CanScan() => !IsScanning;

    [RelayCommand(CanExecute = nameof(CanUseManualIsbn))]
    private async Task UseManualIsbnAsync() => await AcceptAsync(ManualIsbn);

    private bool CanUseManualIsbn() => IsbnNormalizer.Normalize(ManualIsbn).Length > 0;

    /// <summary>Takes back a read before the wizard has moved on — the affordance that makes auto-advance safe.</summary>
    [RelayCommand(CanExecute = nameof(HasResult))]
    private void Undo()
    {
        _pending?.Cancel();
        AcceptedIsbn = null;
        PreviewLine = null;
        DuplicateLine = null;
        IsAdvancing = false;
    }

    private async Task AcceptAsync(string raw)
    {
        var isbn = IsbnNormalizer.Normalize(raw);
        if (isbn.Length == 0)
            return;

        _pending?.Cancel();
        _pending?.Dispose();
        _pending = new CancellationTokenSource();
        var token = _pending.Token;

        AcceptedIsbn = isbn;
        DuplicateLine = _staging.Items.Any(item => item.Isbn == isbn) ? Resources.Scan_AlreadyInBatch : null;
        Message = null;

        // There is no computer to ask, and saying nothing would read as "nothing known about this book"
        // rather than "nobody was asked". The lookup below replaces this if the connection comes back.
        PreviewLine = _connection.Status == ConnectionStatus.Connected ? null : Resources.Scan_NotChecked;

        var lookup = PreviewAsync(isbn, token);

        IsAdvancing = true;
        try
        {
            // The confidence line is the whole reason Undo exists, so the undo window starts once the answer
            // is in — a wizard that moves on while the computer is still talking asked for nothing. It has to
            // be the answer landing that opens the window and not a timer guessing that it has: race the two
            // and a slow computer spends the window on a blank panel, then delivers the line just as the
            // wizard leaves. That is UAT 1g, which survived two goes at raising the timer because the timer
            // was never the mechanism. Only a computer that is answering is worth the pause: an offline dial
            // times out long after the user has moved to the next book.
            if (_connection.Status == ConnectionStatus.Connected)
            {
                var settled = await Task.WhenAny(lookup, Task.Delay(_previewTimeout, token));

                // Asked and never answered. Saying nothing would read as "nothing known about this book".
                if (settled != lookup && !token.IsCancellationRequested)
                    PreviewLine = Resources.Scan_NotChecked;
            }

            // A read the user probably did not mean gets the window twice over: a warning nobody has time to
            // read is the same as no warning.
            await _hold(token);
            if (IsDuplicate)
                await _hold(token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        finally
        {
            IsAdvancing = false;
        }

        if (!token.IsCancellationRequested)
            _onIsbnAccepted(isbn);
    }

    private async Task PreviewAsync(string isbn, CancellationToken ct)
    {
        try
        {
            var connection = await _connection.EnsureConnectedAsync(ct);
            if (connection is null)
            {
                if (!ct.IsCancellationRequested)
                    PreviewLine = Resources.Scan_NotChecked;
                return;
            }

            var preview = await connection.Service.CheckIsbnAsync(
                new IsbnQuery { Isbn = isbn },
                new CallContext(new CallOptions(
                    deadline: DateTime.UtcNow + _previewTimeout, cancellationToken: ct)));

            if (!ct.IsCancellationRequested)
                PreviewLine = Describe(preview);
        }
        catch (OperationCanceledException)
        {
            // The read was taken back, or the page left. Nothing to report to a screen that has gone.
        }
        catch (Exception ex) when (ex is RpcException or HttpRequestException or IOException)
        {
            // Said plainly rather than left blank: a silent line is indistinguishable from a computer that
            // answered and knew nothing, and only one of the two is worth a second look at the number.
            if (!ct.IsCancellationRequested)
                PreviewLine = Resources.Scan_NotChecked;
        }
    }

    private static string? Describe(IsbnPreview preview)
    {
        if (preview.InLibrary)
        {
            return string.Format(
                CultureInfo.CurrentCulture,
                Resources.Scan_AlreadyInLibrary,
                string.IsNullOrWhiteSpace(preview.Title) ? Resources.Scan_UntitledBook : preview.Title);
        }

        if (string.IsNullOrWhiteSpace(preview.Title))
            return null;

        return string.IsNullOrWhiteSpace(preview.Authors)
            ? preview.Title
            : string.Format(CultureInfo.CurrentCulture, Resources.Scan_TitleAndAuthors, preview.Title, preview.Authors);
    }
}
