using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using BookDB.Desktop.ViewModels;
using BookDB.Desktop.Views;
using Xunit;

namespace BookDB.Desktop.UITests;

/// <summary>
/// The Companion tab's three numeric fields, driven through the control rather than the view model. Raised in
/// UAT-10: clearing the port threw, and the spinner stepped from somewhere other than the number in the box.
/// Two faults in one control, both from letting <c>NumericUpDown</c> judge the value:
/// <list type="bullet">
/// <item>a blank box is <c>Value = null</c>, and binding that into a non-nullable <c>int</c> threw;</item>
/// <item>a number outside <c>Minimum</c>/<c>Maximum</c> was <em>silently discarded</em> — <c>ConvertTextToValue</c>
/// runs <c>ValidateMinMax</c>, which throws, and <c>SyncTextAndValueProperties</c> swallows it as
/// "not parsable". The box went on showing what was typed while the view model kept the old number, so the
/// spinner stepped from the stale one and Save wrote it.</item>
/// </list>
/// The bounds are gone from the control for that reason: <see cref="SettingsCompanionTabViewModel.ValidateForSave"/>
/// is the only judge, and it refuses out loud. None of this is reachable from a test that sets the view-model
/// property directly, which is why the four indicator tests behind UAT-10 missed it.
/// </summary>
public class CompanionPortFieldTests : HeadlessTest
{
    private const int CompanionTabIndex = 7;

    /// <summary>Clearing a field is the ordinary first move when typing a new number over an old one.</summary>
    [Fact]
    public Task ClearingThePort_EmptiesTheFieldRatherThanThrowing() => RunUi(async () =>
    {
        var (window, vm) = await ShowCompanionTab();

        Clear(window, window.Find<NumericUpDown>("CompanionPortField"));

        Assert.Null(vm.CompanionTab.Port);
        window.Close();
    });

    /// <summary>The other two fields are the same control bound the same way, one row below the port.</summary>
    [Fact]
    public Task ClearingTheCaptureFields_EmptiesThemRatherThanThrowing() => RunUi(async () =>
    {
        var (window, vm) = await ShowCompanionTab();

        Clear(window, window.Find<NumericUpDown>("CompanionMaxImageSizeField"));
        Clear(window, window.Find<NumericUpDown>("CompanionJpegQualityField"));

        Assert.Null(vm.CompanionTab.MaxLongEdgePx);
        Assert.Null(vm.CompanionTab.JpegQuality);
        window.Close();
    });

    /// <summary>
    /// The spin has to start from what is in the box. 80 is the case that exposed it: under the old
    /// <c>Minimum="1025"</c> the typed 80 never reached the value at all, so the field read 80 while the
    /// view model still held 7443 and one press of ↑ produced 7444.
    /// </summary>
    [Fact]
    public Task TheSpinner_StepsFromTheNumberThatWasTyped() => RunUi(async () =>
    {
        var (window, vm) = await ShowCompanionTab();
        var field = window.Find<NumericUpDown>("CompanionPortField");

        Retype(window, field, "80");
        Assert.Equal(80, vm.CompanionTab.Port);

        Spin(field, SpinDirection.Increase);
        Assert.Equal(81, vm.CompanionTab.Port);

        Spin(field, SpinDirection.Decrease);
        Assert.Equal(80, vm.CompanionTab.Port);

        window.Close();
    });

    /// <summary>
    /// Out of range is refused, not clamped. Save used to <c>Math.Clamp</c> the port, which hands back one the
    /// user never chose — the phone then cannot reach a door they believe they opened.
    /// </summary>
    [Fact]
    public Task APortOutsideTheRange_IsRefusedAndSaysSo() => RunUi(async () =>
    {
        var (window, vm) = await ShowCompanionTab();
        var tab = vm.CompanionTab;

        Retype(window, window.Find<NumericUpDown>("CompanionPortField"), "80");

        Assert.False(tab.ValidateForSave());
        Assert.Equal(SettingsCompanionTabViewModel.PortInvalidMessage, tab.ValidationError);
        Assert.Equal(80, tab.Port); // refused, not rewritten

        window.Close();
    });

    /// <summary>
    /// Both ends of the swallowing fault. 80 is a port someone might plausibly try; 70000 is the same defect
    /// above <c>Maximum</c>, which is why neither bound is left on the control.
    /// </summary>
    [Theory]
    [InlineData("80")]     // below the old Minimum
    [InlineData("70000")]  // above the old Maximum
    public Task AnOutOfRangePort_ReachesTheViewModelRatherThanBeingSwallowed(string typed) => RunUi(async () =>
    {
        var (window, vm) = await ShowCompanionTab();

        Retype(window, window.Find<NumericUpDown>("CompanionPortField"), typed);

        Assert.Equal(int.Parse(typed, CultureInfo.InvariantCulture), vm.CompanionTab.Port);
        Assert.False(vm.CompanionTab.ValidateForSave());

        window.Close();
    });

    /// <summary>A port inside the range passes and leaves no stale message behind.</summary>
    [Fact]
    public Task APortInsideTheRange_PassesAndClearsAnEarlierRefusal() => RunUi(async () =>
    {
        var (window, vm) = await ShowCompanionTab();
        var tab = vm.CompanionTab;
        var field = window.Find<NumericUpDown>("CompanionPortField");

        Retype(window, field, "80");
        Assert.False(tab.ValidateForSave());

        Retype(window, field, "8443");
        Assert.True(tab.ValidateForSave());
        Assert.Null(tab.ValidationError);

        window.Close();
    });

    /// <summary>
    /// A blank field means "no change": Save keeps the value in effect and writes it back into the box, so the
    /// refill is the feedback. Only the port — which the host must actually bind — refuses out of range.
    /// </summary>
    [Fact]
    public Task ABlankFieldOnSave_KeepsTheValueThatWasInEffect() => RunUi(async () =>
    {
        var (window, vm) = await ShowCompanionTab();
        var tab = vm.CompanionTab;
        var port = tab.Port;

        Clear(window, window.Find<NumericUpDown>("CompanionPortField"));
        Assert.Null(tab.Port);

        Assert.True(tab.ValidateForSave());
        await tab.SaveAsync();

        Assert.Equal(port, tab.Port);
        window.Close();
    });

    /// <summary>The refusal has to be visible: Save stops on the Companion tab instead of closing the dialog.</summary>
    [Fact]
    public Task SavingWithABadPort_StaysOpenOnTheCompanionTab() => RunUi(async () =>
    {
        var (window, vm) = await ShowCompanionTab();
        var closed = false;
        vm.CloseDialog = _ => closed = true;

        Retype(window, window.Find<NumericUpDown>("CompanionPortField"), "80");

        // Save from another tab: the refusal has to bring the user back to the field it is about.
        vm.SelectedTabIndex = 0;
        Ui.Pump();

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.False(closed);
        Assert.Equal(CompanionTabIndex, vm.SelectedTabIndex);
        Assert.Equal(SettingsCompanionTabViewModel.PortInvalidMessage, vm.CompanionTab.ValidationError);

        window.Close();
    });

    /// <summary>The message is on screen, not just on the view model.</summary>
    [Fact]
    public Task TheRefusal_IsRendered() => RunUi(async () =>
    {
        var (window, vm) = await ShowCompanionTab();

        Assert.DoesNotContain(VisibleTexts(window), t => t == SettingsCompanionTabViewModel.PortInvalidMessage);

        Retype(window, window.Find<NumericUpDown>("CompanionPortField"), "80");
        vm.CompanionTab.ValidateForSave();
        Ui.Pump();

        Assert.Contains(VisibleTexts(window), t => t == SettingsCompanionTabViewModel.PortInvalidMessage);

        window.Close();
    });

    private static async Task<(Window Window, SettingsWindowViewModel Vm)> ShowCompanionTab()
    {
        using var host = TestHost.Create();
        var vm = host.Resolve<SettingsWindowViewModel>();
        await vm.InitializeAsync();
        var window = new SettingsWindow { DataContext = vm };
        window.Show();
        vm.SelectedTabIndex = CompanionTabIndex;
        Ui.Pump();
        return (window, vm);
    }

    /// <summary>Select-all then Backspace — how a field is emptied at the keyboard.</summary>
    private static void Clear(Window window, NumericUpDown field)
    {
        var box = field.Find<TextBox>();
        box.Focus();
        Ui.Pump();
        box.SelectAll();
        window.Press(PhysicalKey.Backspace);
    }

    private static void Retype(Window window, NumericUpDown field, string text) =>
        window.RetypeInto(field.Find<TextBox>(), text);

    private static void Spin(NumericUpDown field, SpinDirection direction)
    {
        field.Find<ButtonSpinner>().RaiseEvent(new SpinEventArgs(Spinner.SpinEvent, direction));
        Ui.Pump();
    }

    private static string[] VisibleTexts(Window window) =>
        window.Descendants<TextBlock>()
            .Where(t => t.IsEffectivelyVisible)
            .Select(t => t.Text ?? string.Empty)
            .ToArray();
}
