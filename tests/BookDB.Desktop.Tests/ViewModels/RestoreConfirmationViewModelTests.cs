using BookDB.Desktop.Services;
using BookDB.Desktop.ViewModels;
using BookDB.Models;
using NSubstitute;
using Xunit;

namespace BookDB.Desktop.Tests.ViewModels;

public sealed class RestoreConfirmationViewModelTests
{
    private readonly IBootstrapConfigService _config = Substitute.For<IBootstrapConfigService>();
    private readonly IApplicationRestartService _restart = Substitute.For<IApplicationRestartService>();

    private RestoreConfirmationViewModel Create(BootstrapConfig archived, BootstrapConfig current)
    {
        _config.Load().Returns(current);
        return new RestoreConfirmationViewModel(archived, _config, _restart);
    }

    // Captures what the Apply/KeepCurrent commands mutate by replaying the Update action onto a probe config.
    private BootstrapConfig CaptureUpdate(BootstrapConfig start)
    {
        BootstrapConfig probe = start;
        _config.When(c => c.Update(Arg.Any<System.Action<BootstrapConfig>>()))
            .Do(call => call.Arg<System.Action<BootstrapConfig>>()(probe));
        return probe;
    }

    [Fact]
    public void HasBackendChange_True_WhenArchivedBackendDiffers()
    {
        var vm = Create(
            archived: new BootstrapConfig { Backend = "PostgreSql" },
            current: new BootstrapConfig { Backend = "Sqlite" });

        Assert.True(vm.HasBackendChange);
    }

    [Fact]
    public void HasBackendChange_False_WhenSameBackendAndConnection()
    {
        var vm = Create(
            archived: new BootstrapConfig { Backend = "Sqlite" },
            current: new BootstrapConfig { Backend = "Sqlite" });

        Assert.False(vm.HasBackendChange);
    }

    [Fact]
    public void Apply_AdoptsArchivedBackendAndPreferences_ThenRestarts()
    {
        var archived = new BootstrapConfig
        {
            Backend = "PostgreSql",
            Postgres = new PostgresOptions { Host = "db", Username = "u", Database = "bookdb" },
            Language = "sv",
            UiTheme = "Dark",
        };
        var probe = CaptureUpdate(new BootstrapConfig { Backend = "Sqlite", Language = "en" });
        var vm = Create(archived, new BootstrapConfig { Backend = "Sqlite" });

        vm.ApplyCommand.Execute(null);

        Assert.Equal("PostgreSql", probe.Backend);
        Assert.Equal("db", probe.Postgres.Host);
        Assert.Equal("sv", probe.Language);
        Assert.Equal("Dark", probe.UiTheme);
        _restart.Received().Restart();
    }

    [Fact]
    public void HasBackendChange_True_WhenMySqlArchive_DiffersFromCurrent()
    {
        var vm = Create(
            archived: new BootstrapConfig { Backend = "MySql" },
            current: new BootstrapConfig { Backend = "Sqlite" });

        Assert.True(vm.HasBackendChange);
    }

    [Fact]
    public void HasBackendChange_True_WhenSameMySqlBackendButDifferentConnection()
    {
        var vm = Create(
            archived: new BootstrapConfig { Backend = "MySql", MySql = new MySqlOptions { Host = "a", Username = "u", Database = "bookdb" } },
            current: new BootstrapConfig { Backend = "MySql", MySql = new MySqlOptions { Host = "b", Username = "u", Database = "bookdb" } });

        Assert.True(vm.HasBackendChange);
    }

    [Fact]
    public void ArchivedBackendName_NamesMySql_ForMySqlArchive()
    {
        var vm = Create(
            archived: new BootstrapConfig { Backend = "MySql" },
            current: new BootstrapConfig { Backend = "Sqlite" });

        Assert.Equal(BookDB.Desktop.Localization.Resources.Settings_Database_Backend_MySql, vm.ArchivedBackendName);
    }

    [Fact]
    public void Apply_AdoptsArchivedMySqlConnection_ForMySqlArchive()
    {
        var archived = new BootstrapConfig
        {
            Backend = "MySql",
            MySql = new MySqlOptions { Host = "maria", Username = "u", Database = "bookdb" },
        };
        var probe = CaptureUpdate(new BootstrapConfig { Backend = "Sqlite" });
        var vm = Create(archived, new BootstrapConfig { Backend = "Sqlite" });

        vm.ApplyCommand.Execute(null);

        Assert.Equal("MySql", probe.Backend);
        Assert.Equal("maria", probe.MySql.Host);
        _restart.Received().Restart();
    }

    [Fact]
    public void KeepCurrent_AppliesPreferencesOnly_KeepsBackend_ThenRestarts()
    {
        var archived = new BootstrapConfig { Backend = "PostgreSql", Language = "fr" };
        var probe = CaptureUpdate(new BootstrapConfig { Backend = "Sqlite", Language = "en" });
        var vm = Create(archived, new BootstrapConfig { Backend = "Sqlite" });

        vm.KeepCurrentCommand.Execute(null);

        Assert.Equal("Sqlite", probe.Backend);   // current backend kept
        Assert.Equal("fr", probe.Language);       // preference still applied
        _restart.Received().Restart();
    }
}
