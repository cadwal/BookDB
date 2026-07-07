using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using BookDB.Data.Interfaces;
using BookDB.Desktop.Helpers;
using BookDB.Desktop.Services;
using BookDB.Desktop.ViewModels;
using BookDB.Desktop.Views;
using BookDB.Logic.Services;
using BookDB.Models.Entities;
using BookDB.Models.Interfaces;
using BookDB.Models.Metadata;
using CommunityToolkit.Mvvm.Messaging;
using NSubstitute;
using Xunit;

namespace BookDB.Desktop.UITests;

/// <summary>
/// Dialog keyboard chrome: every dialog that declares a default or cancel button honours Enter and Esc — with
/// Enter staying gated exactly like the button it triggers, and destructive choices never sitting on Enter.
/// </summary>
public class DialogKeyboardTests : HeadlessTest
{
    [Fact]
    public async Task BackupFormatDialog_EnterConfirms_EscCancels()
    {
        var picker = Substitute.For<IFilePickerService>();

        await RunUi(() =>
        {
            var (vm, dialog, closed) = OpenBackupDialog(picker);
            dialog.Press(PhysicalKey.Enter);
            Assert.Equal(BackupFormatDialogViewModel.SqliteFormat, vm.Result);
            Assert.True(closed());
            dialog.Close();

            var (escVm, escDialog, escClosed) = OpenBackupDialog(picker);
            escDialog.Press(PhysicalKey.Escape);
            Assert.Null(escVm.Result);
            Assert.False(escClosed());
            escDialog.Close();
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task CheckOutDialog_EnterStaysGatedUntilABorrowerIsNamed_EscCancels()
    {
        var ct = TestContext.Current.CancellationToken;

        await RunUi(async () =>
        {
            using var host = TestHost.Create();
            var book = await SeedData.AddBookAsync(host, "Loanable", ct);
            var loans = host.Resolve<ILoanService>();

            var vm = host.Resolve<CheckOutDialogViewModel>();
            await vm.InitializeAsync(book.BookId);
            bool? closed = null;
            vm.CloseDialog = r => closed = r;
            var dialog = new CheckOutDialog { DataContext = vm };
            dialog.Show();
            Ui.Pump();

            // With no borrower named, Enter is a no-op — exactly like the gated Confirm button.
            dialog.Press(PhysicalKey.Enter);
            Assert.Null(closed);
            Assert.Null(await loans.GetActiveLoanAsync(book.BookId, ct));

            // Typing any name opens the suggestion list (it always offers "add new borrower"), so the first
            // Enter commits the suggestion and closes the list; the second reaches the default button.
            var input = dialog.Find<AutoCompleteBox>().Descendants<TextBox>().First();
            dialog.TypeInto(input, "Zelda Newcomer");
            Assert.Equal("Zelda Newcomer", vm.SearchText);
            Assert.True(dialog.ButtonFor(vm.ConfirmCommand).IsEffectivelyEnabled);
            dialog.Press(PhysicalKey.Enter);
            Assert.Null(closed);
            Assert.Equal("Zelda Newcomer", vm.SearchText);
            dialog.Press(PhysicalKey.Enter);
            await Ui.PumpUntil(() => closed == true, ct);
            Assert.NotNull(await loans.GetActiveLoanAsync(book.BookId, ct));
            dialog.Close();

            // Esc cancels a fresh check-out without recording anything.
            var second = await SeedData.AddBookAsync(host, "Kept On Shelf", ct);
            var escVm = host.Resolve<CheckOutDialogViewModel>();
            await escVm.InitializeAsync(second.BookId);
            bool? escClosed = null;
            escVm.CloseDialog = r => escClosed = r;
            var escDialog = new CheckOutDialog { DataContext = escVm };
            escDialog.Show();
            Ui.Pump();
            escDialog.Press(PhysicalKey.Escape);
            Assert.False(escClosed);
            Assert.Null(await loans.GetActiveLoanAsync(second.BookId, ct));
            escDialog.Close();
        });
    }

    [Fact]
    public async Task AddBookDialog_EnterStaysGatedUntilATitleIsTyped_ThenSaves()
    {
        var ct = TestContext.Current.CancellationToken;

        await RunUi(async () =>
        {
            using var host = TestHost.Create();
            var vm = host.Resolve<AddBookDialogViewModel>();
            vm.Reset(null);
            await vm.InitializeAsync();
            bool? closed = null;
            vm.CloseDialog = r => closed = r;
            var dialog = new AddBookDialog { DataContext = vm };
            dialog.Show();
            Ui.Pump();

            dialog.Press(PhysicalKey.Enter);
            Assert.Null(closed);

            dialog.TypeInto(dialog.Descendants<TextBox>()[0], "Entered Title");
            dialog.Press(PhysicalKey.Enter);
            await Ui.PumpUntil(() => closed == true, ct);

            var list = host.Resolve<BookListViewModel>();
            await list.LoadBooksAsync(ct);
            Assert.Contains(list.Books, b => b.Title == "Entered Title");
            dialog.Close();
        });
    }

    [Fact]
    public async Task AdvancedSearchDialog_EnterFromTheConditionBox_RunsTheSearch()
    {
        var ct = TestContext.Current.CancellationToken;

        await RunUi(async () =>
        {
            using var host = TestHost.Create();
            var vm = host.Resolve<AdvancedSearchViewModel>();
            await vm.InitializeAsync();
            bool? result = null;
            vm.SetCloseAction(r => result = r);
            var dialog = new AdvancedSearchDialog { DataContext = vm };
            dialog.Show();
            Ui.Pump();

            // Enter pressed while still typing in the value box applies the search.
            var valueBox = dialog.Descendants<TextBox>().First(t => t.IsEffectivelyVisible);
            dialog.TypeInto(valueBox, "Dune");
            dialog.Press(PhysicalKey.Enter);
            await Ui.PumpUntil(() => result == true, ct);
            dialog.Close();
        });
    }

    [Fact]
    public async Task ConnectDialog_EnterTakesTheSafeQuit_NeverTheRiskyConnect()
    {
        await RunUi(() =>
        {
            var session = new ClientSession { Hostname = "other-pc", AppVersion = "2.1.0", LastSeenAt = DateTime.UtcNow };
            var vm = new ConnectDialogViewModel([session], TimeProvider.System);
            var dialogClosed = false;
            vm.CloseDialog = () => dialogClosed = true;
            var dialog = new ConnectDialog { DataContext = vm };
            dialog.Show();
            Ui.Pump();

            // Connect-anyway is still in its reflex-guard countdown; Quit is the Enter default.
            Assert.False(vm.CanConnectAnyway);
            dialog.Press(PhysicalKey.Enter);
            Assert.Equal(ConnectChoice.Quit, vm.Result);
            Assert.True(dialogClosed);
            dialog.Close();
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task StartupFailureDialog_EnterRetries_AndProceedsOnSuccess()
    {
        var ct = TestContext.Current.CancellationToken;

        await RunUi(async () =>
        {
            var probes = 0;
            var failure = ConnectionProbeResult.Failed(ConnectionProbeStatus.ConnectionRefused, "down");
            var vm = new StartupFailureViewModel(failure, _ =>
            {
                probes++;
                return Task.FromResult(ConnectionProbeResult.Succeeded("test-server", bookCount: 1));
            });
            var dialogClosed = false;
            vm.CloseDialog = () => dialogClosed = true;
            var dialog = new StartupFailureDialog { DataContext = vm };
            dialog.Show();
            Ui.Pump();

            dialog.Press(PhysicalKey.Enter);
            await Ui.PumpUntil(() => dialogClosed, ct);
            Assert.Equal(1, probes);
            Assert.Equal(StartupFailureOutcome.Proceed, vm.Outcome);
            dialog.Close();
        });
    }

    [Fact]
    public async Task ConfirmDialog_EnterAnswersYes_EscAnswersNo()
    {
        await RunUi(async () =>
        {
            var (dialog, result) = AppDialogs.BuildConfirmDialog("Confirm?", "Body.");
            dialog.Show();
            Ui.Pump();
            dialog.Press(PhysicalKey.Enter);
            Assert.True(await result);
            dialog.Close();

            var (escDialog, escResult) = AppDialogs.BuildConfirmDialog("Confirm?", "Body.");
            escDialog.Show();
            Ui.Pump();
            escDialog.Press(PhysicalKey.Escape);
            Assert.False(await escResult);
            escDialog.Close();
        });
    }

    [Fact]
    public async Task InfoAndAboutDialogs_CloseOnBothEnterAndEsc()
    {
        await RunUi(() =>
        {
            foreach (var key in new[] { PhysicalKey.Enter, PhysicalKey.Escape })
            {
                foreach (var build in new Func<Window>[]
                         { () => AppDialogs.BuildInfoDialog("Read me."), AppDialogs.BuildAboutDialog })
                {
                    var dialog = build();
                    var closed = false;
                    dialog.Closed += (_, _) => closed = true;
                    dialog.Show();
                    Ui.Pump();
                    dialog.Press(key);
                    Assert.True(closed);
                }
            }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task UnsavedChangesDialog_EnterSaves_EscKeepsEditing()
    {
        await RunUi(async () =>
        {
            Assert.Equal(UnsavedChangesResult.Save, await ResultAfterKey<UnsavedChangesResult>(
                AppDialogs.BuildUnsavedChangesDialog("Some Book"), PhysicalKey.Enter));
            Assert.Equal(UnsavedChangesResult.KeepEditing, await ResultAfterKey<UnsavedChangesResult>(
                AppDialogs.BuildUnsavedChangesDialog("Some Book"), PhysicalKey.Escape));
        });
    }

    [Fact]
    public async Task ShutdownWarningDialog_EscKeepsRunning_AndQuitIsNeverTheDefault()
    {
        await RunUi(async () =>
        {
            var dialog = AppDialogs.BuildShutdownWarningDialog("Quit anyway", "Keep running");
            Assert.DoesNotContain(dialog.Descendants<Button>(), b => b.IsDefault);
            Assert.False(await ResultAfterKey<bool?>(dialog, PhysicalKey.Escape));
        });
    }

    [Fact]
    public async Task WriteFailureDialog_EnterTakesTheSafeRetry()
    {
        await RunUi(async () =>
        {
            var (dialog, result) = AppDialogs.BuildWriteFailureDialog("Write failed.");
            dialog.Show();
            Ui.Pump();
            dialog.Press(PhysicalKey.Enter);
            Assert.Equal(WriteFailureChoice.Retry, await result);
            dialog.Close();
        });
    }

    [Fact]
    public async Task ConnectionLostDialog_EnterKeepsWaiting()
    {
        await RunUi(async () =>
        {
            var (dialog, result) = AppDialogs.BuildConnectionLostEscalationDialog();
            dialog.Show();
            Ui.Pump();
            dialog.Press(PhysicalKey.Enter);
            Assert.False(await result); // false = keep waiting, the non-destructive choice
            dialog.Close();
        });
    }

    [Fact]
    public async Task DeleteConfirmationDialog_NeverPutsDeleteOnEnter_AndEscDeclines()
    {
        await RunUi(async () =>
        {
            var dialog = AppDialogs.BuildDeleteConfirmationDialog("Delete this?");
            dialog.Show();
            Ui.Pump();
            Assert.DoesNotContain(dialog.Descendants<Button>(), b => b.IsDefault);
            dialog.Close();

            Assert.False(await ResultAfterKey<bool?>(
                AppDialogs.BuildDeleteConfirmationDialog("Delete this?"), PhysicalKey.Escape));
        });
    }

    [Fact]
    public async Task DuplicateIsbnDialog_EscTakesCancel()
    {
        await RunUi(async () =>
        {
            var result = await ResultAfterKey<DuplicateIsbnResult>(
                AppDialogs.BuildDuplicateIsbnDialog("9780000000000", "Existing Title"), PhysicalKey.Escape);
            Assert.Equal(DuplicateIsbnResult.Cancel, result);
        });
    }

    [Fact]
    public async Task IsbnPromptDialog_EscDismissesWithNoIsbn()
    {
        await RunUi(async () =>
        {
            Assert.Null(await ResultAfterKey<string?>(AppDialogs.BuildIsbnPromptDialog(), PhysicalKey.Escape));
        });
    }

    [Fact]
    public async Task AddBookDialog_EscCancels_EvenMidTyping()
    {
        var ct = TestContext.Current.CancellationToken;

        await RunUi(async () =>
        {
            using var host = TestHost.Create();
            var vm = host.Resolve<AddBookDialogViewModel>();
            vm.Reset(null);
            await vm.InitializeAsync();
            bool? closed = null;
            vm.CloseDialog = r => closed = r;
            var dialog = new AddBookDialog { DataContext = vm };
            dialog.Show();
            Ui.Pump();

            dialog.TypeInto(dialog.Descendants<TextBox>()[0], "Abandoned Title");
            dialog.Press(PhysicalKey.Escape);
            await Ui.PumpUntil(() => closed == false, ct);

            var list = host.Resolve<BookListViewModel>();
            await list.LoadBooksAsync(ct);
            Assert.DoesNotContain(list.Books, b => b.Title == "Abandoned Title");
            dialog.Close();
        });
    }

    [Fact]
    public async Task BulkEditDialog_EscCancels()
    {
        var ct = TestContext.Current.CancellationToken;

        await RunUi(async () =>
        {
            using var host = TestHost.Create();
            var book = await SeedData.AddBookAsync(host, "Bulk Kept", ct);
            var vm = host.Resolve<BulkEditViewModel>();
            await vm.InitializeAsync([book.BookId]);
            bool? closed = null;
            vm.CloseDialog = r => closed = r;
            var dialog = new BulkEditDialog { DataContext = vm };
            dialog.Show();
            Ui.Pump();

            dialog.Press(PhysicalKey.Escape);
            Assert.False(closed);
            dialog.Close();
        });
    }

    [Fact]
    public async Task AdvancedSearchDialog_EscCancels()
    {
        await RunUi(async () =>
        {
            using var host = TestHost.Create();
            var vm = host.Resolve<AdvancedSearchViewModel>();
            await vm.InitializeAsync();
            bool? result = null;
            vm.SetCloseAction(r => result = r);
            var dialog = new AdvancedSearchDialog { DataContext = vm };
            dialog.Show();
            Ui.Pump();

            dialog.Press(PhysicalKey.Escape);
            Assert.False(result);
            dialog.Close();
        });
    }

    [Fact]
    public async Task CsvColumnPickerDialog_EscExportsNothing()
    {
        await RunUi(() =>
        {
            using var host = TestHost.Create();
            var vm = host.Resolve<CsvColumnPickerViewModel>();
            vm.Initialize(["Title", "ISBN"], ["Title"]);
            var closed = false;
            IReadOnlyList<string>? columns = ["sentinel"];
            vm.CloseDialog = c => { closed = true; columns = c; };
            var dialog = new CsvColumnPickerDialog { DataContext = vm };
            dialog.Show();
            Ui.Pump();

            dialog.Press(PhysicalKey.Escape);
            Assert.True(closed);
            Assert.Null(columns);
            dialog.Close();
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task LookupWizardDialog_EscCancels()
    {
        await RunUi(() =>
        {
            using var host = TestHost.Create();
            var vm = host.Resolve<LookupWizardViewModel>();
            bool? closed = null;
            vm.CloseDialog = r => closed = r;
            var dialog = new LookupWizardDialog { DataContext = vm };
            dialog.Show();
            Ui.Pump();

            dialog.Press(PhysicalKey.Escape);
            Assert.False(closed);
            dialog.Close();
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task MergeReviewDialog_EscCancels()
    {
        await RunUi(() =>
        {
            using var host = TestHost.Create();
            bool? closed = null;
            var vm = new MergeReviewViewModel(
                sources: Array.Empty<BookMetadata>(),
                currentBook: null,
                coverOptions: Array.Empty<CoverOption>(),
                bookMetadataService: host.Resolve<IBookMetadataService>(),
                messenger: host.Resolve<IMessenger>(),
                existingBookId: null,
                collectionId: null,
                closeDialog: r => closed = r,
                windowService: host.Resolve<IWindowService>());
            var dialog = new MergeReviewDialog { DataContext = vm };
            dialog.Show();
            Ui.Pump();

            dialog.Press(PhysicalKey.Escape);
            Assert.False(closed);
            dialog.Close();
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task MergeTargetPickerDialog_EscPicksNoTarget()
    {
        await RunUi(() =>
        {
            using var host = TestHost.Create();
            var vm = host.Resolve<MergeTargetPickerViewModel>();
            vm.Initialize("Source", Array.Empty<LookupEntryRow>(), sourceId: 1);
            var closed = false;
            int? target = 7;
            vm.CloseDialog = id => { closed = true; target = id; };
            var dialog = new MergeTargetPickerDialog { DataContext = vm };
            dialog.Show();
            Ui.Pump();

            dialog.Press(PhysicalKey.Escape);
            Assert.True(closed);
            Assert.Null(target);
            dialog.Close();
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task PrintDialog_PreviewLosesItsEnterDefault_WhileNamingAPreset()
    {
        var ct = TestContext.Current.CancellationToken;

        await RunUi(async () =>
        {
            using var host = TestHost.Create();
            var vm = host.Resolve<PrintDialogViewModel>();
            await vm.InitializeAsync(null, null, null, null, sortAscending: true, bookCount: 0, ct);
            var dialog = new PrintDialog { DataContext = vm };
            dialog.Show();
            Ui.Pump();

            // Enter must not generate a report while the user is typing a preset name.
            var previewButton = dialog.ButtonFor(vm.PreviewCommand);
            Assert.True(previewButton.IsDefault);
            vm.NewPresetCommand.Execute(null);
            Ui.Pump();
            Assert.False(previewButton.IsDefault);
            vm.CancelPresetNameCommand.Execute(null);
            Ui.Pump();
            Assert.True(previewButton.IsDefault);
            dialog.Close();
        });
    }

    // ─── Plumbing ────────────────────────────────────────────────────────────

    /// <summary>Shows a code-built dialog modally over a throwaway owner (the result only surfaces through
    /// ShowDialog), presses the key, and returns the dialog's result.</summary>
    private static async Task<T> ResultAfterKey<T>(Window dialog, PhysicalKey key)
    {
        var owner = new Window();
        owner.Show();
        var result = dialog.ShowDialog<T>(owner);
        Ui.Pump();
        dialog.Press(key);
        Ui.Pump();
        var value = await result;
        owner.Close();
        return value;
    }

    private static (BackupFormatDialogViewModel Vm, BackupFormatDialog Dialog, Func<bool?> Closed) OpenBackupDialog(
        IFilePickerService picker)
    {
        var vm = new BackupFormatDialogViewModel(
            supportsFileBackup: true,
            configDefault: BackupFormatDialogViewModel.SqliteFormat,
            defaultFolder: System.IO.Path.GetTempPath(),
            filePicker: picker);
        bool? closed = null;
        vm.CloseDialog = accepted => closed = accepted;
        var dialog = new BackupFormatDialog { DataContext = vm };
        dialog.Show();
        Ui.Pump();
        return (vm, dialog, () => closed);
    }
}
