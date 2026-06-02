using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BookDB.Desktop.Tests.Helpers;
using BookDB.Desktop.ViewModels;
using BookDB.Logic.Services;
using Xunit;

namespace BookDB.Desktop.Tests.ViewModels;

public sealed class SettingsAdvancedLoggingTests
{
    // Minimal in-memory ISettingsService for roundtrip tests
    private sealed class InMemorySettingsService : ISettingsService
    {
        private readonly System.Collections.Generic.Dictionary<string, string?> _store = [];

        public System.Threading.Tasks.Task<string?> GetAsync(string key, CancellationToken ct = default)
            => System.Threading.Tasks.Task.FromResult(_store.TryGetValue(key, out var v) ? v : null);

        public System.Threading.Tasks.Task SetAsync(string key, string? value, CancellationToken ct = default)
        {
            _store[key] = value;
            return System.Threading.Tasks.Task.CompletedTask;
        }
    }

    [Fact]
    public async Task LoadAsync_WithNoStoredLogLevel_DefaultsSelectedLogLevelToNormal()
    {
        // LoadAsync defaults SelectedLogLevel to "Normal" when nothing stored
        var settingsService = new TestLookupServiceFactory.NullSettingsService();
        var filePickerService = new TestLookupServiceFactory.NullFilePickerService();
        var vm = new SettingsAdvancedTabViewModel(settingsService, filePickerService);

        await vm.LoadAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(vm.SelectedLogLevel);
        Assert.Equal("Normal", vm.SelectedLogLevel!.Value);
    }

    [Fact]
    public async Task LoadAsync_WithStoredVerboseValue_SelectsVerboseLogLevel()
    {
        // LoadAsync reads stored "Verbose" value
        var settingsService = new InMemorySettingsService();
        await settingsService.SetAsync("LogLevel", "Verbose", TestContext.Current.CancellationToken);

        var filePickerService = new TestLookupServiceFactory.NullFilePickerService();
        var vm = new SettingsAdvancedTabViewModel(settingsService, filePickerService);

        await vm.LoadAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(vm.SelectedLogLevel);
        Assert.Equal("Verbose", vm.SelectedLogLevel!.Value);
    }

    [Fact]
    public async Task SaveAsync_PersistsSelectedLogLevelValueUnderLogLevelKey()
    {
        // SaveAsync persists SelectedLogLevel.Value under key "LogLevel"
        var settingsStore = new InMemorySettingsService();
        var filePickerService = new TestLookupServiceFactory.NullFilePickerService();
        var vm = new SettingsAdvancedTabViewModel(settingsStore, filePickerService);

        await vm.LoadAsync(TestContext.Current.CancellationToken);

        // Select "Verbose" from available options
        var verboseOption = vm.AvailableLogLevels.FirstOrDefault(l => l.Value == "Verbose");
        Assert.NotNull(verboseOption);
        vm.SelectedLogLevel = verboseOption;

        await vm.SaveAsync(TestContext.Current.CancellationToken);

        var stored = await settingsStore.GetAsync("LogLevel", TestContext.Current.CancellationToken);
        Assert.Equal("Verbose", stored);
    }
}
