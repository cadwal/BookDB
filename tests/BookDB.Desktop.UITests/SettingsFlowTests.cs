using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using BookDB.Data.Interfaces;
using BookDB.Desktop.Localization;
using BookDB.Desktop.Services;
using BookDB.Desktop.Theming;
using BookDB.Desktop.ViewModels;
using BookDB.Desktop.Views;
using BookDB.Logic.Services;
using BookDB.Models;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace BookDB.Desktop.UITests;

/// <summary>
/// Settings journey: navigate every tab (each realizes and binds without error, including the Database tab), then
/// change non-destructive settings across the safe tabs and Save — asserting each value persists. The Database
/// backend switch itself is exercised only up to its process-exiting edge (the restart service is faked): the
/// validation gate, the saved-password reuse rules, and the branch that skips the Settings-table writes.
/// </summary>
public class SettingsFlowTests : HeadlessTest
{
    [Fact]
    public async Task NavigatingEveryTab_RealizesEachIncludingDatabase()
    {
        await RunUi(async () =>
        {
            using var host = TestHost.Create();
            var vm = host.Resolve<SettingsWindowViewModel>();
            await vm.InitializeAsync();
            var window = new SettingsWindow { DataContext = vm };
            window.Show();
            Ui.Pump();

            var tabCount = window.Descendants<TabItem>().Count;
            Assert.Equal(8, tabCount);

            for (var i = 0; i < tabCount; i++)
            {
                vm.SelectedTabIndex = i;
                Ui.Pump();
                Assert.Equal(i, vm.SelectedTabIndex);
            }

            // The Database tab (last) is the motivating case — its view realizes when selected.
            vm.SelectedTabIndex = 7;
            Ui.Pump();
            Assert.NotEmpty(window.Descendants<DatabaseSettingsView>());
            Assert.False(vm.DatabaseTab.IsDirty); // navigation alone must not mark the backend switch dirty
            window.Close();
        });
    }

    [Fact]
    public async Task ChangingNonDestructiveSettingsAndSaving_PersistsEachValue()
    {
        await RunUi(async () =>
        {
            using var host = TestHost.Create();
            var collection = await SeedData.AddCollectionAsync(host, "Default Collection");

            var vm = host.Resolve<SettingsWindowViewModel>();
            await vm.InitializeAsync();
            var window = new SettingsWindow { DataContext = vm };
            window.Show();
            Ui.Pump();

            // Change a setting on several safe tabs (all persist to the Settings table, no restart).
            vm.GeneralTab.DefaultCollectionId = collection.CollectionId;
            vm.BrowseTab.IsDisplayNameSelected = true;
            vm.BrowseTab.IsSortNameSelected = false;
            vm.LookupTab.GoogleBooksEnabled = false;
            vm.ImportTab.OverwritePolicy = "Overwrite";
            vm.AdvancedTab.AutoBackupEnabled = true;
            vm.AdvancedTab.AutoBackupFormat = "CsvArchive";

            Assert.False(vm.DatabaseTab.IsDirty); // stay on the non-destructive save path
            await ((IAsyncRelayCommand)vm.SaveCommand).ExecuteAsync(null);

            var settings = host.Resolve<ISettingsService>();
            Assert.Equal(collection.CollectionId.ToString(), await settings.GetAsync("DefaultCollectionId"));
            Assert.Equal("DisplayName", await settings.GetAsync("AuthorFacetLabel"));
            Assert.Equal("false", await settings.GetAsync("LookupEnabled.GoogleBooks"));
            Assert.Equal("Overwrite", await settings.GetAsync("Import.OverwritePolicy"));
            Assert.Equal("true", await settings.GetAsync("AutoBackup.Enabled"));
            Assert.Equal("CsvArchive", await settings.GetAsync("AutoBackup.Format"));
            window.Close();
        });
    }

    [Fact]
    public async Task ChangingOnlyTheTheme_SavesWithoutARestartPrompt_AndAppliesImmediately()
    {
        await RunUi(async () =>
        {
            using var host = TestHost.Create();
            var (vm, window) = await OpenSettingsAsync(host);
            try
            {
                vm.AppearanceTab.SelectedFlavour =
                    vm.AppearanceTab.AvailableFlavours.First(f => f.Flavour == ThemeFlavour.Vibrant);

                await ((IAsyncRelayCommand)vm.SaveCommand).ExecuteAsync(null);
                Ui.Pump();

                // The flavour persists and is live on the running app — no restart was offered.
                Assert.Equal("Vibrant", host.Resolve<IBootstrapConfigService>().Load().UiTheme);
                var restart = host.Resolve<IApplicationRestartService>();
                await restart.DidNotReceive().ConfirmRestartAsync(Arg.Any<string>());
                Assert.False(vm.AppearanceTab.ThemeChanged); // re-baselined after applying
            }
            finally
            {
                ThemeApplier.Apply(ThemeFlavour.Default);
                window.Close();
            }
        });
    }

    [Fact]
    public async Task SavingServerBackendWithoutAnyPassword_BlocksOnDatabaseTab_AndWritesNothing()
    {
        await RunUi(async () =>
        {
            using var host = TestHost.Create();
            var (vm, window) = await OpenSettingsAsync(host);
            bool? closed = null;
            vm.CloseDialog = r => closed = r;

            EnterPostgresConnection(window, vm, "db.test.local", "bookuser"); // password field left blank, none stored

            // A safe-tab edit made alongside the backend switch — it must not be written on this path.
            vm.LookupTab.GoogleBooksEnabled = false;

            // Save from another tab, so the jump-to-error behaviour is observable.
            vm.SelectedTabIndex = 0;
            Ui.Pump();
            await ((IAsyncRelayCommand)window.ButtonFor(vm.SaveCommand).Command!).ExecuteAsync(null);
            Ui.Pump();

            Assert.Equal(7, vm.SelectedTabIndex); // Save focused the Database tab to surface the blocking error
            Assert.True(vm.DatabaseTab.HasApplyError);
            var view = window.Find<DatabaseSettingsView>();
            Assert.Contains(view.Descendants<TextBlock>(),
                t => t.IsVisible && t.Text == vm.DatabaseTab.ApplyErrorMessage);

            Assert.Null(closed); // the dialog stays open on a blocked Save
            var restart = host.Resolve<IApplicationRestartService>();
            await restart.DidNotReceive().ConfirmRestartAsync(Arg.Any<string>());
            Assert.Equal("Sqlite", host.Resolve<IBootstrapConfigService>().Load().Backend);
            Assert.Null(await host.Resolve<ISettingsService>().GetAsync("LookupEnabled.GoogleBooks"));
            window.Close();
        });
    }

    [Fact]
    public async Task SavingBackendSwitchWithStoredPassword_ReusesIt_PromptsRestart_SkipsSettingsWrites()
    {
        await RunUi(async () =>
        {
            using var host = TestHost.Create();
            host.Resolve<ISecretStore>().Set(
                new PostgresOptions { Host = "db.test.local", Username = "bookuser" }.AccountKey, "s3cret!");

            var (vm, window) = await OpenSettingsAsync(host);
            bool? closed = null;
            vm.CloseDialog = r => closed = r;

            var view = EnterPostgresConnection(window, vm, "db.test.local", "bookuser");
            Assert.True(vm.DatabaseTab.HasSavedPassword); // the stored password satisfies Save without re-entry
            Assert.True(SavedPasswordHint(view).IsVisible); // …and the user is told so next to the blank field

            vm.LookupTab.GoogleBooksEnabled = false;
            await ((IAsyncRelayCommand)window.ButtonFor(vm.SaveCommand).Command!).ExecuteAsync(null);
            Ui.Pump();

            Assert.False(vm.DatabaseTab.HasApplyError);
            var config = host.Resolve<IBootstrapConfigService>().Load();
            Assert.Equal("PostgreSql", config.Backend);
            Assert.Equal("db.test.local", config.Postgres.Host);
            Assert.Equal("bookuser", config.Postgres.Username);

            var restart = host.Resolve<IApplicationRestartService>();
            await restart.Received(1).ConfirmRestartAsync(Arg.Any<string>());
            restart.DidNotReceive().Restart(); // the faked confirm declines, so the process is never replaced
            Assert.True(closed);

            // The backend-switch path deliberately skips the per-database preference tabs.
            Assert.Null(await host.Resolve<ISettingsService>().GetAsync("LookupEnabled.GoogleBooks"));
            window.Close();
        });
    }

    [Fact]
    public async Task ChangingConnectionIdentity_DropsSavedPasswordReuse_AndBlocksSave()
    {
        await RunUi(async () =>
        {
            using var host = TestHost.Create();
            host.Resolve<ISecretStore>().Set(
                new PostgresOptions { Host = "db.test.local", Username = "bookuser" }.AccountKey, "s3cret!");

            var (vm, window) = await OpenSettingsAsync(host);
            var view = EnterPostgresConnection(window, vm, "db.test.local", "bookuser");
            Assert.True(vm.DatabaseTab.HasSavedPassword);
            Assert.True(SavedPasswordHint(view).IsVisible);

            // A different username is a different credential-store identity — the old password must not carry over.
            view.Descendants<TextBox>()[3].Text = "otheruser";
            Ui.Pump();
            Assert.False(vm.DatabaseTab.HasSavedPassword);
            Assert.False(SavedPasswordHint(view).IsVisible); // the hint must not promise a password that won't be used

            await ((IAsyncRelayCommand)window.ButtonFor(vm.SaveCommand).Command!).ExecuteAsync(null);
            Ui.Pump();

            Assert.True(vm.DatabaseTab.HasApplyError);
            Assert.Equal("Sqlite", host.Resolve<IBootstrapConfigService>().Load().Backend);
            window.Close();
        });
    }

    [Fact]
    public async Task SavingWithTypedPassword_StoresItForTheConnection()
    {
        await RunUi(async () =>
        {
            using var host = TestHost.Create();
            var (vm, window) = await OpenSettingsAsync(host);

            var view = EnterPostgresConnection(window, vm, "db.test.local", "bookuser");
            var passwordBox = view.Descendants<TextBox>().Single(b => b.PasswordChar == '•');
            window.TypeInto(passwordBox, "typed-secret");
            Assert.False(vm.DatabaseTab.HasSavedPassword); // nothing stored yet — the typed value is what unlocks Save
            Assert.False(SavedPasswordHint(view).IsVisible);

            await ((IAsyncRelayCommand)window.ButtonFor(vm.SaveCommand).Command!).ExecuteAsync(null);
            Ui.Pump();

            Assert.False(vm.DatabaseTab.HasApplyError);
            Assert.Equal("typed-secret", host.Resolve<ISecretStore>().Get(
                new PostgresOptions { Host = "db.test.local", Username = "bookuser" }.AccountKey));
            Assert.True(vm.DatabaseTab.HasSavedPassword); // the just-saved password is now reusable
            Assert.True(SavedPasswordHint(view).IsVisible);
            window.Close();
        });
    }

    [Fact]
    public async Task TestConnection_IsGatedOnHost_AndFallsBackToTheStoredPassword()
    {
        var prober = Substitute.For<IPostgresConnectionProber>();
        prober.ProbeAsync(Arg.Any<PostgresOptions>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(ConnectionProbeResult.Succeeded("16.2", 3));

        await RunUi(async () =>
        {
            using var host = TestHost.Create(s => s.AddSingleton(prober));
            host.Resolve<ISecretStore>().Set(
                new PostgresOptions { Host = "db.test.local", Username = "bookuser" }.AccountKey, "s3cret!");

            var (vm, window) = await OpenSettingsAsync(host);
            vm.SelectedTabIndex = 7;
            Ui.Pump();
            var view = window.Find<DatabaseSettingsView>();
            view.Descendants<RadioButton>()[1].IsChecked = true; // PostgreSQL
            Ui.Pump();

            var testButton = view.ButtonFor(vm.DatabaseTab.TestConnectionCommand);
            Assert.False(testButton.IsEffectivelyEnabled); // no host yet

            var boxes = view.Descendants<TextBox>(); // Host, Port, Database, Username, Password — in tree order
            window.TypeInto(boxes[0], "db.test.local");
            Assert.True(testButton.IsEffectivelyEnabled);
            window.TypeInto(boxes[3], "bookuser");

            // The password field is blank by design when one is stored; Test must authenticate like the live
            // connection would — with the stored password, not with none.
            await ((IAsyncRelayCommand)testButton.Command!).ExecuteAsync(null);
            Ui.Pump();

            await prober.Received(1).ProbeAsync(Arg.Any<PostgresOptions>(), "s3cret!", Arg.Any<CancellationToken>());
            Assert.True(vm.DatabaseTab.HasTestResult);
            Assert.False(vm.DatabaseTab.TestResultIsError);
            Assert.Contains(view.Descendants<TextBlock>(),
                t => t.IsVisible && t.Text == vm.DatabaseTab.TestResultMessage);
            window.Close();
        });
    }

    [Fact]
    public async Task SelectingPlaintextTls_ShowsTheWarning_AndSecureModeHidesIt()
    {
        await RunUi(async () =>
        {
            using var host = TestHost.Create();
            var (vm, window) = await OpenSettingsAsync(host);
            var view = EnterPostgresConnection(window, vm, "db.test.local", "bookuser");

            var tlsWarning = view.Descendants<TextBlock>()
                .Single(t => t.Text == Resources.Settings_Database_TlsDisableWarning);
            Assert.False(tlsWarning.IsVisible); // the default mode ("Require") is secure

            var tlsCombo = view.Find<ComboBox>();
            tlsCombo.SelectedItem = vm.DatabaseTab.AvailableSslModes.Single(m => m.Value == "Disable");
            Ui.Pump();
            Assert.True(tlsWarning.IsVisible);

            tlsCombo.SelectedItem = vm.DatabaseTab.AvailableSslModes.Single(m => m.Value == "Require");
            Ui.Pump();
            Assert.False(tlsWarning.IsVisible);
            window.Close();
        });
    }

    [Fact]
    public async Task WithoutAKeyring_ServerBackendsAreDisabled_AndSelectionIsVetoed()
    {
        await RunUi(async () =>
        {
            using var host = TestHost.Create(s =>
                s.AddSingleton(SecretStoreAvailability.Unavailable("no keyring")));
            var (vm, window) = await OpenSettingsAsync(host);
            vm.SelectedTabIndex = 7;
            Ui.Pump();

            var view = window.Find<DatabaseSettingsView>();
            var radios = view.Descendants<RadioButton>(); // SQLite, PostgreSQL, MySQL — in tree order
            Assert.True(radios[0].IsEnabled);
            Assert.False(radios[1].IsEnabled);
            Assert.False(radios[2].IsEnabled);
            Assert.Contains(view.Descendants<TextBlock>(),
                t => t.IsVisible && t.Text == vm.DatabaseTab.KeyringUnavailableMessage);

            // Even a programmatic selection is vetoed — no plaintext fallback exists.
            vm.DatabaseTab.IsPostgreSqlSelected = true;
            Ui.Pump();
            Assert.True(vm.DatabaseTab.IsSqliteSelected);
            Assert.False(vm.DatabaseTab.IsRemoteSelected);
            window.Close();
        });
    }

    /// <summary>The saved-password hint under the password box — matched by the same resource the view binds,
    /// so the lookup is culture-independent.</summary>
    private static TextBlock SavedPasswordHint(DatabaseSettingsView view) =>
        view.Descendants<TextBlock>().Single(t => t.Text == Resources.Settings_Database_SavedPasswordHint);

    private static async Task<(SettingsWindowViewModel Vm, SettingsWindow Window)> OpenSettingsAsync(TestHost host)
    {
        var vm = host.Resolve<SettingsWindowViewModel>();
        await vm.InitializeAsync();
        var window = new SettingsWindow { DataContext = vm };
        window.Show();
        Ui.Pump();
        return (vm, window);
    }

    /// <summary>Selects the Database tab, picks PostgreSQL via its radio, and types the connection into the real
    /// host/username boxes. The password box is left untouched — each test decides how Save gets a password.</summary>
    private static DatabaseSettingsView EnterPostgresConnection(
        SettingsWindow window, SettingsWindowViewModel vm, string host, string username)
    {
        vm.SelectedTabIndex = 7;
        Ui.Pump();
        var view = window.Find<DatabaseSettingsView>();
        view.Descendants<RadioButton>()[1].IsChecked = true; // SQLite, PostgreSQL, MySQL — in tree order
        Ui.Pump();

        var boxes = view.Descendants<TextBox>(); // Host, Port, Database, Username, Password — in tree order
        window.TypeInto(boxes[0], host);
        window.TypeInto(boxes[3], username);
        return view;
    }
}
