using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using BookDB.Desktop.Services;
using BookDB.Desktop.ViewModels;
using BookDB.Desktop.Views;
using Xunit;

namespace BookDB.Desktop.UITests;

/// <summary>
/// The shared message-dialog component: buttons render from the spec in order with their role
/// styling, and the keyboard semantics (Enter on the default, Esc on the cancel) come from the
/// spec alone — no per-dialog wiring.
/// </summary>
public class MessageDialogComponentTests : HeadlessTest
{
    private static MessageDialogSpec ThreeButtonSpec(bool withDefault = true) => new(
        "Component title",
        "Component body.",
        [
            new DialogButton("Proceed", "proceed", DialogButtonRole.Primary, IsDefault: withDefault),
            new DialogButton("Erase", "erase", DialogButtonRole.Danger),
            new DialogButton("Back", "back", IsCancel: true),
        ],
        SafeCloseResult: "back");

    private static (MessageDialog Dialog, System.Func<object?> Chosen) Open(MessageDialogSpec spec)
    {
        var vm = new MessageDialogViewModel(spec);
        object? chosen = "unset";
        vm.CloseDialog = r => chosen = r;
        var dialog = new MessageDialog { DataContext = vm };
        dialog.Show();
        Ui.Pump();
        return (dialog, () => chosen);
    }

    [Fact]
    public async Task ButtonsRenderInSpecOrder_WithRoleStyling()
    {
        await RunUi(() =>
        {
            var (dialog, _) = Open(ThreeButtonSpec());
            var buttons = dialog.Descendants<Button>();
            Assert.Equal(["Proceed", "Erase", "Back"], buttons.Select(b => b.Content?.ToString()));
            Assert.Contains("accent", buttons[0].Classes);
            Assert.Contains("dangerButton", buttons[1].Classes);
            Assert.DoesNotContain("accent", buttons[2].Classes);
            Assert.DoesNotContain("dangerButton", buttons[2].Classes);
            dialog.Close();
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task EnterFiresTheDefaultButtonsResult()
    {
        await RunUi(() =>
        {
            var (dialog, chosen) = Open(ThreeButtonSpec());
            dialog.Press(PhysicalKey.Enter);
            Assert.Equal("proceed", chosen());
            dialog.Close();
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task EscFiresTheCancelButtonsResult()
    {
        await RunUi(() =>
        {
            var (dialog, chosen) = Open(ThreeButtonSpec());
            dialog.Press(PhysicalKey.Escape);
            Assert.Equal("back", chosen());
            dialog.Close();
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task WithoutADefaultInTheSpec_EnterDoesNothing()
    {
        await RunUi(() =>
        {
            var (dialog, chosen) = Open(ThreeButtonSpec(withDefault: false));
            Assert.DoesNotContain(dialog.Descendants<Button>(), b => b.IsDefault);
            dialog.Press(PhysicalKey.Enter);
            Assert.Equal("unset", chosen());
            dialog.Close();
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task BodyWrapsAndWindowIsNotResizable()
    {
        await RunUi(() =>
        {
            var (dialog, _) = Open(ThreeButtonSpec());
            var body = dialog.Descendants<TextBlock>().First(t => t.Text == "Component body.");
            Assert.Equal(Avalonia.Media.TextWrapping.Wrap, body.TextWrapping);
            Assert.Equal(400, body.MaxWidth);
            Assert.False(dialog.CanResize);
            Assert.Equal("Component title", dialog.Title);
            dialog.Close();
            return Task.CompletedTask;
        });
    }
}
