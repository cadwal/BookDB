using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BookDB.Data.Interfaces;
using BookDB.Desktop.Services;
using BookDB.Desktop.Tests.Helpers;
using BookDB.Desktop.ViewModels;
using BookDB.Models;
using Xunit;

namespace BookDB.Desktop.Tests.ViewModels;

public sealed class DatabaseSettingsViewModelTests
{
    /// <summary>Records the last probe call and returns a preset result.</summary>
    private sealed class StubProber : IPostgresConnectionProber
    {
        private readonly ConnectionProbeResult _result;
        public PostgresOptions? LastOptions { get; private set; }
        public string? LastPassword { get; private set; }
        public bool WasCalled { get; private set; }

        public StubProber(ConnectionProbeResult result) => _result = result;

        public Task<ConnectionProbeResult> ProbeAsync(
            PostgresOptions options, string? password, CancellationToken ct = default)
        {
            WasCalled = true;
            LastOptions = options;
            LastPassword = password;
            return Task.FromResult(_result);
        }
    }

    /// <summary>In-memory secret store recording the last write.</summary>
    private sealed class FakeSecretStore : ISecretStore
    {
        private readonly System.Collections.Generic.Dictionary<string, string> _secrets = new();
        public string? LastSetAccount { get; private set; }
        public string? LastSetSecret { get; private set; }

        public string? Get(string account) => _secrets.TryGetValue(account, out var v) ? v : null;
        public void Set(string account, string secret)
        {
            _secrets[account] = secret;
            LastSetAccount = account;
            LastSetSecret = secret;
        }
        public void Delete(string account) => _secrets.Remove(account);
        public void Seed(string account, string secret) => _secrets[account] = secret;
    }

    private static readonly ConnectionProbeResult DefaultProbe =
        ConnectionProbeResult.Succeeded("16.0", 0);

    private static DatabaseSettingsViewModel CreateViewModel(
        InMemoryBootstrapConfigService config,
        bool keyringAvailable = true,
        IPostgresConnectionProber? prober = null,
        ISecretStore? secretStore = null)
    {
        var availability = keyringAvailable
            ? SecretStoreAvailability.Available
            : SecretStoreAvailability.Unavailable("no credential store");
        return new DatabaseSettingsViewModel(
            config,
            availability,
            prober ?? new StubProber(DefaultProbe),
            secretStore ?? new FakeSecretStore());
    }

    [Fact]
    public void Defaults_SelectSqlite_AndRequireTls()
    {
        var vm = CreateViewModel(new InMemoryBootstrapConfigService());

        Assert.True(vm.IsSqliteSelected);
        Assert.False(vm.IsPostgreSqlSelected);
        Assert.Equal("Require", vm.SelectedSslMode!.Value);
    }

    [Fact]
    public void AvailableSslModes_AreDisablePreferRequireVerifyFull_InOrder()
    {
        var vm = CreateViewModel(new InMemoryBootstrapConfigService());

        Assert.Equal(
            new[] { "Disable", "Prefer", "Require", "VerifyFull" },
            vm.AvailableSslModes.Select(m => m.Value));
    }

    [Fact]
    public async Task LoadAsync_WithStoredPostgresAndKeyring_SelectsPostgresAndPopulatesFields()
    {
        var config = new InMemoryBootstrapConfigService();
        config.Config.Backend = "PostgreSql";
        config.Config.Postgres.Host = "db.example.com";
        config.Config.Postgres.Port = 6543;
        config.Config.Postgres.Database = "mylib";
        config.Config.Postgres.Username = "alice";
        config.Config.Postgres.SslMode = "VerifyFull";
        var vm = CreateViewModel(config, keyringAvailable: true);

        await vm.LoadAsync(TestContext.Current.CancellationToken);

        Assert.True(vm.IsPostgreSqlSelected);
        Assert.False(vm.IsSqliteSelected);
        Assert.Equal("db.example.com", vm.Host);
        Assert.Equal("6543", vm.Port);
        Assert.Equal("mylib", vm.DatabaseName);
        Assert.Equal("alice", vm.Username);
        Assert.Equal("VerifyFull", vm.SelectedSslMode!.Value);
    }

    [Fact]
    public async Task LoadAsync_WithStoredPostgresButNoKeyring_FallsBackToSqlite()
    {
        var config = new InMemoryBootstrapConfigService();
        config.Config.Backend = "PostgreSql";
        var vm = CreateViewModel(config, keyringAvailable: false);

        await vm.LoadAsync(TestContext.Current.CancellationToken);

        Assert.True(vm.IsSqliteSelected);
        Assert.False(vm.IsPostgreSqlSelected);
    }

    [Fact]
    public void SelectingPostgres_WithoutKeyring_IsVetoed()
    {
        var vm = CreateViewModel(new InMemoryBootstrapConfigService(), keyringAvailable: false);

        vm.IsPostgreSqlSelected = true;

        Assert.False(vm.IsPostgreSqlSelected);
        Assert.False(vm.IsKeyringAvailable);
    }

    [Fact]
    public void SelectingPostgres_WithKeyring_DeselectsSqlite()
    {
        var vm = CreateViewModel(new InMemoryBootstrapConfigService(), keyringAvailable: true);

        vm.IsPostgreSqlSelected = true;

        Assert.True(vm.IsPostgreSqlSelected);
        Assert.False(vm.IsSqliteSelected);
    }

    [Fact]
    public async Task IsDirty_FalseAfterLoad_TrueAfterEditingAField()
    {
        var config = new InMemoryBootstrapConfigService();
        config.Config.Backend = "PostgreSql";
        config.Config.Postgres.Host = "db.example.com";
        var vm = CreateViewModel(config, keyringAvailable: true);
        await vm.LoadAsync(TestContext.Current.CancellationToken);

        Assert.False(vm.IsDirty);

        vm.Host = "other.example.com";

        Assert.True(vm.IsDirty);
    }

    [Fact]
    public async Task IsDirty_TrueAfterSwitchingBackend()
    {
        var vm = CreateViewModel(new InMemoryBootstrapConfigService(), keyringAvailable: true);
        await vm.LoadAsync(TestContext.Current.CancellationToken);

        Assert.False(vm.IsDirty);

        vm.IsPostgreSqlSelected = true;

        Assert.True(vm.IsDirty);
    }

    [Fact]
    public void ShowTlsDisableWarning_OnlyWhenSslModeIsDisable()
    {
        var vm = CreateViewModel(new InMemoryBootstrapConfigService());

        Assert.False(vm.ShowTlsDisableWarning);

        vm.SelectedSslMode = vm.AvailableSslModes.First(m => m.Value == "Disable");
        Assert.True(vm.ShowTlsDisableWarning);

        vm.SelectedSslMode = vm.AvailableSslModes.First(m => m.Value == "Require");
        Assert.False(vm.ShowTlsDisableWarning);
    }

    [Fact]
    public void KeyringUnavailable_ExposesAvailabilityAndMessage()
    {
        var vm = CreateViewModel(new InMemoryBootstrapConfigService(), keyringAvailable: false);

        Assert.False(vm.IsKeyringAvailable);
        Assert.False(string.IsNullOrWhiteSpace(vm.KeyringUnavailableMessage));
    }

    private static DatabaseSettingsViewModel PostgresViewModel(IPostgresConnectionProber prober)
    {
        var vm = CreateViewModel(new InMemoryBootstrapConfigService(), keyringAvailable: true, prober: prober);
        vm.IsPostgreSqlSelected = true;
        vm.Host = "db.example.com";
        return vm;
    }

    [Fact]
    public void CanTestConnection_FalseForSqlite_TrueForPostgresWithHost_FalseWhenHostBlank()
    {
        var vm = CreateViewModel(new InMemoryBootstrapConfigService(), keyringAvailable: true);
        Assert.False(vm.TestConnectionCommand.CanExecute(null));

        vm.IsPostgreSqlSelected = true;
        vm.Host = "db.example.com";
        Assert.True(vm.TestConnectionCommand.CanExecute(null));

        vm.Host = "   ";
        Assert.False(vm.TestConnectionCommand.CanExecute(null));
    }

    [Fact]
    public async Task TestConnection_Success_ShowsNonErrorResultWithVersionAndCount()
    {
        var vm = PostgresViewModel(new StubProber(ConnectionProbeResult.Succeeded("16.3", 42)));

        await vm.TestConnectionCommand.ExecuteAsync(null);

        Assert.False(vm.TestResultIsError);
        Assert.True(vm.HasTestResult);
        Assert.Contains("16.3", vm.TestResultMessage);
        Assert.Contains("42", vm.TestResultMessage);
        Assert.False(vm.IsTesting);
    }

    [Fact]
    public async Task TestConnection_SuccessWithNoBookCount_ShowsNonErrorResultWithVersion()
    {
        var vm = PostgresViewModel(new StubProber(ConnectionProbeResult.Succeeded("16.3", null)));

        await vm.TestConnectionCommand.ExecuteAsync(null);

        Assert.False(vm.TestResultIsError);
        Assert.Contains("16.3", vm.TestResultMessage);
    }

    [Theory]
    [InlineData(ConnectionProbeStatus.AuthenticationFailed)]
    [InlineData(ConnectionProbeStatus.ConnectionRefused)]
    [InlineData(ConnectionProbeStatus.Timeout)]
    [InlineData(ConnectionProbeStatus.TlsError)]
    public async Task TestConnection_FailureStatuses_ShowErrorResult(ConnectionProbeStatus status)
    {
        var vm = PostgresViewModel(new StubProber(ConnectionProbeResult.Failed(status, "detail")));

        await vm.TestConnectionCommand.ExecuteAsync(null);

        Assert.True(vm.TestResultIsError);
        Assert.True(vm.HasTestResult);
        Assert.False(string.IsNullOrWhiteSpace(vm.TestResultMessage));
    }

    [Fact]
    public async Task TestConnection_UnknownError_MessageIncludesDetail()
    {
        var vm = PostgresViewModel(new StubProber(ConnectionProbeResult.Failed(ConnectionProbeStatus.Unknown, "boom-detail")));

        await vm.TestConnectionCommand.ExecuteAsync(null);

        Assert.True(vm.TestResultIsError);
        Assert.Contains("boom-detail", vm.TestResultMessage);
    }

    [Fact]
    public async Task TestConnection_PassesEnteredFieldsAndPassword()
    {
        var prober = new StubProber(DefaultProbe);
        var vm = PostgresViewModel(prober);
        vm.Host = "h.example.com";
        vm.Port = "6543";
        vm.DatabaseName = "mylib";
        vm.Username = "alice";
        vm.Password = "secret";
        vm.SelectedSslMode = vm.AvailableSslModes.First(m => m.Value == "Disable");

        await vm.TestConnectionCommand.ExecuteAsync(null);

        Assert.True(prober.WasCalled);
        Assert.Equal("h.example.com", prober.LastOptions!.Host);
        Assert.Equal(6543, prober.LastOptions.Port);
        Assert.Equal("mylib", prober.LastOptions.Database);
        Assert.Equal("alice", prober.LastOptions.Username);
        Assert.Equal("Disable", prober.LastOptions.SslMode);
        Assert.Equal("secret", prober.LastPassword);
    }

    [Fact]
    public async Task TestConnection_EmptyPassword_PassesNull()
    {
        var prober = new StubProber(DefaultProbe);
        var vm = PostgresViewModel(prober);
        vm.Password = string.Empty;

        await vm.TestConnectionCommand.ExecuteAsync(null);

        Assert.Null(prober.LastPassword);
    }

    [Fact]
    public async Task TestConnection_BlankPasswordWithStoredSecret_ProbesWithStoredSecret()
    {
        var prober = new StubProber(DefaultProbe);
        var secrets = new FakeSecretStore();
        secrets.Seed("alice@db.example.com:5432/bookdb", "stored-pw");
        var vm = CreateViewModel(
            new InMemoryBootstrapConfigService(), keyringAvailable: true, prober: prober, secretStore: secrets);
        vm.IsPostgreSqlSelected = true;
        vm.Host = "db.example.com";
        vm.Username = "alice";
        vm.Password = string.Empty;

        await vm.TestConnectionCommand.ExecuteAsync(null);

        Assert.True(prober.WasCalled);
        Assert.Equal("stored-pw", prober.LastPassword);
    }

    [Fact]
    public async Task SwitchingBackend_ClearsTestResult()
    {
        var vm = PostgresViewModel(new StubProber(DefaultProbe));
        await vm.TestConnectionCommand.ExecuteAsync(null);
        Assert.True(vm.HasTestResult);

        vm.IsSqliteSelected = true;

        Assert.False(vm.HasTestResult);
        Assert.Null(vm.TestResultMessage);
    }

    [Fact]
    public async Task ValidateForSave_PostgresWithoutPasswordOrStoredSecret_ReturnsFalseWithInlineError()
    {
        var vm = CreateViewModel(new InMemoryBootstrapConfigService(), keyringAvailable: true);
        await vm.LoadAsync(TestContext.Current.CancellationToken);
        vm.IsPostgreSqlSelected = true;
        vm.Host = "db.example.com";
        vm.Username = "alice";
        vm.Password = string.Empty;

        Assert.False(vm.ValidateForSave());
        Assert.True(vm.HasApplyError);
    }

    [Fact]
    public async Task ValidateForSave_Sqlite_ReturnsTrue()
    {
        var vm = CreateViewModel(new InMemoryBootstrapConfigService(), keyringAvailable: true);
        await vm.LoadAsync(TestContext.Current.CancellationToken);

        Assert.True(vm.ValidateForSave());
        Assert.False(vm.HasApplyError);
    }

    [Fact]
    public async Task SaveAsync_SwitchToPostgres_WritesConfigStoresSecret_AndMarksDbChanged()
    {
        var config = new InMemoryBootstrapConfigService();
        var secrets = new FakeSecretStore();
        var vm = CreateViewModel(config, keyringAvailable: true, secretStore: secrets);
        await vm.LoadAsync(TestContext.Current.CancellationToken);

        vm.IsPostgreSqlSelected = true;
        vm.Host = "db.example.com";
        vm.Port = "6543";
        vm.DatabaseName = "mylib";
        vm.Username = "alice";
        vm.Password = "s3cret";
        vm.SelectedSslMode = vm.AvailableSslModes.First(m => m.Value == "VerifyFull");

        Assert.True(vm.ValidateForSave());
        await vm.SaveAsync(TestContext.Current.CancellationToken);

        Assert.Equal("PostgreSql", config.Config.Backend);
        Assert.Equal("db.example.com", config.Config.Postgres.Host);
        Assert.Equal(6543, config.Config.Postgres.Port);
        Assert.Equal("mylib", config.Config.Postgres.Database);
        Assert.Equal("alice", config.Config.Postgres.Username);
        Assert.Equal("VerifyFull", config.Config.Postgres.SslMode);
        Assert.Equal("s3cret", secrets.LastSetSecret);
        Assert.Equal("alice@db.example.com:6543/mylib", secrets.LastSetAccount);
        Assert.True(vm.DbChanged);
    }

    [Fact]
    public async Task SaveAsync_SwitchToSqlite_WritesBackend_WithoutStoringSecret_AndMarksDbChanged()
    {
        var config = new InMemoryBootstrapConfigService();
        config.Config.Backend = "PostgreSql";
        config.Config.Postgres.Host = "db.example.com";
        config.Config.Postgres.Username = "alice";
        var secrets = new FakeSecretStore();
        var vm = CreateViewModel(config, keyringAvailable: true, secretStore: secrets);
        await vm.LoadAsync(TestContext.Current.CancellationToken);

        vm.IsSqliteSelected = true;

        Assert.True(vm.ValidateForSave());
        await vm.SaveAsync(TestContext.Current.CancellationToken);

        Assert.Equal("Sqlite", config.Config.Backend);
        Assert.Null(secrets.LastSetSecret);
        Assert.True(vm.DbChanged);
    }

    [Fact]
    public async Task SaveAsync_NotDirty_WritesNothing_AndDbChangedFalse()
    {
        var config = new InMemoryBootstrapConfigService();
        var vm = CreateViewModel(config, keyringAvailable: true);
        await vm.LoadAsync(TestContext.Current.CancellationToken);

        await vm.SaveAsync(TestContext.Current.CancellationToken);

        Assert.Equal("Sqlite", config.Config.Backend);
        Assert.False(vm.DbChanged);
    }

    [Fact]
    public async Task SaveAsync_PostgresWithStoredSecretAndBlankPassword_ProceedsWithoutRewritingSecret()
    {
        var config = new InMemoryBootstrapConfigService();
        var secrets = new FakeSecretStore();
        secrets.Seed("alice@db.example.com:5432/bookdb", "already-stored");
        var vm = CreateViewModel(config, keyringAvailable: true, secretStore: secrets);
        await vm.LoadAsync(TestContext.Current.CancellationToken);

        vm.IsPostgreSqlSelected = true;
        vm.Host = "db.example.com";
        vm.Username = "alice";
        vm.Password = string.Empty;

        Assert.True(vm.ValidateForSave());
        await vm.SaveAsync(TestContext.Current.CancellationToken);

        Assert.Equal("PostgreSql", config.Config.Backend);
        Assert.Null(secrets.LastSetSecret); // existing secret left untouched
        Assert.True(vm.DbChanged);
    }
}
