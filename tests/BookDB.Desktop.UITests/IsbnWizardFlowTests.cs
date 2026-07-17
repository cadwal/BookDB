using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using BookDB.Desktop.Localization;
using BookDB.Desktop.Services;
using BookDB.Desktop.ViewModels;
using BookDB.Desktop.Views;
using BookDB.Models.Interfaces;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace BookDB.Desktop.UITests;

/// <summary>
/// Catalog-by-ISBN wizard journeys, short of the network lookup itself (starting a lookup hands the parsed list
/// to the batch queue). ISBNs typed one per line keep a live count; a browsed or dropped .txt/.csv file fills the
/// box (a digit-less CSV header row is skipped); starting with an empty box shows the inline error instead of
/// launching anything; and manual entry closes the wizard pre-filling the first ISBN.
/// </summary>
public class IsbnWizardFlowTests : HeadlessTest
{
    [Fact]
    public async Task TypedIsbns_OnePerLine_KeepTheCountLive_AndStartHandsThemToTheBatch()
    {
        var windowService = Substitute.For<IWindowService>();

        await RunUi(async () =>
        {
            using var host = TestHost.Create(s => s.AddSingleton(windowService));
            var (vm, dialog, result) = Open(host);

            // No error is shown while the box is untouched, and the count starts at zero.
            Assert.Equal(0, vm.IsbnCount);
            Assert.DoesNotContain(dialog.Descendants<TextBlock>(),
                t => t.IsEffectivelyVisible && t.Text == Resources.LookupWizard_Error_EnterIsbn);
            Assert.Contains(dialog.Descendants<TextBlock>(), t =>
                t.IsEffectivelyVisible && t.Text == string.Format(Resources.LookupWizard_IsbnCount, 0));

            // Two ISBNs typed as real input, one per line — the count follows each line.
            var box = dialog.Find<TextBox>("IsbnTextBox");
            dialog.TypeInto(box, "9780441013593");
            Assert.Equal(1, vm.IsbnCount);
            dialog.Press(PhysicalKey.Enter);
            dialog.KeyTextInput("9780141439587");
            Ui.Pump();
            Assert.Equal(2, vm.IsbnCount);
            Assert.Contains(dialog.Descendants<TextBlock>(), t =>
                t.IsEffectivelyVisible && t.Text == string.Format(Resources.LookupWizard_IsbnCount, 2));

            // Start closes the wizard and hands exactly the parsed list to the batch queue.
            await Ui.ClickAsync(dialog.ButtonFor(vm.StartLookupCommand));
            Assert.True(result());
            await windowService.Received(1).StartBatchAsync(
                Arg.Is<System.Collections.Generic.IReadOnlyList<string>>(l =>
                    l != null && l.Count == 2 && l[0] == "9780441013593" && l[1] == "9780141439587"));
            dialog.Close();
        });
    }

    [Fact]
    public async Task StartingWithNoIsbns_ShowsTheInlineError_InsteadOfLaunching()
    {
        var windowService = Substitute.For<IWindowService>();

        await RunUi(async () =>
        {
            using var host = TestHost.Create(s => s.AddSingleton(windowService));
            var (vm, dialog, result) = Open(host);

            await Ui.ClickAsync(dialog.ButtonFor(vm.StartLookupCommand));
            Assert.Contains(dialog.Descendants<TextBlock>(),
                t => t.IsEffectivelyVisible && t.Text == Resources.LookupWizard_Error_EnterIsbn);
            Assert.Null(result());
            await windowService.DidNotReceiveWithAnyArgs().StartBatchAsync(default!);

            // Cancel closes without a result.
            await Ui.ClickAsync(dialog.ButtonFor(vm.CancelCommand));
            Assert.False(result());
            dialog.Close();
        });
    }

    [Fact]
    public async Task BrowsedFile_FillsTheBox_AndACsvHeaderRowIsSkipped()
    {
        var picker = Substitute.For<IFilePickerService>();

        await RunUi(async () =>
        {
            using var host = TestHost.Create(s => s.AddSingleton(picker));
            var (vm, dialog, _) = Open(host);

            // A plain text file: every non-empty line counts, whitespace trimmed.
            var txtPath = await WriteTempFileAsync(".txt", "9780441013593\r\n\r\n  9780141439587  \r\n");
            var csvPath = await WriteTempFileAsync(".csv", "ISBN\r\n9780441013593\r\n9780141439587\r\n");
            try
            {
                picker.PickFileAsync(Arg.Any<string>(), Arg.Any<System.Collections.Generic.IReadOnlyList<string>>())
                    .Returns(txtPath);
                await Ui.ClickAsync(dialog.ButtonFor(vm.BrowseFileCommand));
                Assert.Equal("9780441013593\n9780141439587", vm.IsbnText);
                Assert.Equal(2, vm.IsbnCount);

                // A CSV whose first line has no digits is treated as a header and dropped.
                picker.PickFileAsync(Arg.Any<string>(), Arg.Any<System.Collections.Generic.IReadOnlyList<string>>())
                    .Returns(csvPath);
                await Ui.ClickAsync(dialog.ButtonFor(vm.BrowseFileCommand));
                Assert.Equal("9780441013593\n9780141439587", vm.IsbnText);
                Assert.Equal(2, vm.IsbnCount);
            }
            finally
            {
                File.Delete(txtPath);
                File.Delete(csvPath);
            }
            dialog.Close();
        });
    }

    [Fact]
    public async Task DroppedFile_FillsTheBox_ThroughTheDropCommand()
    {
        await RunUi(async () =>
        {
            using var host = TestHost.Create();
            var (vm, dialog, _) = Open(host);

            var path = await WriteTempFileAsync(".txt", "9780441013593\r\n9780141439587\r\n9780553293357\r\n");
            try
            {
                await ((IAsyncRelayCommand)vm.HandleFileDropCommand).ExecuteAsync(path);
                Ui.Pump();
                Assert.Equal(3, vm.IsbnCount);
                Assert.Contains(dialog.Descendants<TextBlock>(), t =>
                    t.IsEffectivelyVisible && t.Text == string.Format(Resources.LookupWizard_IsbnCount, 3));
            }
            finally { File.Delete(path); }
            dialog.Close();
        });
    }

    [Fact]
    public async Task ManualEntry_ClosesTheWizard_PrefillingTheFirstIsbn()
    {
        var windowService = Substitute.For<IWindowService>();

        await RunUi(async () =>
        {
            using var host = TestHost.Create(s => s.AddSingleton(windowService));
            var (vm, dialog, result) = Open(host);

            // The manual-entry button lives in the error panel, so surface it first.
            await Ui.ClickAsync(dialog.ButtonFor(vm.StartLookupCommand));
            dialog.TypeInto(dialog.Find<TextBox>("IsbnTextBox"), "978-0-441-01359-3");
            await Ui.ClickAsync(dialog.ButtonFor(vm.ManualEntryCommand));

            Assert.False(result());
            await windowService.Received(1).ShowAddBookDialogAsync(null, "9780441013593");
            dialog.Close();
        });
    }

    // ─── Plumbing ────────────────────────────────────────────────────────────

    /// <summary>Opens the dialog the way <c>WindowService.ShowLookupWizardDialogAsync</c> composes it.</summary>
    private static (LookupWizardViewModel Vm, LookupWizardDialog Dialog, Func<bool?> Result) Open(TestHost host)
    {
        var vm = host.Resolve<LookupWizardViewModel>();
        bool? result = null;
        vm.CloseDialog = r => result = r;
        var dialog = new LookupWizardDialog { DataContext = vm };
        dialog.Show();
        Ui.Pump();
        return (vm, dialog, () => result);
    }

    private static async Task<string> WriteTempFileAsync(string extension, string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"bookdb_isbn_{Guid.NewGuid():N}{extension}");
        await File.WriteAllTextAsync(path, content);
        return path;
    }

}
