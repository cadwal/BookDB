using System.Threading;
using System.Threading.Tasks;
using BookDB.Data.DbContexts;
using BookDB.Data.Interfaces;
using BookDB.Desktop.Services;
using BookDB.Desktop.ViewModels;
using BookDB.Logic.Services;
using BookDB.Models;
using BookDB.Models.Interfaces;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace BookDB.Desktop.Tests.ViewModels;

public sealed class MoveLibraryViewModelTests
{
    private readonly IPostgresConnectionProber _prober = Substitute.For<IPostgresConnectionProber>();
    private readonly IMySqlConnectionProber _mySqlProber = Substitute.For<IMySqlConnectionProber>();
    private readonly IMigrationTargetBuilder _targetBuilder = Substitute.For<IMigrationTargetBuilder>();
    private readonly ILibraryMigrationService _migrationService = Substitute.For<ILibraryMigrationService>();
    private readonly IBackupService _backupService = Substitute.For<IBackupService>();
    private readonly IFilePickerService _filePicker = Substitute.For<IFilePickerService>();
    private readonly ISecretStore _secretStore = Substitute.For<ISecretStore>();
    private readonly IApplicationRestartService _restartService = Substitute.For<IApplicationRestartService>();
    private readonly IBootstrapConfigService _bootstrapConfig = Substitute.For<IBootstrapConfigService>();
    private IMigrationTarget _target = null!;

    // Active backend = SQLite, so the move target is PostgreSQL.
    private MoveLibraryViewModel Create(DatabaseBackend active = DatabaseBackend.Sqlite)
    {
        var settings = new AppSettings { Backend = active, SqliteLibraryPath = @"C:\data\library.db" };
        var target = Substitute.For<IMigrationTarget>();
        target.Factory.Returns(Substitute.For<IDbContextFactory<BookDbContext>>());
        target.Resync.Returns(Substitute.For<IIdentitySequenceResync>());
        target.Backup.Returns(Substitute.For<IBackupService>());
        _target = target;
        _targetBuilder.BuildAsync(Arg.Any<DatabaseBackend>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(target);

        return new MoveLibraryViewModel(
            Substitute.For<IDbContextFactory<BookDbContext>>(), settings, _bootstrapConfig, _prober, _mySqlProber,
            _targetBuilder, _migrationService, _backupService, _filePicker, _secretStore, _restartService,
            Substitute.For<IWindowService>());
    }

    private void EnterValidPostgresTarget(MoveLibraryViewModel vm)
    {
        vm.Host = "db.example.com";
        vm.Username = "bookdb";
        vm.Password = "secret";
    }

    private static MigrationResult Completed(bool matching) =>
        new(MigrationOutcome.Completed,
            [new MigrationTableResult(MigrationTable.Book, 10, matching ? 10 : 9)], null, null);

    [Fact]
    public void SqliteSource_MakesPostgresTarget()
    {
        var vm = Create();
        Assert.True(vm.TargetIsPostgres);
        Assert.False(vm.TargetIsSqlite);
        Assert.Contains("library.db", vm.SourceDescription);
    }

    [Fact]
    public void SelectingMySqlTarget_DeselectsOtherTargets()
    {
        var vm = Create(); // SQLite source → default Postgres target

        vm.TargetIsMySql = true;

        Assert.True(vm.TargetIsMySql);
        Assert.False(vm.TargetIsPostgres);
        Assert.False(vm.TargetIsSqlite);
        Assert.True(vm.IsServerTarget);
    }

    [Fact]
    public async Task IncompleteMySqlTarget_IsGatedFromMoving_UntilPasswordEntered()
    {
        var vm = Create();
        vm.TargetIsMySql = true;
        vm.Host = "maria.example.com";
        vm.Username = "bookdb";
        // password left blank — an incomplete server target must not be movable
        _mySqlProber.ProbeAsync(Arg.Any<MySqlOptions>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(ConnectionProbeResult.Succeeded("MySQL 8.0.3", null));

        await vm.CheckTargetCommand.ExecuteAsync(null);

        Assert.True(vm.TargetChecked); // proves the MySQL prober was dispatched, not the Postgres one
        Assert.True(vm.TargetIsEmpty);
        Assert.False(vm.MoveCommand.CanExecute(null)); // gated: password missing

        vm.Password = "secret";
        Assert.True(vm.MoveCommand.CanExecute(null));
    }

    [Fact]
    public void Move_IsBlocked_UntilTargetChecked()
    {
        var vm = Create();
        EnterValidPostgresTarget(vm);

        Assert.False(vm.MoveCommand.CanExecute(null));
    }

    [Fact]
    public async Task Check_Postgres_Empty_AllowsMove()
    {
        var vm = Create();
        EnterValidPostgresTarget(vm);
        _prober.ProbeAsync(Arg.Any<PostgresOptions>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(ConnectionProbeResult.Succeeded("16.2", null));

        await vm.CheckTargetCommand.ExecuteAsync(null);

        Assert.True(vm.TargetChecked);
        Assert.True(vm.TargetIsEmpty);
        Assert.False(vm.TargetHasData);
        Assert.True(vm.MoveCommand.CanExecute(null));
    }

    [Fact]
    public async Task Check_Postgres_WithData_GatesMoveOnAcknowledgement()
    {
        var vm = Create();
        EnterValidPostgresTarget(vm);
        _prober.ProbeAsync(Arg.Any<PostgresOptions>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(ConnectionProbeResult.Succeeded("16.2", 5012));

        await vm.CheckTargetCommand.ExecuteAsync(null);

        Assert.True(vm.TargetHasData);
        Assert.Equal(5012, vm.TargetRecordCount);
        Assert.False(vm.MoveCommand.CanExecute(null)); // blocked without acknowledgement

        vm.AcknowledgeReplace = true;
        Assert.True(vm.MoveCommand.CanExecute(null));
    }

    [Fact]
    public async Task Check_Postgres_Failure_ShowsError_AndStaysUnchecked()
    {
        var vm = Create();
        EnterValidPostgresTarget(vm);
        _prober.ProbeAsync(Arg.Any<PostgresOptions>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(ConnectionProbeResult.Failed(ConnectionProbeStatus.ConnectionRefused, "refused"));

        await vm.CheckTargetCommand.ExecuteAsync(null);

        Assert.False(vm.TargetChecked);
        Assert.True(vm.HasCheckError);
    }

    [Fact]
    public async Task Move_HappyPath_Completes_AndSwitchesActiveDatabase()
    {
        var vm = Create();
        EnterValidPostgresTarget(vm);
        _prober.ProbeAsync(Arg.Any<PostgresOptions>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(ConnectionProbeResult.Succeeded("16.2", null));
        await vm.CheckTargetCommand.ExecuteAsync(null);

        _filePicker.PickFolderAsync(Arg.Any<string>()).Returns(@"C:\backups");
        _backupService.BackupCsvArchiveAsync(@"C:\backups", Arg.Any<CancellationToken>(), Arg.Any<string?>(), Arg.Any<System.IProgress<string>?>())
            .Returns(@"C:\backups\safety.zip");
        _migrationService.MigrateAsync(Arg.Any<IDbContextFactory<BookDbContext>>(), Arg.Any<IDbContextFactory<BookDbContext>>(),
                Arg.Any<IIdentitySequenceResync>(), Arg.Any<System.IProgress<MigrationProgress>?>(), Arg.Any<CancellationToken>())
            .Returns(Completed(matching: true));
        _restartService.ConfirmRestartAsync(Arg.Any<string>()).Returns(true);

        await vm.MoveCommand.ExecuteAsync(null);

        Assert.False(vm.HasFailure);
        Assert.Contains(BookDB.Desktop.Localization.Resources.MoveLibrary_Complete, vm.LogText);
        _secretStore.Received().Set(Arg.Any<string>(), "secret");
        _restartService.Received().Restart();
    }

    [Fact]
    public async Task Move_Failure_ShowsPanel_AndDoesNotSwitch()
    {
        var vm = Create();
        EnterValidPostgresTarget(vm);
        _prober.ProbeAsync(Arg.Any<PostgresOptions>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(ConnectionProbeResult.Succeeded("16.2", null));
        await vm.CheckTargetCommand.ExecuteAsync(null);

        _filePicker.PickFolderAsync(Arg.Any<string>()).Returns(@"C:\backups");
        _backupService.BackupCsvArchiveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>(), Arg.Any<string?>(), Arg.Any<System.IProgress<string>?>())
            .Returns(@"C:\backups\safety.zip");
        _migrationService.MigrateAsync(Arg.Any<IDbContextFactory<BookDbContext>>(), Arg.Any<IDbContextFactory<BookDbContext>>(),
                Arg.Any<IIdentitySequenceResync>(), Arg.Any<System.IProgress<MigrationProgress>?>(), Arg.Any<CancellationToken>())
            .Returns(new MigrationResult(MigrationOutcome.Failed, [], MigrationTable.BookImage, "server gone"));

        await vm.MoveCommand.ExecuteAsync(null);

        Assert.True(vm.HasFailure);
        Assert.Contains("safety.zip", vm.FailureMessage);
        _restartService.DidNotReceive().Restart();
    }

    [Fact]
    public async Task Move_CountMismatch_BlocksSwitch()
    {
        var vm = Create();
        EnterValidPostgresTarget(vm);
        _prober.ProbeAsync(Arg.Any<PostgresOptions>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(ConnectionProbeResult.Succeeded("16.2", null));
        await vm.CheckTargetCommand.ExecuteAsync(null);

        _filePicker.PickFolderAsync(Arg.Any<string>()).Returns(@"C:\backups");
        _backupService.BackupCsvArchiveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>(), Arg.Any<string?>(), Arg.Any<System.IProgress<string>?>())
            .Returns(@"C:\backups\safety.zip");
        _migrationService.MigrateAsync(Arg.Any<IDbContextFactory<BookDbContext>>(), Arg.Any<IDbContextFactory<BookDbContext>>(),
                Arg.Any<IIdentitySequenceResync>(), Arg.Any<System.IProgress<MigrationProgress>?>(), Arg.Any<CancellationToken>())
            .Returns(Completed(matching: false));

        await vm.MoveCommand.ExecuteAsync(null);

        Assert.Contains(BookDB.Desktop.Localization.Resources.MoveLibrary_CountMismatch, vm.LogText);
        _restartService.DidNotReceive().Restart();
    }

    [Fact]
    public async Task Move_TargetHasData_BacksUpTargetBeforeMigrating()
    {
        var vm = Create();
        EnterValidPostgresTarget(vm);
        _prober.ProbeAsync(Arg.Any<PostgresOptions>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(ConnectionProbeResult.Succeeded("16.2", 42)); // target already holds data
        await vm.CheckTargetCommand.ExecuteAsync(null);
        vm.AcknowledgeReplace = true;

        _filePicker.PickFolderAsync(Arg.Any<string>()).Returns(@"C:\backups");
        _backupService.BackupCsvArchiveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>(), Arg.Any<string?>(), Arg.Any<System.IProgress<string>?>())
            .Returns(@"C:\backups\source.zip");
        _target.Backup.BackupCsvArchiveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>(), Arg.Any<string?>(), Arg.Any<System.IProgress<string>?>())
            .Returns(@"C:\backups\target.zip");
        _migrationService.MigrateAsync(Arg.Any<IDbContextFactory<BookDbContext>>(), Arg.Any<IDbContextFactory<BookDbContext>>(),
                Arg.Any<IIdentitySequenceResync>(), Arg.Any<System.IProgress<MigrationProgress>?>(), Arg.Any<CancellationToken>())
            .Returns(Completed(matching: true));
        _restartService.ConfirmRestartAsync(Arg.Any<string>()).Returns(true);

        await vm.MoveCommand.ExecuteAsync(null);

        await _target.Backup.Received().BackupCsvArchiveAsync(@"C:\backups", Arg.Any<CancellationToken>(), Arg.Any<string?>(), Arg.Any<System.IProgress<string>?>());
        Assert.False(vm.HasFailure);
    }

    [Fact]
    public async Task Move_TargetEmpty_DoesNotBackUpTarget()
    {
        var vm = Create();
        EnterValidPostgresTarget(vm);
        _prober.ProbeAsync(Arg.Any<PostgresOptions>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(ConnectionProbeResult.Succeeded("16.2", null)); // empty target — nothing to back up
        await vm.CheckTargetCommand.ExecuteAsync(null);

        _filePicker.PickFolderAsync(Arg.Any<string>()).Returns(@"C:\backups");
        _backupService.BackupCsvArchiveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>(), Arg.Any<string?>(), Arg.Any<System.IProgress<string>?>())
            .Returns(@"C:\backups\source.zip");
        _migrationService.MigrateAsync(Arg.Any<IDbContextFactory<BookDbContext>>(), Arg.Any<IDbContextFactory<BookDbContext>>(),
                Arg.Any<IIdentitySequenceResync>(), Arg.Any<System.IProgress<MigrationProgress>?>(), Arg.Any<CancellationToken>())
            .Returns(Completed(matching: true));
        _restartService.ConfirmRestartAsync(Arg.Any<string>()).Returns(true);

        await vm.MoveCommand.ExecuteAsync(null);

        await _target.Backup.DidNotReceive().BackupCsvArchiveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>(), Arg.Any<string?>(), Arg.Any<System.IProgress<string>?>());
    }

    [Fact]
    public async Task Move_FolderCancelled_DoesNothing()
    {
        var vm = Create();
        EnterValidPostgresTarget(vm);
        _prober.ProbeAsync(Arg.Any<PostgresOptions>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(ConnectionProbeResult.Succeeded("16.2", null));
        await vm.CheckTargetCommand.ExecuteAsync(null);

        _filePicker.PickFolderAsync(Arg.Any<string>()).Returns((string?)null);

        await vm.MoveCommand.ExecuteAsync(null);

        Assert.False(vm.IsRunning);
        await _backupService.DidNotReceive().BackupCsvArchiveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>(), Arg.Any<string?>(), Arg.Any<System.IProgress<string>?>());
    }
}
