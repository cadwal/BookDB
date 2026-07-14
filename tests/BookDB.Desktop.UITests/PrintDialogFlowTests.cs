using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using BookDB.Data.Interfaces;
using BookDB.Desktop.Localization;
using BookDB.Desktop.ViewModels;
using BookDB.Desktop.Views;
using BookDB.Logic.Services;
using BookDB.Models;
using BookDB.Models.Interfaces;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BookDB.Desktop.UITests;

/// <summary>
/// Print dialog journeys, short of generating a PDF (the print service owns that). The dialog always carries the
/// undeletable Standard preset; a new preset is named inline with validation (empty and duplicate names both show
/// an error that clears again), renaming pre-fills the row, and deleting asks for inline confirmation first.
/// Presets persist as settings JSON and switching presets applies their stored form values. Preview and Save as
/// PDF stay gated while the current scope holds no books.
/// </summary>
public class PrintDialogFlowTests : HeadlessTest
{
    [Fact]
    public async Task StandardPreset_IsProtected_AndPrintingIsGatedOnBooks()
    {
        var ct = TestContext.Current.CancellationToken;

        await RunUi(async () =>
        {
            using var host = TestHost.Create();
            var (vm, dialog) = await OpenAsync(host, bookCount: 0, ct);

            // The Standard preset is selected under its localized display name and can't be renamed or deleted.
            Assert.Equal(PrintPreset.StandardPresetName, vm.SelectedPreset!.Name);
            Assert.Equal(Resources.Print_StandardPresetDisplayName, vm.SelectedPresetDisplayName);
            Assert.False(dialog.ButtonFor(vm.RenamePresetCommand).IsEffectivelyEnabled);
            Assert.False(dialog.ButtonFor(vm.DeletePresetCommand).IsEffectivelyEnabled);

            // An empty scope keeps both output paths gated, and the summary says there is nothing to print.
            Assert.False(dialog.ButtonFor(vm.PreviewCommand).IsEffectivelyEnabled);
            Assert.False(dialog.ButtonFor(vm.SaveAsPdfCommand).IsEffectivelyEnabled);
            Assert.Contains(dialog.Descendants<TextBlock>(),
                t => t.IsEffectivelyVisible && t.Text == Resources.Print_ScopeSummary_None);

            // The Standard preset's stored columns are ticked; select/clear-all drive every checkbox.
            Assert.Equal(PrintPreset.CreateDefault().Columns.OrderBy(c => c),
                vm.Columns.Where(c => c.IsSelected).Select(c => c.Key).OrderBy(c => c));
            await Ui.ClickAsync(dialog.ButtonFor(vm.SelectAllColumnsCommand));
            Assert.All(vm.Columns, c => Assert.True(c.IsSelected));
            var checkBoxes = dialog.Descendants<CheckBox>();
            Assert.Equal(vm.Columns.Count, checkBoxes.Count(c => c.IsChecked == true));
            await Ui.ClickAsync(dialog.ButtonFor(vm.ClearAllColumnsCommand));
            Assert.All(vm.Columns, c => Assert.False(c.IsSelected));
            Assert.Equal(0, checkBoxes.Count(c => c.IsChecked == true));
            dialog.Close();
        });
    }

    [Fact]
    public async Task ScopeWithBooks_EnablesPreviewAndPdf()
    {
        var ct = TestContext.Current.CancellationToken;

        await RunUi(async () =>
        {
            using var host = TestHost.Create();
            var (vm, dialog) = await OpenAsync(host, bookCount: 2, ct);

            Assert.True(dialog.ButtonFor(vm.PreviewCommand).IsEffectivelyEnabled);
            Assert.True(dialog.ButtonFor(vm.SaveAsPdfCommand).IsEffectivelyEnabled);
            Assert.Contains(dialog.Descendants<TextBlock>(),
                t => t.IsEffectivelyVisible && t.Text == string.Format(Resources.Print_ScopeSummary_Multiple, 2));
            dialog.Close();
        });
    }

    [Fact]
    public async Task Generating_ShowsLocalizedStatus_AndGatesBothOutputButtons()
    {
        var ct = TestContext.Current.CancellationToken;

        await RunUi(async () =>
        {
            // The fake blocks mid-generation after reporting its first step, so the in-flight dialog state is
            // observable; RunContinuationsAsynchronously keeps SetResult from running the command continuation
            // inline under the gate.
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var host = TestHost.Create(services =>
            {
                services.AddSingleton<IPrintService>(new GateablePrintService(gate));
                services.AddSingleton<IFilePickerService>(new FixedSaveFilePicker(
                    Path.Combine(Path.GetTempPath(), "bookdb-print-status-test.pdf")));
            });
            var (vm, dialog) = await OpenAsync(host, bookCount: 2, ct);

            var run = vm.SaveAsPdfCommand.ExecuteAsync(null);
            Ui.Pump();

            // The status line actually renders the localized step, and both output commands are gated meanwhile.
            Assert.Contains(dialog.Descendants<TextBlock>(),
                t => t.IsEffectivelyVisible && t.Text == Resources.Print_Status_Querying);
            Assert.False(dialog.ButtonFor(vm.PreviewCommand).IsEffectivelyEnabled);
            Assert.False(dialog.ButtonFor(vm.SaveAsPdfCommand).IsEffectivelyEnabled);

            gate.SetResult();
            await run;
            Ui.Pump();

            // Generation over: status line gone, output commands live again.
            Assert.DoesNotContain(dialog.Descendants<TextBlock>(),
                t => t.IsEffectivelyVisible && t.Text == Resources.Print_Status_Querying);
            Assert.True(dialog.ButtonFor(vm.PreviewCommand).IsEffectivelyEnabled);
            Assert.True(dialog.ButtonFor(vm.SaveAsPdfCommand).IsEffectivelyEnabled);
            dialog.Close();
        });
    }

    private sealed class GateablePrintService : IPrintService
    {
        private readonly TaskCompletionSource _gate;
        public GateablePrintService(TaskCompletionSource gate) => _gate = gate;
        public IReadOnlyList<string> AllColumnNames { get; } = ["Title"];
        public IReadOnlyList<string> DefaultColumnNames { get; } = ["Title"];
        public void InitializeLicense() { }

        public async Task GenerateAsync(
            PrintParameters parameters,
            CancellationToken ct = default,
            IProgress<ProgressUpdate<PrintProgressStep>>? progress = null)
        {
            progress?.Report(new ProgressUpdate<PrintProgressStep>(PrintProgressStep.Querying));
            await _gate.Task;
        }
    }

    private sealed class FixedSaveFilePicker : IFilePickerService
    {
        private readonly string _path;
        public FixedSaveFilePicker(string path) => _path = path;
        public Task<string?> PickFileAsync(string title, IReadOnlyList<string> extensions) => Task.FromResult<string?>(null);
        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);
        public Task<string?> SaveFileAsync(string title, string suggestedName, IReadOnlyList<string> extensions) => Task.FromResult<string?>(_path);
    }

    [Fact]
    public async Task NamingAPreset_ValidatesEmptyAndDuplicate_ThenPersists()
    {
        var ct = TestContext.Current.CancellationToken;

        await RunUi(async () =>
        {
            using var host = TestHost.Create();
            var (vm, dialog) = await OpenAsync(host, bookCount: 0, ct);

            // New preset switches the preset row for the inline naming row.
            await Ui.ClickAsync(dialog.ButtonFor(vm.NewPresetCommand));
            Assert.False(dialog.ButtonFor(vm.NewPresetCommand).IsEffectivelyVisible);
            var namingBox = NamingBox(dialog, vm);

            // Saving with no name shows the empty-name error inline.
            await Ui.ClickAsync(dialog.ButtonFor(vm.ConfirmPresetNameCommand));
            Assert.Contains(dialog.Descendants<TextBlock>(),
                t => t.IsEffectivelyVisible && t.Text == Resources.Print_Error_PresetNameEmpty);

            // A name that only differs by case from an existing preset is a duplicate.
            dialog.TypeInto(namingBox, "standard");
            await Ui.ClickAsync(dialog.ButtonFor(vm.ConfirmPresetNameCommand));
            Assert.Contains(dialog.Descendants<TextBlock>(),
                t => t.IsEffectivelyVisible && t.Text == Resources.Print_Error_PresetNameDuplicate);

            // A fresh name saves: the error clears, the naming row yields to the preset row, and the new
            // preset is selected and persisted.
            Ui.RetypeInto(dialog, namingBox, "Shelf List");
            await Ui.ClickAsync(dialog.ButtonFor(vm.ConfirmPresetNameCommand));
            Assert.DoesNotContain(dialog.Descendants<TextBlock>(), t =>
                t.IsEffectivelyVisible &&
                (t.Text == Resources.Print_Error_PresetNameEmpty || t.Text == Resources.Print_Error_PresetNameDuplicate));
            Assert.True(dialog.ButtonFor(vm.NewPresetCommand).IsEffectivelyVisible);
            Assert.Equal("Shelf List", vm.SelectedPreset!.Name);
            Assert.Contains("Shelf List", await PersistedPresetNames(host, ct));

            // Cancelling a naming round leaves the presets untouched.
            await Ui.ClickAsync(dialog.ButtonFor(vm.NewPresetCommand));
            dialog.TypeInto(NamingBox(dialog, vm), "Never Saved");
            await Ui.ClickAsync(dialog.ButtonFor(vm.CancelPresetNameCommand));
            Assert.DoesNotContain(vm.Presets, p => p.Name == "Never Saved");
            dialog.Close();
        });
    }

    [Fact]
    public async Task RenameAndDelete_RoundTripThroughConfirmation_AndPresetsApplyOnSelection()
    {
        var ct = TestContext.Current.CancellationToken;

        await RunUi(async () =>
        {
            using var host = TestHost.Create();
            var (vm, dialog) = await OpenAsync(host, bookCount: 0, ct);

            // Create a preset that captures a landscape form with every column ticked.
            var landscapeRadio = dialog.Descendants<RadioButton>()
                .Single(r => Equals(r.Content, Resources.Print_OrientationLandscape));
            landscapeRadio.IsChecked = true;
            Ui.Pump();
            Assert.True(vm.IsLandscape);
            await Ui.ClickAsync(dialog.ButtonFor(vm.SelectAllColumnsCommand));
            await Ui.ClickAsync(dialog.ButtonFor(vm.NewPresetCommand));
            dialog.TypeInto(NamingBox(dialog, vm), "Wide List");
            await Ui.ClickAsync(dialog.ButtonFor(vm.ConfirmPresetNameCommand));

            // Switching to Standard applies its stored portrait/column values; switching back restores the wide form.
            PresetCombo(dialog).SelectedItem = vm.Presets.Single(p => p.Name == PrintPreset.StandardPresetName);
            Ui.Pump();
            Assert.True(vm.IsPortrait);
            Assert.Equal(PrintPreset.CreateDefault().Columns.Count, vm.Columns.Count(c => c.IsSelected));
            PresetCombo(dialog).SelectedItem = vm.Presets.Single(p => p.Name == "Wide List");
            Ui.Pump();
            Assert.True(vm.IsLandscape);
            Assert.All(vm.Columns, c => Assert.True(c.IsSelected));

            // Rename pre-fills the naming row with the current name and updates in place.
            await Ui.ClickAsync(dialog.ButtonFor(vm.RenamePresetCommand));
            var namingBox = NamingBox(dialog, vm);
            Assert.Equal("Wide List", namingBox.Text);
            Ui.RetypeInto(dialog, namingBox, "Wide List 2");
            await Ui.ClickAsync(dialog.ButtonFor(vm.ConfirmPresetNameCommand));
            Assert.Equal("Wide List 2", vm.SelectedPreset!.Name);
            Assert.DoesNotContain(vm.Presets, p => p.Name == "Wide List");
            Assert.Contains("Wide List 2", await PersistedPresetNames(host, ct));

            // Delete asks inline first — cancelling keeps the preset.
            await Ui.ClickAsync(dialog.ButtonFor(vm.DeletePresetCommand));
            Assert.Contains(dialog.Descendants<TextBlock>(), t => t.IsEffectivelyVisible
                && t.Text == string.Format(Resources.Print_DeletePresetConfirm, "Wide List 2"));
            await Ui.ClickAsync(dialog.ButtonFor(vm.CancelDeleteCommand));
            Assert.Contains(vm.Presets, p => p.Name == "Wide List 2");

            // Confirming removes it, falls back to Standard, and the removal persists.
            await Ui.ClickAsync(dialog.ButtonFor(vm.DeletePresetCommand));
            await Ui.ClickAsync(dialog.ButtonFor(vm.ConfirmDeleteCommand));
            Assert.DoesNotContain(vm.Presets, p => p.Name == "Wide List 2");
            Assert.Equal(PrintPreset.StandardPresetName, vm.SelectedPreset!.Name);
            Assert.DoesNotContain("Wide List 2", await PersistedPresetNames(host, ct));
            dialog.Close();
        });
    }

    [Fact]
    public async Task Esc_RoutesToWhicheverCancelButtonIsVisible()
    {
        var ct = TestContext.Current.CancellationToken;

        await RunUi(async () =>
        {
            using var host = TestHost.Create();
            var (vm, dialog) = await OpenAsync(host, bookCount: 0, ct);

            // Naming row open: Esc cancels the naming round, not the whole dialog.
            await Ui.ClickAsync(dialog.ButtonFor(vm.NewPresetCommand));
            dialog.TypeInto(NamingBox(dialog, vm), "Escaped Preset");
            dialog.Press(PhysicalKey.Escape);
            Assert.False(vm.IsNamingMode);
            Assert.DoesNotContain(vm.Presets, p => p.Name == "Escaped Preset");

            // A non-Standard preset so DeletePresetCommand — and its confirm row — is reachable.
            await Ui.ClickAsync(dialog.ButtonFor(vm.NewPresetCommand));
            dialog.TypeInto(NamingBox(dialog, vm), "Deletable");
            await Ui.ClickAsync(dialog.ButtonFor(vm.ConfirmPresetNameCommand));

            // Delete-confirm row open: Esc cancels the confirmation, the preset survives.
            await Ui.ClickAsync(dialog.ButtonFor(vm.DeletePresetCommand));
            dialog.Press(PhysicalKey.Escape);
            Assert.False(vm.IsDeleteConfirmMode);
            Assert.Contains(vm.Presets, p => p.Name == "Deletable");

            // Neither row open: Esc reaches the main Cancel and closes the whole dialog with no result.
            PrintParameters? result = null;
            var closed = false;
            vm.CloseDialog = r => { result = r; closed = true; dialog.Close(); };
            dialog.Press(PhysicalKey.Escape);
            Assert.True(closed);
            Assert.Null(result);
        });
    }

    [Fact]
    public async Task EnterDefault_StaysDroppedWhileAPresetRowIsOpen()
    {
        var ct = TestContext.Current.CancellationToken;

        await RunUi(async () =>
        {
            using var host = TestHost.Create();
            var (vm, dialog) = await OpenAsync(host, bookCount: 2, ct);
            var closed = false;
            vm.CloseDialog = _ => closed = true;

            // Naming row open: Preview isn't the default button here, so Enter is a no-op.
            await Ui.ClickAsync(dialog.ButtonFor(vm.NewPresetCommand));
            dialog.Press(PhysicalKey.Enter);
            Assert.False(closed);
            Assert.True(vm.IsNamingMode);
            await Ui.ClickAsync(dialog.ButtonFor(vm.CancelPresetNameCommand));

            // A non-Standard preset so the delete-confirm row can open.
            await Ui.ClickAsync(dialog.ButtonFor(vm.NewPresetCommand));
            dialog.TypeInto(NamingBox(dialog, vm), "Deletable");
            await Ui.ClickAsync(dialog.ButtonFor(vm.ConfirmPresetNameCommand));

            // Delete-confirm row open: same gating — Enter still doesn't fall through to Preview.
            await Ui.ClickAsync(dialog.ButtonFor(vm.DeletePresetCommand));
            dialog.Press(PhysicalKey.Enter);
            Assert.False(closed);
            Assert.True(vm.IsDeleteConfirmMode);
            dialog.Close();
        });
    }

    // ─── Plumbing ────────────────────────────────────────────────────────────

    /// <summary>Opens the dialog the way <c>WindowService.ShowPrintDialogAsync</c> composes it.</summary>
    private static async Task<(PrintDialogViewModel Vm, PrintDialog Dialog)> OpenAsync(
        TestHost host, int bookCount, System.Threading.CancellationToken ct)
    {
        var vm = host.Resolve<PrintDialogViewModel>();
        await vm.InitializeAsync(null, null, null, null, sortAscending: true, bookCount, ct);
        var dialog = new PrintDialog { DataContext = vm };
        dialog.Show();
        Ui.Pump();
        return (vm, dialog);
    }

    private static async Task<string[]> PersistedPresetNames(TestHost host, System.Threading.CancellationToken ct)
    {
        var json = await host.Resolve<ISettingsService>().GetAsync("PrintPresets", ct);
        Assert.NotNull(json);
        return JsonSerializer.Deserialize<PrintPreset[]>(json!)!.Select(p => p.Name).ToArray();
    }

    /// <summary>The inline naming box — the TextBox sharing the naming row with its Save button.</summary>
    private static TextBox NamingBox(PrintDialog dialog, PrintDialogViewModel vm) =>
        ((Visual)dialog.ButtonFor(vm.ConfirmPresetNameCommand).GetVisualParent()!).Find<TextBox>();

    private static ComboBox PresetCombo(PrintDialog dialog) =>
        dialog.Descendants<ComboBox>().First(c => c.IsEffectivelyVisible);

}
