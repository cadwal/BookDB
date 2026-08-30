using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BookDB.Contracts;
using BookDB.Isbn;
using BookDB.Mobile.Localization;
using BookDB.Mobile.Services;
using BookDB.Mobile.Staging;
using BookDB.Mobile.ViewModels;
using Xunit;

namespace BookDB.Mobile.Tests;

/// <summary>The ISBN step of the wizard, driven with a fake scanner and a fake computer: what a read does,
/// what Undo takes back, what typing a number by hand is allowed to do, and how the confidence line behaves
/// when there is — and isn't — a computer to ask.</summary>
public class IsbnScanViewModelTests
{
    private const string Dune = "9780441013593";

    /// <summary>A connection whose desktop answers ISBN questions the way the test wants.</summary>
    private sealed class StubConnection : ICompanionConnection
    {
        public StubConnection(IBookScannerService service) => Service = service;

        public IBookScannerService Service { get; }

        public void Dispose() { }
    }

    private static IsbnScanViewModel New(
        out FakeBarcodeScanner scanner,
        out List<string> accepted,
        StubScannerService? desktop = null,
        Func<CancellationToken, Task>? hold = null,
        FakeDeviceSettings? deviceSettings = null,
        IStagingStore? staging = null)
    {
        scanner = new FakeBarcodeScanner();
        var taken = new List<string>();
        accepted = taken;

        var connection = new FakeConnectionService();
        if (desktop is not null)
        {
            connection.Connection = new StubConnection(desktop);

            // A computer that answers is a computer the wizard is willing to wait for, which is the state
            // every lookup test means to be in.
            connection.Report(ConnectionStatus.Connected);
        }

        return new IsbnScanViewModel(
            scanner,
            connection,
            staging ?? new FakeStagingStore(),
            deviceSettings ?? new FakeDeviceSettings(),
            taken.Add,
            hold ?? (_ => Task.CompletedTask));
    }

    /// <summary>
    /// Offline, the notice is on screen the moment the read is, not when the dial eventually gives up. That
    /// distinction is the whole point: a real offline connect costs about seven seconds — a 5 s handshake and
    /// a 2 s discovery — by which time the wizard is two screens away and the line has nobody to tell.
    /// </summary>
    [Fact]
    public async Task Offline_TheUncheckedNoticeIsThereAtOnce_NotWhenTheDialGivesUp()
    {
        var dialling = new TaskCompletionSource();
        var connection = new FakeConnectionService { ConnectGate = dialling.Task };
        var vm = new IsbnScanViewModel(
            new FakeBarcodeScanner { Next = Dune }, connection, new FakeStagingStore(),
            new FakeDeviceSettings(), _ => { }, _ => Task.CompletedTask);

        await vm.ScanCommand.ExecuteAsync(null);

        Assert.Equal(Resources.Scan_NotChecked, vm.PreviewLine);

        dialling.SetResult();
    }

    private static FakeStagingStore Holding(string isbn)
    {
        var store = new FakeStagingStore();
        store.Save(new StagedBook { ClientItemId = "a1", Isbn = isbn, Images = [] });
        return store;
    }

    [Fact]
    public async Task Scanning_AsksTheCameraForABookNumber()
    {
        var vm = New(out var scanner, out _);
        scanner.Next = "978-0-441-01359-3";

        await vm.ScanCommand.ExecuteAsync(null);

        Assert.Equal(BarcodeKind.BookNumber, scanner.LastKind);
    }

    [Fact]
    public async Task AScannedBarcode_BecomesTheReadAndTheWizardMovesOn()
    {
        var vm = New(out var scanner, out var accepted);
        scanner.Next = "978-0-441-01359-3";

        await vm.ScanCommand.ExecuteAsync(null);

        Assert.Equal(Dune, vm.AcceptedIsbn);
        Assert.True(vm.HasResult);
        Assert.Equal([Dune], accepted);
        Assert.False(vm.IsAdvancing);
    }

    [Fact]
    public async Task NothingRead_SaysSoAndStaysPut()
    {
        var vm = New(out var scanner, out var accepted);
        scanner.Next = null;

        await vm.ScanCommand.ExecuteAsync(null);

        Assert.Equal(Resources.Scan_NothingRead, vm.Message);
        Assert.False(vm.HasResult);
        Assert.Empty(accepted);
    }

    [Fact]
    public async Task Undo_TakesTheReadBackBeforeTheWizardMovesOn()
    {
        var gate = new TaskCompletionSource();
        var vm = New(out var scanner, out var accepted, hold: ct => gate.Task.WaitAsync(ct));
        scanner.Next = Dune;

        var scanning = vm.ScanCommand.ExecuteAsync(null);
        Assert.True(vm.IsAdvancing);
        Assert.True(vm.UndoCommand.CanExecute(null));

        vm.UndoCommand.Execute(null);
        await scanning;

        Assert.Null(vm.AcceptedIsbn);
        Assert.False(vm.IsAdvancing);
        Assert.Empty(accepted);
    }

    [Fact]
    public void ATypedNumber_CannotBeUsedUntilSomethingIsTyped()
    {
        var vm = New(out _, out _);

        Assert.False(vm.UseManualIsbnCommand.CanExecute(null));

        vm.ManualIsbn = "978";
        Assert.True(vm.UseManualIsbnCommand.CanExecute(null));
    }

    [Fact]
    public void TheCheckDigitIsReported_ForAGoodNumberAndABadOne()
    {
        var vm = New(out _, out _);

        vm.ManualIsbn = Dune;
        Assert.Equal(IsbnValidity.Valid, vm.ManualValidity);
        Assert.True(vm.ShowsManualValid);
        Assert.False(vm.ShowsManualWarning);

        vm.ManualIsbn = "9780441013594";
        Assert.Equal(IsbnValidity.Invalid, vm.ManualValidity);
        Assert.True(vm.ShowsManualWarning);

        vm.ManualIsbn = string.Empty;
        Assert.Equal(IsbnValidity.None, vm.ManualValidity);
        Assert.False(vm.ShowsManualValid);
        Assert.False(vm.ShowsManualWarning);
    }

    /// <summary>Misprinted ISBNs exist on real books, so a failed check digit warns and nothing more.</summary>
    [Fact]
    public async Task ATypedNumberWithABadCheckDigit_IsStillAllowedThrough()
    {
        var vm = New(out _, out var accepted);
        vm.ManualIsbn = "9780441013594";

        await vm.UseManualIsbnCommand.ExecuteAsync(null);

        Assert.True(vm.ShowsManualWarning);
        Assert.Equal(["9780441013594"], accepted);
    }

    [Fact]
    public async Task AReadTheLibraryAlreadyHas_SaysSoAndNamesTheBook()
    {
        var desktop = new StubScannerService
        {
            Preview = new IsbnPreview { InLibrary = true, Title = "Dune", Source = IsbnPreviewSource.Library },
        };
        var vm = New(out var scanner, out _, desktop);
        scanner.Next = Dune;

        await vm.ScanCommand.ExecuteAsync(null);

        Assert.Equal([Dune], desktop.Checked);
        Assert.Equal(string.Format(null, Resources.Scan_AlreadyInLibrary, "Dune"), vm.PreviewLine);
    }

    [Fact]
    public async Task AReadTheLibraryDoesNotHave_ShowsTheLookedUpTitleAsAConfidenceCheck()
    {
        var desktop = new StubScannerService
        {
            Preview = new IsbnPreview { Title = "Dune", Authors = "Frank Herbert", Source = IsbnPreviewSource.Lookup },
        };
        var vm = New(out var scanner, out _, desktop);
        scanner.Next = Dune;

        await vm.ScanCommand.ExecuteAsync(null);

        Assert.Equal(string.Format(null, Resources.Scan_TitleAndAuthors, "Dune", "Frank Herbert"), vm.PreviewLine);
    }

    [Fact]
    public async Task ANumberTheComputerKnowsNothingAbout_GetsNoLine()
    {
        var desktop = new StubScannerService { Preview = new IsbnPreview() };
        var vm = New(out var scanner, out var accepted, desktop);
        scanner.Next = Dune;

        await vm.ScanCommand.ExecuteAsync(null);

        Assert.Null(vm.PreviewLine);
        Assert.Equal([Dune], accepted);
    }

    /// <summary>Offline the read stands and the wizard moves on, but it says the number went unchecked: in
    /// UAT a silent panel read as "the computer looked and knew nothing", which is a different fact.</summary>
    [Fact]
    public async Task Offline_TheReadIsKeptAndTheScreenSaysNobodyWasAsked()
    {
        var vm = New(out var scanner, out var accepted);
        scanner.Next = Dune;

        await vm.ScanCommand.ExecuteAsync(null);

        Assert.Equal(Dune, vm.AcceptedIsbn);
        Assert.Equal(Resources.Scan_NotChecked, vm.PreviewLine);
        Assert.Equal([Dune], accepted);
    }

    [Fact]
    public async Task AComputerThatFailsTheLookup_SaysTheNumberWentUncheckedAndNothingElse()
    {
        var desktop = new StubScannerService { PreviewFails = true };
        var vm = New(out var scanner, out var accepted, desktop);
        scanner.Next = Dune;

        await vm.ScanCommand.ExecuteAsync(null);

        Assert.Equal(Resources.Scan_NotChecked, vm.PreviewLine);
        Assert.Equal([Dune], accepted);
    }

    /// <summary>
    /// The confidence line is the reason Undo is on screen, so the undo window has to start once the answer
    /// is in. In UAT the wizard had already moved on to the photo step before the title arrived, which is
    /// why the line "never showed" for a book the library already had.
    /// </summary>
    [Fact]
    public async Task AConnectedComputerIsWaitedFor_SoTheLineIsOnScreenBeforeTheWizardMovesOn()
    {
        var answer = new TaskCompletionSource();
        var desktop = new StubScannerService
        {
            Preview = new IsbnPreview { InLibrary = true, Title = "Dune", Source = IsbnPreviewSource.Library },
            PreviewGate = answer.Task,
        };

        var vm = New(out var scanner, out var accepted, desktop);
        scanner.Next = Dune;

        var scanning = vm.ScanCommand.ExecuteAsync(null);

        // The computer has not answered, so the wizard is still here: the undo window has not opened and
        // nothing has been handed to the photo step.
        Assert.Null(vm.PreviewLine);
        Assert.Empty(accepted);

        answer.SetResult();
        await scanning;

        Assert.Equal(string.Format(null, Resources.Scan_AlreadyInLibrary, "Dune"), vm.PreviewLine);
        Assert.Equal([Dune], accepted);
    }

    /// <summary>Offline the wizard does not wait: the dial that would answer takes seconds it is not worth
    /// spending on every book of a stack, and the panel already says the number went unchecked.</summary>
    [Fact]
    public async Task Offline_TheWizardDoesNotWaitForAComputerThatIsNotThere()
    {
        var vm = New(out var scanner, out var accepted);
        scanner.Next = Dune;

        var scanning = vm.ScanCommand.ExecuteAsync(null);

        // Comfortably inside the grace a connected computer would get, so a wizard that waited would lose.
        var first = await Task.WhenAny(
            scanning,
            Task.Delay(TimeSpan.FromMilliseconds(500), TestContext.Current.CancellationToken));

        Assert.Same(scanning, first);
        await scanning;
        Assert.Equal([Dune], accepted);
    }

    /// <summary>Found in UAT: the same book could be scanned into the batch over and over with nothing on
    /// screen to notice. Reported rather than refused — two copies of a book is a real thing to catalogue —
    /// but reported in time to reach Undo, which is why the window runs twice.</summary>
    [Fact]
    public async Task ABookTheBatchAlreadyHolds_IsFlaggedAndGetsTheUndoWindowTwice()
    {
        int holds = 0;
        var vm = New(out var scanner, out var accepted, staging: Holding(Dune),
            hold: _ => { holds++; return Task.CompletedTask; });
        scanner.Next = Dune;

        await vm.ScanCommand.ExecuteAsync(null);

        Assert.True(vm.IsDuplicate);
        Assert.Equal(Resources.Scan_AlreadyInBatch, vm.DuplicateLine);
        Assert.Equal(2, holds);
        Assert.Equal([Dune], accepted);
    }

    /// <summary>
    /// UAT 1g, third time: the window has to open on the line landing, not on a timer that expects it to have
    /// landed. Raced against a fixed grace, a computer slower than the grace spends the whole window showing a
    /// blank panel and delivers the title just as the wizard leaves — which is why raising the grace twice
    /// never fixed it. Stated as the property rather than as a duration, because the durations were never the
    /// mechanism: **when the window opens, the line is already on screen.**
    /// <para>
    /// This also subsumes UAT 1h-bis structurally. A duplicate takes its two windows after the same starting
    /// point an ordinary read takes its one, so it can no longer be the shorter of the two by arithmetic —
    /// the old <c>grace &lt;= window</c> guard existed only to stop those ranges crossing and has nothing left
    /// to protect.
    /// </para>
    /// </summary>
    [Fact]
    public async Task TheUndoWindow_DoesNotOpenUntilTheConfidenceLineHasLanded()
    {
        var answering = new TaskCompletionSource();
        var desktop = new StubScannerService
        {
            Preview = new IsbnPreview { InLibrary = true, Title = "Dune", Source = IsbnPreviewSource.Library },
        };
        var connection = new FakeConnectionService
        {
            Connection = new StubConnection(desktop),
            ConnectGate = answering.Task,
        };
        connection.Report(ConnectionStatus.Connected);

        IsbnScanViewModel? vm = null;
        string? lineWhenTheWindowOpened = null;
        int windows = 0;

        vm = new IsbnScanViewModel(
            new FakeBarcodeScanner { Next = Dune }, connection, new FakeStagingStore(),
            new FakeDeviceSettings(), _ => { },
            _ =>
            {
                windows++;
                lineWhenTheWindowOpened ??= vm!.PreviewLine;
                return Task.CompletedTask;
            });

        var scanning = vm.ScanCommand.ExecuteAsync(null);

        // The computer is still thinking, so no window may have opened — however long it takes.
        Assert.Equal(0, windows);

        answering.SetResult();
        await scanning;

        Assert.Equal(1, windows);
        Assert.Equal(string.Format(null, Resources.Scan_AlreadyInLibrary, "Dune"), lineWhenTheWindowOpened);
    }

    /// <summary>The wait above is bounded, or a computer that stopped answering mid-read would hold the wizard
    /// open for good. What it falls back to is the unchecked notice, not a blank panel: the window is spent
    /// either way, and a blank line reads as "asked, knows nothing about this book".</summary>
    [Fact]
    public async Task AComputerThatNeverAnswers_GivesUpAndSaysTheReadWasNotChecked()
    {
        var neverAnswers = new TaskCompletionSource();
        var connection = new FakeConnectionService
        {
            Connection = new StubConnection(new StubScannerService()),
            ConnectGate = neverAnswers.Task,
        };
        connection.Report(ConnectionStatus.Connected);

        var accepted = new List<string>();
        var vm = new IsbnScanViewModel(
            new FakeBarcodeScanner { Next = Dune }, connection, new FakeStagingStore(),
            new FakeDeviceSettings(), accepted.Add, _ => Task.CompletedTask,
            previewTimeout: TimeSpan.FromMilliseconds(50));

        await vm.ScanCommand.ExecuteAsync(null);

        Assert.Equal(Resources.Scan_NotChecked, vm.PreviewLine);
        Assert.Equal([Dune], accepted);

        neverAnswers.SetResult();
    }

    [Fact]
    public async Task ABookTheBatchDoesNotHold_IsNotFlagged()
    {
        int holds = 0;
        var vm = New(out var scanner, out _, staging: Holding("9780306406157"),
            hold: _ => { holds++; return Task.CompletedTask; });
        scanner.Next = Dune;

        await vm.ScanCommand.ExecuteAsync(null);

        Assert.False(vm.IsDuplicate);
        Assert.Null(vm.DuplicateLine);
        Assert.Equal(1, holds);
    }

    /// <summary>
    /// The read carries the mark its own number earns. A scan is not proof of a book number — a cover can
    /// carry other barcodes — and the panel used to print a tick over whatever came back, which is the app
    /// agreeing with a misread.
    /// </summary>
    [Fact]
    public async Task TheReadIsMarkedWithItsOwnCheckDigit_NotWithATickRegardless()
    {
        var vm = New(out var scanner, out _);

        scanner.Next = Dune;
        await vm.ScanCommand.ExecuteAsync(null);
        Assert.True(vm.ShowsAcceptedValid);
        Assert.False(vm.ShowsAcceptedWarning);

        // Eight digits: a valid EAN-8 and an impossible ISBN, which is exactly what UAT scanned off a cover.
        scanner.Next = "60367310";
        await vm.ScanCommand.ExecuteAsync(null);
        Assert.Equal(IsbnValidity.Invalid, vm.AcceptedValidity);
        Assert.True(vm.ShowsAcceptedWarning);
        Assert.False(vm.ShowsAcceptedValid);
    }

    [Fact]
    public async Task Undo_TakesBackTheDuplicateNoticeWithTheRead()
    {
        var gate = new TaskCompletionSource();
        var vm = New(out var scanner, out _, staging: Holding(Dune), hold: ct => gate.Task.WaitAsync(ct));
        scanner.Next = Dune;

        var scanning = vm.ScanCommand.ExecuteAsync(null);
        Assert.True(vm.IsDuplicate);

        vm.UndoCommand.Execute(null);
        await scanning;

        Assert.False(vm.IsDuplicate);
        Assert.Null(vm.PreviewLine);
    }

    [Fact]
    public async Task ASecondRead_ReplacesTheFirst()
    {
        var vm = New(out var scanner, out var accepted);
        scanner.Next = Dune;
        await vm.ScanCommand.ExecuteAsync(null);

        scanner.Next = "9780306406157";
        await vm.ScanCommand.ExecuteAsync(null);

        Assert.Equal("9780306406157", vm.AcceptedIsbn);
        Assert.Equal([Dune, "9780306406157"], accepted);
    }

    /// <summary>A refused camera is not the same as an empty read, and must not be reported as one: the way
    /// back is in the device's settings, and the screen is what has to say so.</summary>
    [Fact]
    public async Task ARefusedCamera_ExplainsItselfAndOffersTheWayBack()
    {
        var settings = new FakeDeviceSettings();
        var vm = New(out var scanner, out _, deviceSettings: settings);
        scanner.NextScan = BarcodeScan.PermissionDenied;

        await vm.ScanCommand.ExecuteAsync(null);

        Assert.True(vm.CameraAccess.IsVisible);
        Assert.Equal(Resources.Camera_PermissionDenied, vm.CameraAccess.Message);
        Assert.True(vm.CameraAccess.CanOpenSettings);
        Assert.NotEqual(Resources.Scan_NothingRead, vm.Message);

        vm.CameraAccess.OpenSettingsCommand.Execute(null);
        Assert.Equal(1, settings.OpenCount);
    }

    /// <summary>Nothing to scan with is not something the user can grant their way out of, so it is said
    /// without a settings button behind it.</summary>
    [Fact]
    public async Task NoCameraAtAll_IsSaidWithoutOfferingSettings()
    {
        var vm = New(out var scanner, out _);
        scanner.NextScan = BarcodeScan.CameraUnavailable;

        await vm.ScanCommand.ExecuteAsync(null);

        Assert.True(vm.CameraAccess.IsVisible);
        Assert.Equal(Resources.Camera_Unavailable, vm.CameraAccess.Message);
        Assert.False(vm.CameraAccess.CanOpenSettings);
        Assert.False(vm.CameraAccess.OpenSettingsCommand.CanExecute(null));
    }

    /// <summary>Typing the number is what makes a refused camera survivable rather than fatal, and a later
    /// read clears the notice rather than leaving it standing over a working camera.</summary>
    [Fact]
    public async Task WithTheCameraRefused_TypingTheNumberStillWorks()
    {
        var vm = New(out var scanner, out var accepted);
        scanner.NextScan = BarcodeScan.PermissionDenied;
        await vm.ScanCommand.ExecuteAsync(null);

        vm.ManualIsbn = Dune;
        await vm.UseManualIsbnCommand.ExecuteAsync(null);
        Assert.Equal([Dune], accepted);

        scanner.NextScan = null;
        scanner.Next = "9780306406157";
        await vm.ScanCommand.ExecuteAsync(null);

        Assert.False(vm.CameraAccess.IsVisible);
    }

    [Fact]
    public async Task AClosedCamera_IsStillJustAnEmptyRead()
    {
        var vm = New(out var scanner, out _);
        scanner.NextScan = BarcodeScan.Nothing;

        await vm.ScanCommand.ExecuteAsync(null);

        Assert.False(vm.CameraAccess.IsVisible);
        Assert.Equal(Resources.Scan_NothingRead, vm.Message);
    }
}
