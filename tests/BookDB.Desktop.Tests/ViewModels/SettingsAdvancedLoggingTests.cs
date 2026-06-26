using System.Linq;
using System.Threading.Tasks;
using BookDB.Desktop.Services;
using BookDB.Desktop.Tests.Helpers;
using BookDB.Desktop.ViewModels;
using Xunit;

namespace BookDB.Desktop.Tests.ViewModels;

public sealed class SettingsAdvancedLoggingTests
{
    private static SettingsAdvancedTabViewModel CreateVm(IBootstrapConfigService bootstrapConfig)
        => new(
            new TestLookupServiceFactory.NullSettingsService(),
            new TestLookupServiceFactory.NullFilePickerService(),
            bootstrapConfig,
            supportsFileBackup: true);

    [Fact]
    public async Task LoadAsync_WithNoStoredLogLevel_DefaultsSelectedLogLevelToNormal()
    {
        var vm = CreateVm(new InMemoryBootstrapConfigService());

        await vm.LoadAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(vm.SelectedLogLevel);
        Assert.Equal("Normal", vm.SelectedLogLevel!.Value);
    }

    [Fact]
    public async Task LoadAsync_WithStoredVerboseValue_SelectsVerboseLogLevel()
    {
        var bootstrapConfig = new InMemoryBootstrapConfigService();
        bootstrapConfig.Config.LogLevel = "Verbose";
        var vm = CreateVm(bootstrapConfig);

        await vm.LoadAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(vm.SelectedLogLevel);
        Assert.Equal("Verbose", vm.SelectedLogLevel!.Value);
    }

    [Fact]
    public async Task LogLevelChanged_FalseAfterLoad_TrueAfterSwitching()
    {
        var vm = CreateVm(new InMemoryBootstrapConfigService());
        await vm.LoadAsync(TestContext.Current.CancellationToken);

        Assert.False(vm.LogLevelChanged);

        vm.SelectedLogLevel = vm.AvailableLogLevels.First(l => l.Value == "Verbose");

        Assert.True(vm.LogLevelChanged);
    }

    [Fact]
    public async Task SaveAsync_PersistsSelectedLogLevelToConfig()
    {
        var bootstrapConfig = new InMemoryBootstrapConfigService();
        var vm = CreateVm(bootstrapConfig);

        await vm.LoadAsync(TestContext.Current.CancellationToken);
        vm.SelectedLogLevel = vm.AvailableLogLevels.First(l => l.Value == "Verbose");
        await vm.SaveAsync(TestContext.Current.CancellationToken);

        Assert.Equal("Verbose", bootstrapConfig.Config.LogLevel);
    }
}
