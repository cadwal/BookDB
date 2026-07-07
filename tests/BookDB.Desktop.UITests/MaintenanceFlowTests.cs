using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using BookDB.Data.Interfaces;
using BookDB.Desktop.Localization;
using BookDB.Desktop.ViewModels;
using BookDB.Desktop.Views;
using BookDB.Models;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace BookDB.Desktop.UITests;

/// <summary>
/// Maintenance dialog journey: the window opens with both tabs (integrity check + Move library), and the Move
/// section's validation gates hold through the real controls — Check needs host and username, Move additionally
/// needs a checked target and a typed password, and a target that already holds data demands the explicit
/// replace acknowledgement. The move itself is never run here (its mechanics are covered by the Logic and
/// container-gated round-trip tests).
/// </summary>
public class MaintenanceFlowTests : HeadlessTest
{
    [Fact]
    public async Task OpeningMaintenance_RealizesBothTabs_AndOffersOnlyNonSourceTargets()
    {
        await RunUi(async () =>
        {
            using var host = TestHost.Create();
            var (vm, window) = Open(host);

            var tabs = window.Find<TabControl>();
            Assert.Equal(2, tabs.ItemCount);
            _ = window.ButtonFor(vm.RunCheckCommand);          // integrity actions are wired
            _ = window.ButtonFor(vm.OptimizeAndRepairCommand);

            tabs.SelectedIndex = 1;
            Ui.Pump();
            var move = window.Find<MoveLibraryView>();

            // SQLite is the fixed source, so it is not offered as a target; PostgreSQL is the default target.
            var radios = move.Descendants<RadioButton>(); // SQLite, PostgreSQL, MySQL — in tree order
            Assert.False(radios[0].IsVisible);
            Assert.True(radios[1].IsVisible);
            Assert.True(radios[1].IsChecked);
            Assert.True(radios[2].IsVisible);
            window.Close();
            await Task.CompletedTask;
        });
    }

    [Fact]
    public async Task MoveIsGated_OnHostUsernameCheck_AndATypedPassword()
    {
        var prober = Substitute.For<IPostgresConnectionProber>();
        prober.ProbeAsync(Arg.Any<PostgresOptions>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(ConnectionProbeResult.Succeeded("16.2", 0)); // reachable and empty

        await RunUi(async () =>
        {
            using var host = TestHost.Create(s => s.AddSingleton(prober));
            var (vm, window) = Open(host);
            var move = SelectMoveTab(window);
            var mv = vm.MoveLibrary;

            var boxes = move.Descendants<TextBox>(); // Host, Port, Database, Username, Password, progress log — in tree order
            var checkButton = move.ButtonFor(mv.CheckTargetCommand);
            var moveButton = move.ButtonFor(mv.MoveCommand);

            Assert.False(checkButton.IsEffectivelyEnabled); // no host yet
            window.TypeInto(boxes[0], "db.test.local");
            Assert.False(checkButton.IsEffectivelyEnabled); // still no username
            window.TypeInto(boxes[3], "bookuser");
            Assert.True(checkButton.IsEffectivelyEnabled);
            Assert.False(moveButton.IsEffectivelyEnabled); // target not checked yet

            await ((IAsyncRelayCommand)checkButton.Command!).ExecuteAsync(null);
            Ui.Pump();
            Assert.True(mv.TargetIsEmpty);
            Assert.Contains(move.Descendants<TextBlock>(), // the ready-to-receive note tells the user the check passed
                t => t.IsVisible && t.Text == Resources.MoveLibrary_Target_Empty);
            Assert.False(moveButton.IsEffectivelyEnabled); // password still missing

            // Unlike the Settings tab, a stored password is deliberately not reused: the move target is a new
            // connection whose credential the user must supply explicitly.
            host.Resolve<ISecretStore>().Set(
                new PostgresOptions { Host = "db.test.local", Username = "bookuser" }.AccountKey, "stored");
            Assert.False(mv.MoveCommand.CanExecute(null));

            var passwordBox = boxes.Single(b => b.PasswordChar == '•');
            window.TypeInto(passwordBox, "typed-secret");
            Assert.True(moveButton.IsEffectivelyEnabled);
            window.Close();
        });
    }

    [Fact]
    public async Task TargetWithExistingData_RequiresTheReplaceAcknowledgement()
    {
        var prober = Substitute.For<IPostgresConnectionProber>();
        prober.ProbeAsync(Arg.Any<PostgresOptions>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(ConnectionProbeResult.Succeeded("16.2", 42)); // reachable, already holds a library

        await RunUi(async () =>
        {
            using var host = TestHost.Create(s => s.AddSingleton(prober));
            var (vm, window) = Open(host);
            var move = SelectMoveTab(window);
            var mv = vm.MoveLibrary;

            EnterPostgresTarget(window, move);
            var moveButton = move.ButtonFor(mv.MoveCommand);
            await ((IAsyncRelayCommand)move.ButtonFor(mv.CheckTargetCommand).Command!).ExecuteAsync(null);
            Ui.Pump();

            Assert.True(mv.TargetHasData);
            // The replace warning carries the actual record count, so the user knows what is at stake.
            Assert.Contains(move.Descendants<TextBlock>(),
                t => t.IsVisible && t.Text == mv.TargetHasDataWarning && t.Text.Contains("42"));
            var acknowledge = move.Descendants<CheckBox>()[0]; // acknowledge-replace, then switch-active — in tree order
            Assert.True(acknowledge.IsVisible);
            Assert.False(moveButton.IsEffectivelyEnabled); // data would be replaced — needs the acknowledgement

            acknowledge.IsChecked = true;
            Ui.Pump();
            Assert.True(moveButton.IsEffectivelyEnabled);

            acknowledge.IsChecked = false;
            Ui.Pump();
            Assert.False(moveButton.IsEffectivelyEnabled);
            window.Close();
        });
    }

    [Fact]
    public async Task SwitchingTargetBackend_ResetsTheCheck_AndSwapsTheDefaultPort()
    {
        var prober = Substitute.For<IPostgresConnectionProber>();
        prober.ProbeAsync(Arg.Any<PostgresOptions>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(ConnectionProbeResult.Succeeded("16.2", 0));

        await RunUi(async () =>
        {
            using var host = TestHost.Create(s => s.AddSingleton(prober));
            var (vm, window) = Open(host);
            var move = SelectMoveTab(window);
            var mv = vm.MoveLibrary;

            EnterPostgresTarget(window, move);
            await ((IAsyncRelayCommand)move.ButtonFor(mv.CheckTargetCommand).Command!).ExecuteAsync(null);
            Ui.Pump();
            Assert.True(mv.MoveCommand.CanExecute(null));

            move.Descendants<RadioButton>()[2].IsChecked = true; // MySQL
            Ui.Pump();

            var boxes = move.Descendants<TextBox>();
            Assert.Equal("3306", boxes[1].Text); // untouched default port follows the engine
            Assert.False(mv.TargetChecked); // the content check must be redone against the new engine
            Assert.False(move.ButtonFor(mv.MoveCommand).IsEffectivelyEnabled);
            window.Close();
        });
    }

    private static (MaintenanceViewModel Vm, MaintenanceDialog Window) Open(TestHost host)
    {
        var vm = host.Resolve<MaintenanceViewModel>();
        var window = new MaintenanceDialog { DataContext = vm };
        window.Show();
        Ui.Pump();
        return (vm, window);
    }

    private static MoveLibraryView SelectMoveTab(MaintenanceDialog window)
    {
        window.Find<TabControl>().SelectedIndex = 1;
        Ui.Pump();
        return window.Find<MoveLibraryView>();
    }

    /// <summary>Types a complete PostgreSQL target (host, username, password) into the real boxes.</summary>
    private static void EnterPostgresTarget(MaintenanceDialog window, MoveLibraryView move)
    {
        var boxes = move.Descendants<TextBox>(); // Host, Port, Database, Username, Password, progress log — in tree order
        window.TypeInto(boxes[0], "db.test.local");
        window.TypeInto(boxes[3], "bookuser");
        window.TypeInto(boxes.Single(b => b.PasswordChar == '•'), "typed-secret");
    }
}
