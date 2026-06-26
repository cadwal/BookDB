using BookDB.Desktop.ViewModels;
using BookDB.Models.Interfaces;
using NSubstitute;
using Xunit;

namespace BookDB.Desktop.Tests.ViewModels;

public sealed class BackupFormatDialogViewModelTests
{
    private static BackupFormatDialogViewModel Create(bool supportsFileBackup, string configDefault, string folder = @"C:\backups")
        => new(supportsFileBackup, configDefault, folder, Substitute.For<IFilePickerService>());

    [Fact]
    public void Local_DefaultsToConfiguredSqlite()
    {
        var vm = Create(supportsFileBackup: true, configDefault: "SQLite");
        Assert.True(vm.SqliteSelected);
        Assert.False(vm.CsvSelected);
        Assert.False(vm.ShowRemoteFallbackNote);
    }

    [Fact]
    public void Local_DefaultsToConfiguredCsv()
    {
        var vm = Create(supportsFileBackup: true, configDefault: "CsvArchive");
        Assert.True(vm.CsvSelected);
        Assert.False(vm.SqliteSelected);
    }

    [Fact]
    public void Remote_PreselectsCsv_EvenWhenConfigSaysSqlite()
    {
        var vm = Create(supportsFileBackup: false, configDefault: "SQLite");
        Assert.True(vm.CsvSelected);
        Assert.False(vm.SqliteSelected);
    }

    [Fact]
    public void Remote_ShowsFallbackNote_Immediately()
    {
        var vm = Create(supportsFileBackup: false, configDefault: "SQLite");
        Assert.True(vm.ShowRemoteFallbackNote);
    }

    [Fact]
    public void DefaultFolder_IsPrefilled()
    {
        var vm = Create(supportsFileBackup: true, configDefault: "SQLite", folder: @"D:\my-backups");
        Assert.Equal(@"D:\my-backups", vm.DestinationFolder);
    }

    [Fact]
    public void Confirm_DisabledWhenNoFolder()
    {
        var vm = Create(supportsFileBackup: true, configDefault: "SQLite", folder: "");
        Assert.False(vm.ConfirmCommand.CanExecute(null));
    }

    [Fact]
    public void Confirm_ReturnsSelectedFormat()
    {
        var vm = Create(supportsFileBackup: true, configDefault: "SQLite");
        bool? closed = null;
        vm.CloseDialog = ok => closed = ok;

        // Mirror the radio group: picking CSV clears SQLite.
        vm.SqliteSelected = false;
        vm.CsvSelected = true;
        vm.ConfirmCommand.Execute(null);

        Assert.Equal("CsvArchive", vm.Result);
        Assert.True(closed);
    }

    [Fact]
    public void Cancel_LeavesResultNull()
    {
        var vm = Create(supportsFileBackup: true, configDefault: "SQLite");
        vm.CancelCommand.Execute(null);
        Assert.Null(vm.Result);
    }
}
