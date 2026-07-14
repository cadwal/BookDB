using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using BookDB.Desktop.ViewModels;
using BookDB.Desktop.Views;
using Xunit;

namespace BookDB.Desktop.UITests;

public class ManageLookupsWindowKeyboardTests : HeadlessTest
{
    [Fact]
    public async Task Esc_ClosesWhenIdle()
    {
        await RunUi(async () =>
        {
            using var host = TestHost.Create();
            var vm = host.Resolve<ManageLookupsViewModel>();
            await vm.InitializeAsync(null);
            var closed = false;
            vm.CloseWindow = () => closed = true;
            var window = new ManageLookupsWindow { DataContext = vm };
            window.Show();
            Ui.Pump();

            window.Press(PhysicalKey.Escape);
            Assert.True(closed);
        });
    }

    [Fact]
    public async Task Esc_IsIgnoredWhileATabsInlineEditorIsOpen_ThenClosesOnceItsDismissed()
    {
        await RunUi(async () =>
        {
            using var host = TestHost.Create();
            var vm = host.Resolve<ManageLookupsViewModel>();
            await vm.InitializeAsync(null);
            var closed = false;
            vm.CloseWindow = () => closed = true;
            var window = new ManageLookupsWindow { DataContext = vm };
            window.Show();
            Ui.Pump();

            window.Find<TabControl>().SelectedIndex = 1; // Publisher
            Ui.Pump();
            vm.PublisherTab.AddCommand.Execute(null);
            Ui.Pump();
            Assert.True(vm.IsAnyTabEditing);

            window.Press(PhysicalKey.Escape);
            Assert.False(closed);

            // Dismissing the inline editor (Discard) clears the gate — Esc then closes the window.
            vm.PublisherTab.CancelCommand.Execute(null);
            Ui.Pump();
            Assert.False(vm.IsAnyTabEditing);

            window.Press(PhysicalKey.Escape);
            Assert.True(closed);
        });
    }

    [Fact]
    public async Task Esc_IsBlockedByAnEditorLeftOpenOnATabTheUserHasSwitchedAwayFrom()
    {
        await RunUi(async () =>
        {
            using var host = TestHost.Create();
            var vm = host.Resolve<ManageLookupsViewModel>();
            await vm.InitializeAsync(null);
            var closed = false;
            vm.CloseWindow = () => closed = true;
            var window = new ManageLookupsWindow { DataContext = vm };
            window.Show();
            Ui.Pump();

            // Open Publisher's editor, then switch away to Series without dismissing it — the pending edit
            // must still block Esc so it can't be lost by an accidental keypress on another tab.
            window.Find<TabControl>().SelectedIndex = 1; // Publisher
            Ui.Pump();
            vm.PublisherTab.AddCommand.Execute(null);
            Ui.Pump();
            window.Find<TabControl>().SelectedIndex = 2; // Series
            Ui.Pump();

            Assert.True(vm.IsAnyTabEditing);
            window.Press(PhysicalKey.Escape);
            Assert.False(closed);

            // Dismissing it from the (now off-screen) Publisher tab clears the gate.
            vm.PublisherTab.CancelCommand.Execute(null);
            Ui.Pump();
            Assert.False(vm.IsAnyTabEditing);
            window.Press(PhysicalKey.Escape);
            Assert.True(closed);
        });
    }
}
