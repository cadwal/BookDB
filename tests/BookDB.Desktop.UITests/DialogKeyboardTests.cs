using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using BookDB.Data.Interfaces;
using BookDB.Desktop.Services;
using BookDB.Desktop.ViewModels;
using BookDB.Desktop.Views;
using BookDB.Logic.Import;
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
    public async Task ConfirmDialog_EnterAnswersYes_EscAnswersNo_XAnswersNothing()
    {
        await RunUi(async () =>
        {
            using var host = TestHost.Create();
            var (main, windowService) = await ShowMainWindowAsync(host);

            Assert.True(await WrapperResultAfter(
                main, () => windowService.ShowConfirmAsync("Confirm?", "Body.", main),
                d => d.Press(PhysicalKey.Enter)));
            Assert.False(await WrapperResultAfter(
                main, () => windowService.ShowConfirmAsync("Confirm?", "Body.", main),
                d => d.Press(PhysicalKey.Escape)));
            Assert.Null(await WrapperResultAfter(
                main, () => windowService.ShowConfirmAsync("Confirm?", "Body.", main),
                d => d.Close()));
            main.Close();
        });
    }

    [Fact]
    public async Task InfoDialog_ClosesOnBothEnterAndEsc()
    {
        await RunUi(async () =>
        {
            using var host = TestHost.Create();
            var (main, windowService) = await ShowMainWindowAsync(host);

            foreach (var key in new[] { PhysicalKey.Enter, PhysicalKey.Escape })
            {
                var shown = windowService.ShowInfoAsync("Read me.");
                Ui.Pump();
                var dialog = main.OwnedWindows.OfType<MessageDialog>().Single();
                dialog.Press(key);
                await shown;
            }
            main.Close();
        });
    }

    [Fact]
    public async Task AboutWindow_ShowsResourceBackedVersion_AndClosesOnBothEnterAndEsc()
    {
        await RunUi(async () =>
        {
            using var host = TestHost.Create();
            var (main, windowService) = await ShowMainWindowAsync(host);

            var version = typeof(AboutWindow).Assembly.GetName().Version!;
            var expected = string.Format(BookDB.Desktop.Localization.Resources.About_Version,
                $"{version.Major}.{version.Minor}.{version.Build}");

            foreach (var key in new[] { PhysicalKey.Enter, PhysicalKey.Escape })
            {
                var shown = windowService.ShowAboutAsync();
                Ui.Pump();
                var about = main.OwnedWindows.OfType<AboutWindow>().Single();
                Assert.Contains(about.Descendants<TextBlock>(), t => t.Text == expected);
                about.Press(key);
                await shown;
            }
            main.Close();
        });
    }

    [Fact]
    public async Task UnsavedChangesDialog_EnterSaves_EscAndXKeepEditing()
    {
        await RunUi(async () =>
        {
            using var host = TestHost.Create();
            var (main, windowService) = await ShowMainWindowAsync(host);

            Assert.Equal(UnsavedChangesResult.Save, await WrapperResultAfter(
                main, () => windowService.ShowUnsavedChangesDialogAsync("Some Book"),
                d => d.Press(PhysicalKey.Enter)));
            Assert.Equal(UnsavedChangesResult.KeepEditing, await WrapperResultAfter(
                main, () => windowService.ShowUnsavedChangesDialogAsync("Some Book"),
                d => d.Press(PhysicalKey.Escape)));
            Assert.Equal(UnsavedChangesResult.KeepEditing, await WrapperResultAfter(
                main, () => windowService.ShowUnsavedChangesDialogAsync("Some Book"),
                d => d.Close()));
            main.Close();
        });
    }

    [Fact]
    public async Task ShutdownWarningDialog_EscKeepsRunning_AndQuitIsNeverTheDefault()
    {
        await RunUi(async () =>
        {
            using var host = TestHost.Create();
            var (main, windowService) = await ShowMainWindowAsync(host);

            var warn = windowService.ShowMainShutdownWarningAsync();
            Ui.Pump();
            var dialog = main.OwnedWindows.OfType<MessageDialog>().Single();
            Assert.DoesNotContain(dialog.Descendants<Button>(), b => b.IsDefault);
            dialog.Press(PhysicalKey.Escape);
            Assert.False(await warn);

            // Closing via the title-bar X is the same safe keep-running answer.
            var second = windowService.ShowMainShutdownWarningAsync();
            Ui.Pump();
            main.OwnedWindows.OfType<MessageDialog>().Single().Close();
            Ui.Pump();
            Assert.False(await second);
            main.Close();
        });
    }

    [Fact]
    public async Task WriteFailureDialog_EnterEscAndXAllTakeTheSafeRetry()
    {
        await RunUi(async () =>
        {
            using var host = TestHost.Create();
            var (main, windowService) = await ShowMainWindowAsync(host);

            foreach (var dismiss in new Action<MessageDialog>[]
                     { d => d.Press(PhysicalKey.Enter), d => d.Press(PhysicalKey.Escape), d => d.Close() })
            {
                Assert.Equal(WriteFailureChoice.Retry, await WrapperResultAfter(
                    main, () => windowService.ShowWriteFailureDialogAsync("Write failed."), dismiss));
            }
            main.Close();
        });
    }

    [Fact]
    public async Task ConnectionLostDialog_EnterEscAndXAllKeepWaiting()
    {
        await RunUi(async () =>
        {
            using var host = TestHost.Create();
            var (main, windowService) = await ShowMainWindowAsync(host);

            foreach (var dismiss in new Action<MessageDialog>[]
                     { d => d.Press(PhysicalKey.Enter), d => d.Press(PhysicalKey.Escape), d => d.Close() })
            {
                // false = keep waiting, the non-destructive choice
                Assert.False(await WrapperResultAfter(
                    main, windowService.ShowConnectionLostEscalationDialogAsync, dismiss));
            }
            main.Close();
        });
    }

    [Fact]
    public async Task DeleteConfirmationDialog_NeverPutsDeleteOnEnter_AndEscDeclines()
    {
        await RunUi(async () =>
        {
            using var host = TestHost.Create();
            var (main, windowService) = await ShowMainWindowAsync(host);

            var confirm = windowService.ShowDeleteConfirmationAsync("Delete this?");
            Ui.Pump();
            var dialog = main.OwnedWindows.OfType<MessageDialog>().Single();
            Assert.DoesNotContain(dialog.Descendants<Button>(), b => b.IsDefault);
            Assert.Contains("dangerButton", dialog.Descendants<Button>()[0].Classes);
            dialog.Press(PhysicalKey.Escape);
            Assert.False(await confirm);

            // Title-bar X declines just like Esc — never deletes.
            var second = windowService.ShowDeleteConfirmationAsync("Delete this?");
            Ui.Pump();
            main.OwnedWindows.OfType<MessageDialog>().Single().Close();
            Ui.Pump();
            Assert.False(await second);
            main.Close();
        });
    }

    [Fact]
    public async Task DuplicateIsbnDialog_NeverPutsAWriteOnEnter_AndEscAndXCancel()
    {
        await RunUi(async () =>
        {
            using var host = TestHost.Create();
            var (main, windowService) = await ShowMainWindowAsync(host);

            // Update-existing and add-as-new both write to the library — neither may sit on Enter.
            var prompt = windowService.ShowDuplicateIsbnDialogAsync("9780000000000", "Existing Title");
            Ui.Pump();
            var dialog = main.OwnedWindows.OfType<MessageDialog>().Single();
            Assert.DoesNotContain(dialog.Descendants<Button>(), b => b.IsDefault);
            dialog.Press(PhysicalKey.Escape);
            Assert.Equal(DuplicateIsbnResult.Cancel, await prompt);

            Assert.Equal(DuplicateIsbnResult.Cancel, await WrapperResultAfter(
                main, () => windowService.ShowDuplicateIsbnDialogAsync("9780000000000", "Existing Title"),
                d => d.Close()));
            main.Close();
        });
    }

    [Fact]
    public async Task BackupConflictDialog_EnterSavesUnderASuffix_EscAndXCancel()
    {
        await RunUi(async () =>
        {
            using var host = TestHost.Create();
            var (main, windowService) = await ShowMainWindowAsync(host);
            var existing = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "backup.zip");

            Assert.Equal(BackupConflictChoice.AddSuffix, await WrapperResultAfter(
                main, () => windowService.ShowBackupConflictAsync(existing), d => d.Press(PhysicalKey.Enter)));
            Assert.Equal(BackupConflictChoice.Cancel, await WrapperResultAfter(
                main, () => windowService.ShowBackupConflictAsync(existing), d => d.Press(PhysicalKey.Escape)));
            Assert.Equal(BackupConflictChoice.Cancel, await WrapperResultAfter(
                main, () => windowService.ShowBackupConflictAsync(existing), d => d.Close()));
            main.Close();
        });
    }

    [Fact]
    public async Task RestoreTargetDialog_EnterTakesArchived_CurrentByClick_EscAndXCancel()
    {
        await RunUi(async () =>
        {
            using var host = TestHost.Create();
            var (main, windowService) = await ShowMainWindowAsync(host);

            async Task<RestoreTargetChoice> After(Func<RestoreTargetDialog, Task> dismiss)
            {
                var task = windowService.ShowRestoreTargetAsync("PostgreSQL on backup-host");
                Ui.Pump();
                var dialog = main.OwnedWindows.OfType<RestoreTargetDialog>().Single();
                await dismiss(dialog);
                Ui.Pump();
                return await task;
            }

            Assert.Equal(RestoreTargetChoice.Archived, await After(d =>
            {
                d.Press(PhysicalKey.Enter);
                return Task.CompletedTask;
            }));
            Assert.Equal(RestoreTargetChoice.Current, await After(d =>
                d.ButtonFor(((RestoreTargetViewModel)d.DataContext!).ChooseCurrentCommand).ClickAsync()));
            Assert.Equal(RestoreTargetChoice.Cancel, await After(d =>
            {
                d.Press(PhysicalKey.Escape);
                return Task.CompletedTask;
            }));
            Assert.Equal(RestoreTargetChoice.Cancel, await After(d =>
            {
                d.Close();
                return Task.CompletedTask;
            }));
            main.Close();
        });
    }

    [Fact]
    public async Task DuplicateResolutionDialog_FiveChoicesByClick_EscAndXSkip_EnterPicksNothing()
    {
        await RunUi(async () =>
        {
            using var host = TestHost.Create();
            var (main, windowService) = await ShowMainWindowAsync(host);

            async Task<ImportDuplicateResolution> After(Func<DuplicateResolutionDialog, Task> dismiss)
            {
                var task = windowService.ShowDuplicateResolutionAsync("Duplicate ISBN", "A book with this ISBN already exists.");
                Ui.Pump();
                var dialog = main.OwnedWindows.OfType<DuplicateResolutionDialog>().Single();
                await dismiss(dialog);
                Ui.Pump();
                return await task;
            }

            Task Click(DuplicateResolutionDialog d, Func<DuplicateResolutionViewModel, System.Windows.Input.ICommand> command) =>
                d.ButtonFor(command((DuplicateResolutionViewModel)d.DataContext!)).ClickAsync();

            Assert.Equal(ImportDuplicateResolution.Overwrite, await After(d => Click(d, vm => vm.OverwriteCommand)));
            Assert.Equal(ImportDuplicateResolution.OverwriteAll, await After(d => Click(d, vm => vm.OverwriteAllCommand)));
            Assert.Equal(ImportDuplicateResolution.Skip, await After(d => Click(d, vm => vm.SkipCommand)));
            Assert.Equal(ImportDuplicateResolution.SkipAll, await After(d => Click(d, vm => vm.SkipAllCommand)));
            Assert.Equal(ImportDuplicateResolution.CancelImport, await After(d => Click(d, vm => vm.CancelImportCommand)));

            // Every choice writes or skips data, so Enter deliberately picks nothing; the dialog stays open.
            Assert.Equal(ImportDuplicateResolution.Skip, await After(d =>
            {
                d.Press(PhysicalKey.Enter);
                Assert.Single(main.OwnedWindows.OfType<DuplicateResolutionDialog>());
                d.Press(PhysicalKey.Escape);
                return Task.CompletedTask;
            }));
            Assert.Equal(ImportDuplicateResolution.Skip, await After(d =>
            {
                d.Close();
                return Task.CompletedTask;
            }));
            main.Close();
        });
    }

    [Fact]
    public async Task IsbnPromptDialog_EnterLooksUpTheTypedIsbn_EscAndXDismissWithNull()
    {
        await RunUi(async () =>
        {
            using var host = TestHost.Create();
            var (main, windowService) = await ShowMainWindowAsync(host);

            // Enter triggers Look up — deliberate v2.3 fix (the old dialog had no default button).
            // The body must name the book being re-cataloged: in a bulk run the prompts repeat and
            // the title is the only thing telling them apart.
            var typed = windowService.ShowIsbnPromptDialogAsync("Nameless Tome");
            Ui.Pump();
            var dialog = main.OwnedWindows.OfType<IsbnPromptDialog>().Single();
            var expectedBody = string.Format(
                BookDB.Desktop.Localization.Resources.Recatalog_NoIsbn_BodyForBook, "Nameless Tome");
            Assert.Contains(dialog.Descendants<TextBlock>(), t => t.Text == expectedBody);
            dialog.TypeInto(dialog.Find<TextBox>(), "9781234567897");
            dialog.Press(PhysicalKey.Enter);
            Assert.Equal("9781234567897", await typed);

            var esc = windowService.ShowIsbnPromptDialogAsync("Nameless Tome");
            Ui.Pump();
            main.OwnedWindows.OfType<IsbnPromptDialog>().Single().Press(PhysicalKey.Escape);
            Assert.Null(await esc);

            var closed = windowService.ShowIsbnPromptDialogAsync("Nameless Tome");
            Ui.Pump();
            main.OwnedWindows.OfType<IsbnPromptDialog>().Single().Close();
            Ui.Pump();
            Assert.Null(await closed);
            main.Close();
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

    /// <summary>Shows the app's real MainWindow so WindowService-run dialogs have their production owner,
    /// and returns it with the real IWindowService.</summary>
    private static async Task<(MainWindow Main, IWindowService WindowService)> ShowMainWindowAsync(TestHost host)
    {
        var main = host.Resolve<MainWindow>();
        await ((MainWindowViewModel)main.DataContext!).InitializeAsync();
        main.Show();
        Ui.Pump();
        return (main, host.Resolve<IWindowService>());
    }

    /// <summary>Opens a component-backed dialog through its wrapper, dismisses it with the given gesture,
    /// and returns the wrapper's result.</summary>
    private static async Task<T> WrapperResultAfter<T>(
        MainWindow main, Func<Task<T>> open, Action<MessageDialog> dismiss)
    {
        var task = open();
        Ui.Pump();
        var dialog = main.OwnedWindows.OfType<MessageDialog>().Single();
        dismiss(dialog);
        Ui.Pump();
        return await task;
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
