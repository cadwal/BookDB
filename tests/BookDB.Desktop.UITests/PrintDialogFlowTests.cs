using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using BookDB.Data.Interfaces;
using BookDB.Desktop.Localization;
using BookDB.Desktop.ViewModels;
using BookDB.Desktop.Views;
using BookDB.Logic.Services;
using CommunityToolkit.Mvvm.Input;
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
