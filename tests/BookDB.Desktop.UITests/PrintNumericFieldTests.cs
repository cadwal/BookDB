using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using BookDB.Desktop.ViewModels;
using BookDB.Desktop.Views;
using BookDB.Logic.Services;
using Xunit;

namespace BookDB.Desktop.UITests;

/// <summary>
/// The print dialog's font size and two margins, driven through the control. Same defect the Companion tab's
/// port field had, found by looking for it rather than by hitting it: <c>NumericUpDown.Value</c> is
/// <c>decimal?</c>, so an emptied box threw on the way into a non-nullable <c>decimal</c>; and a number outside
/// the control's <c>Minimum</c>/<c>Maximum</c> is not clamped but <em>discarded</em> — <c>ValidateMinMax</c>
/// throws and <c>SyncTextAndValueProperties</c> swallows it — leaving the box showing one figure while the view
/// model held another, and the page printed with the one you could not see.
/// The bounds now live in the view model and are applied in <c>BuildCurrentPreset</c>.
/// </summary>
public class PrintNumericFieldTests : HeadlessTest
{
    [Theory]
    [InlineData("PrintFontSizeField")]
    [InlineData("PrintMarginHorizontalField")]
    [InlineData("PrintMarginVerticalField")]
    public Task ClearingAField_EmptiesItRatherThanThrowing(string fieldName) => RunUi(async () =>
    {
        var (vm, dialog) = await OpenAsync();

        var field = dialog.Find<NumericUpDown>(fieldName);
        var box = field.Find<TextBox>();
        box.Focus();
        Ui.Pump();
        box.SelectAll();
        dialog.Press(PhysicalKey.Backspace);

        Assert.Null(field.Value);
        dialog.Close();
    });

    /// <summary>
    /// 3 pt is below the old <c>Minimum="7"</c>. It has to reach the view model — where it is clamped and
    /// written back — rather than being dropped while the box goes on reading 3.
    /// </summary>
    [Fact]
    public Task AFontSizeBelowTheBound_ReachesTheViewModelAndIsSettledOnUse() => RunUi(async () =>
    {
        var (vm, dialog) = await OpenAsync();

        dialog.RetypeInto(dialog.Find<NumericUpDown>("PrintFontSizeField").Find<TextBox>(), "3");
        Assert.Equal(3m, vm.FontSize);

        var preset = await SettleViaPreset(vm);

        Assert.Equal(7f, preset.FontSize);
        Assert.Equal(7m, vm.FontSize); // written back, so the box shows what will print
        dialog.Close();
    });

    /// <summary>The same at the top end, which the old <c>Maximum</c> swallowed just as quietly.</summary>
    [Fact]
    public Task AMarginAboveTheBound_ReachesTheViewModelAndIsSettledOnUse() => RunUi(async () =>
    {
        var (vm, dialog) = await OpenAsync();

        dialog.RetypeInto(dialog.Find<NumericUpDown>("PrintMarginHorizontalField").Find<TextBox>(), "90");
        Assert.Equal(90m, vm.MarginHorizontalMm);

        var preset = await SettleViaPreset(vm);

        Assert.Equal(40f, preset.MarginHorizontalMm);
        Assert.Equal(40m, vm.MarginHorizontalMm);
        dialog.Close();
    });

    /// <summary>An emptied field prints the default rather than taking the dialog down with it.</summary>
    [Fact]
    public Task AnEmptiedField_PrintsTheDefault() => RunUi(async () =>
    {
        var (vm, dialog) = await OpenAsync();

        var box = dialog.Find<NumericUpDown>("PrintMarginVerticalField").Find<TextBox>();
        box.Focus();
        Ui.Pump();
        box.SelectAll();
        dialog.Press(PhysicalKey.Backspace);
        Assert.Null(vm.MarginVerticalMm);

        Assert.Equal(20f, (await SettleViaPreset(vm)).MarginVerticalMm);
        dialog.Close();
    });

    /// <summary>Saving a preset is the reachable path through <c>BuildCurrentPreset</c>, where the settling happens.</summary>
    private static async Task<PrintPreset> SettleViaPreset(PrintDialogViewModel vm)
    {
        vm.NewPresetCommand.Execute(null);
        vm.PresetNameEditText = "Settled";
        await vm.ConfirmPresetNameCommand.ExecuteAsync(null);
        Ui.Pump();
        return vm.Presets.Single(p => p.Name == "Settled");
    }

    private static async Task<(PrintDialogViewModel Vm, PrintDialog Dialog)> OpenAsync()
    {
        using var host = TestHost.Create();
        var vm = host.Resolve<PrintDialogViewModel>();
        await vm.InitializeAsync(null, null, null, null, sortAscending: true, bookCount: 3,
            TestContext.Current.CancellationToken);
        var dialog = new PrintDialog { DataContext = vm };
        dialog.Show();
        Ui.Pump();
        return (vm, dialog);
    }
}
