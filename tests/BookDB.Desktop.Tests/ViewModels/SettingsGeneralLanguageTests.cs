using System.Linq;
using System.Threading.Tasks;
using BookDB.Desktop.Tests.Helpers;
using BookDB.Desktop.ViewModels;
using BookDB.Logic.Services;
using Xunit;

namespace BookDB.Desktop.Tests.ViewModels;

public sealed class SettingsGeneralLanguageTests
{
    [Fact]
    public async Task LoadAsync_PopulatesAvailableLanguages_WithEnglishAlwaysFirst()
    {
        // AvailableLanguages always contains "en" as the first entry
        var settingsService = new TestLookupServiceFactory.NullSettingsService();
        using var factory = new TestLookupServiceFactory();
        var vm = new SettingsGeneralTabViewModel(settingsService, factory.LookupService);

        await vm.LoadAsync(TestContext.Current.CancellationToken);

        Assert.NotEmpty(vm.AvailableLanguages);
        Assert.Contains(vm.AvailableLanguages, l => l.CultureName == "en");
        Assert.Equal("en", vm.AvailableLanguages[0].CultureName);
    }

    [Fact]
    public async Task LoadAsync_WithNullStoredLanguage_SelectsEnglishByDefault()
    {
        // Null stored language => SelectedLanguage defaults to "en"
        var settingsService = new TestLookupServiceFactory.NullSettingsService();
        using var factory = new TestLookupServiceFactory();
        var vm = new SettingsGeneralTabViewModel(settingsService, factory.LookupService);

        await vm.LoadAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(vm.SelectedLanguage);
        Assert.Equal("en", vm.SelectedLanguage!.CultureName);
    }

    [Fact]
    public async Task SaveAsync_PersistsSelectedLanguageViaCultureName()
    {
        // SaveAsync writes SelectedLanguage.CultureName to ISettingsService key "Language"
        var settingsStore = new InMemorySettingsService();
        using var factory = new TestLookupServiceFactory();
        var vm = new SettingsGeneralTabViewModel(settingsStore, factory.LookupService);

        await vm.LoadAsync(TestContext.Current.CancellationToken);

        vm.SelectedLanguage = vm.AvailableLanguages.First(l => l.CultureName == "en");
        await vm.SaveAsync(TestContext.Current.CancellationToken);

        var stored = await settingsStore.GetAsync("Language", TestContext.Current.CancellationToken);
        Assert.Equal("en", stored);
    }

    // Minimal in-memory ISettingsService for roundtrip tests
    private sealed class InMemorySettingsService : ISettingsService
    {
        private readonly System.Collections.Generic.Dictionary<string, string?> _store = [];

        public System.Threading.Tasks.Task<string?> GetAsync(string key, System.Threading.CancellationToken ct = default)
            => System.Threading.Tasks.Task.FromResult(_store.TryGetValue(key, out var v) ? v : null);

        public System.Threading.Tasks.Task SetAsync(string key, string? value, System.Threading.CancellationToken ct = default)
        {
            _store[key] = value;
            return System.Threading.Tasks.Task.CompletedTask;
        }
    }
}
