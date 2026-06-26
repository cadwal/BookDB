using System.Linq;
using System.Threading.Tasks;
using BookDB.Desktop.Tests.Helpers;
using BookDB.Desktop.ViewModels;
using Xunit;

namespace BookDB.Desktop.Tests.ViewModels;

public sealed class SettingsGeneralLanguageTests
{
    [Fact]
    public async Task LoadAsync_PopulatesAvailableLanguages_WithEnglishAlwaysFirst()
    {
        var settingsService = new TestLookupServiceFactory.NullSettingsService();
        var bootstrapConfig = new InMemoryBootstrapConfigService();
        using var factory = new TestLookupServiceFactory();
        var vm = new SettingsGeneralTabViewModel(settingsService, factory.LookupService, bootstrapConfig);

        await vm.LoadAsync(TestContext.Current.CancellationToken);

        Assert.NotEmpty(vm.AvailableLanguages);
        Assert.Contains(vm.AvailableLanguages, l => l.CultureName == "en");
        Assert.Equal("en", vm.AvailableLanguages[0].CultureName);
    }

    [Fact]
    public async Task LoadAsync_WithNoStoredLanguage_SelectsEnglishByDefault()
    {
        var settingsService = new TestLookupServiceFactory.NullSettingsService();
        var bootstrapConfig = new InMemoryBootstrapConfigService();   // Language null by default
        using var factory = new TestLookupServiceFactory();
        var vm = new SettingsGeneralTabViewModel(settingsService, factory.LookupService, bootstrapConfig);

        await vm.LoadAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(vm.SelectedLanguage);
        Assert.Equal("en", vm.SelectedLanguage!.CultureName);
    }

    [Fact]
    public async Task LanguageChanged_FalseAfterLoad_TrueAfterSwitching()
    {
        var settingsService = new TestLookupServiceFactory.NullSettingsService();
        var bootstrapConfig = new InMemoryBootstrapConfigService();
        using var factory = new TestLookupServiceFactory();
        var vm = new SettingsGeneralTabViewModel(settingsService, factory.LookupService, bootstrapConfig);
        await vm.LoadAsync(TestContext.Current.CancellationToken);

        Assert.False(vm.LanguageChanged);

        vm.SelectedLanguage = new LanguageOption("de", "Deutsch");

        Assert.True(vm.LanguageChanged);
    }

    [Fact]
    public async Task SaveAsync_PersistsSelectedLanguageToConfig()
    {
        var settingsService = new TestLookupServiceFactory.NullSettingsService();
        var bootstrapConfig = new InMemoryBootstrapConfigService();
        using var factory = new TestLookupServiceFactory();
        var vm = new SettingsGeneralTabViewModel(settingsService, factory.LookupService, bootstrapConfig);

        await vm.LoadAsync(TestContext.Current.CancellationToken);
        vm.SelectedLanguage = vm.AvailableLanguages.First(l => l.CultureName == "en");
        await vm.SaveAsync(TestContext.Current.CancellationToken);

        Assert.Equal("en", bootstrapConfig.Config.Language);
    }
}
