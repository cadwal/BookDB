using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using BookDB.Desktop.ViewModels;
using BookDB.Desktop.Views;
using Xunit;

namespace BookDB.Desktop.UITests;

/// <summary>
/// Tab-order defects surfaced during v2.2 UAT: the Settings ▸ Database backend radio group used to be three
/// separate tab stops (so Tab/Shift+Tab walked each radio individually instead of treating the group as one
/// control with arrow-key navigation inside it), and the read-only progress logs in Maintenance and Move-library
/// were reachable by Tab despite having nothing to type into.
/// </summary>
public class TabOrderKeyboardTests : HeadlessTest
{
    [Fact]
    public async Task DatabaseBackendRadioGroup_IsASingleTabStop()
    {
        await RunUi(async () =>
        {
            using var host = TestHost.Create();
            var vm = host.Resolve<SettingsWindowViewModel>();
            await vm.InitializeAsync();
            var window = new SettingsWindow { DataContext = vm };
            window.Show();
            Ui.Pump();
            vm.SelectedTabIndex = 7; // Database
            Ui.Pump();

            var dbView = window.Find<DatabaseSettingsView>();
            var radios = dbView.Descendants<RadioButton>(); // Sqlite, PostgreSQL, MySQL — in tree order
            Assert.Equal(3, radios.Count);

            // PostgreSQL selected so the server-connection form (and its Host box, right after the group) is
            // enabled and focusable — giving an unambiguous landing spot once Tab has left the radio group.
            radios[1].IsChecked = true;
            Ui.Pump();

            radios[0].Focus();
            Ui.Pump();
            Assert.True(radios[0].IsFocused);

            // Forward from the first radio must leave the group entirely, not step onto the second radio.
            window.Press(PhysicalKey.Tab);
            Assert.False(radios[1].IsFocused);
            Assert.False(radios[2].IsFocused);

            var hostBox = dbView.Descendants<TextBox>().First(); // Host — first focusable control after the group
            Assert.True(hostBox.IsFocused);

            // And Shift+Tab back from there must return straight to the group, not step onto the third radio.
            window.Press(PhysicalKey.Tab, RawInputModifiers.Shift);
            Assert.False(radios[2].IsFocused);
            Assert.Contains(radios, r => r.IsFocused);

            window.Close();
        });
    }

    [Fact]
    public async Task MoveLibraryTargetRadioGroup_IsASingleTabStop()
    {
        await RunUi(async () =>
        {
            using var host = TestHost.Create();
            var vm = host.Resolve<MaintenanceViewModel>();
            var dialog = new MaintenanceDialog { DataContext = vm };
            dialog.Show();
            Ui.Pump();
            dialog.Find<TabControl>().SelectedIndex = 1; // Move library
            Ui.Pump();
            var move = dialog.Find<MoveLibraryView>();

            var radios = move.Descendants<RadioButton>().Where(r => r.IsEffectivelyVisible).ToList();
            Assert.Equal(2, radios.Count); // PostgreSQL, MySQL — SQLite is the fixed source, not offered

            // PostgreSQL selected so the server-connection form (and its Host box, right after the group) is
            // visible and focusable — giving an unambiguous landing spot once Tab has left the radio group.
            radios[0].IsChecked = true;
            Ui.Pump();

            radios[0].Focus();
            Ui.Pump();
            Assert.True(radios[0].IsFocused);

            dialog.Press(PhysicalKey.Tab);
            Assert.False(radios[1].IsFocused);
            var hostBox = move.Descendants<TextBox>().First(t => !t.IsReadOnly);
            Assert.True(hostBox.IsFocused);

            dialog.Close();
        });
    }

    [Fact]
    public async Task MaintenanceLog_IsSkippedInTabOrder()
    {
        await RunUi(async () =>
        {
            using var host = TestHost.Create();
            var vm = host.Resolve<MaintenanceViewModel>();
            var dialog = new MaintenanceDialog { DataContext = vm };
            dialog.Show();
            Ui.Pump();

            var log = dialog.Descendants<TextBox>().Single();
            Assert.False(log.IsTabStop);

            // The check/repair buttons sit right after the log in the tab body — Shift+Tab from the first of
            // them must not land on the log, proving it is excluded from keyboard traversal.
            var runCheck = dialog.ButtonFor(vm.RunCheckCommand);
            runCheck.Focus();
            Ui.Pump();
            Assert.True(runCheck.IsFocused);

            dialog.Press(PhysicalKey.Tab, RawInputModifiers.Shift);
            Assert.False(log.IsFocused);

            // Still reachable by mouse/explicit focus — only Tab traversal skips it.
            log.Focus();
            Ui.Pump();
            Assert.True(log.IsFocused);

            dialog.Close();
        });
    }

    [Fact]
    public async Task MoveLibraryLog_IsSkippedInTabOrder()
    {
        await RunUi(async () =>
        {
            using var host = TestHost.Create();
            var vm = host.Resolve<MaintenanceViewModel>();
            var dialog = new MaintenanceDialog { DataContext = vm };
            dialog.Show();
            Ui.Pump();
            dialog.Find<TabControl>().SelectedIndex = 1; // Move library
            Ui.Pump();
            var move = dialog.Find<MoveLibraryView>();

            // The only read-only TextBox in the view — the rest are the (hidden, SQLite-target) connection form.
            var log = move.Descendants<TextBox>().Single(t => t.IsReadOnly);
            Assert.False(log.IsTabStop);

            // The Move button itself is gated (no target checked yet), so anchor on the always-enabled
            // "switch active" checkbox that sits right before it in the same action-bar row. (The other
            // checkbox in this view — "acknowledge replace" — stays hidden until a target check finds data.)
            var switchActive = move.Descendants<CheckBox>().Single(c => c.IsEffectivelyVisible);
            switchActive.Focus();
            Ui.Pump();
            Assert.True(switchActive.IsFocused);

            dialog.Press(PhysicalKey.Tab);
            Assert.False(log.IsFocused);

            log.Focus();
            Ui.Pump();
            Assert.True(log.IsFocused);

            dialog.Close();
        });
    }
}
