using System.Linq;
using System.Threading.Tasks;
using BookDB.Desktop.Tests.Helpers;
using BookDB.Desktop.Theming;
using BookDB.Desktop.ViewModels;
using Xunit;

namespace BookDB.Desktop.Tests.ViewModels;

public sealed class SettingsAppearanceTests
{
    [Fact]
    public void AvailableFlavours_ContainsEveryFlavourInOrder()
    {
        var vm = new SettingsAppearanceTabViewModel(new InMemoryBootstrapConfigService());

        Assert.Equal(
            new[] { ThemeFlavour.Default, ThemeFlavour.Vibrant, ThemeFlavour.HighContrast, ThemeFlavour.Dark },
            vm.AvailableFlavours.Select(f => f.Flavour));
    }

    [Fact]
    public async Task LoadAsync_WithNoStoredValue_SelectsDefault()
    {
        var vm = new SettingsAppearanceTabViewModel(new InMemoryBootstrapConfigService());

        await vm.LoadAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(vm.SelectedFlavour);
        Assert.Equal(ThemeFlavour.Default, vm.SelectedFlavour!.Flavour);
    }

    [Fact]
    public async Task LoadAsync_WithStoredFlavour_SelectsIt()
    {
        var bootstrapConfig = new InMemoryBootstrapConfigService();
        bootstrapConfig.Config.UiTheme = "Vibrant";
        var vm = new SettingsAppearanceTabViewModel(bootstrapConfig);

        await vm.LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ThemeFlavour.Vibrant, vm.SelectedFlavour!.Flavour);
    }

    [Fact]
    public async Task SaveAsync_PersistsTheSelectedFlavourName()
    {
        var bootstrapConfig = new InMemoryBootstrapConfigService();
        var vm = new SettingsAppearanceTabViewModel(bootstrapConfig);
        await vm.LoadAsync(TestContext.Current.CancellationToken);

        vm.SelectedFlavour = vm.AvailableFlavours.First(f => f.Flavour == ThemeFlavour.HighContrast);
        await vm.SaveAsync(TestContext.Current.CancellationToken);

        Assert.Equal("HighContrast", bootstrapConfig.Config.UiTheme);
    }

    [Fact]
    public async Task ThemeChanged_FalseAfterLoad_TrueAfterPickingAnotherFlavour()
    {
        var vm = new SettingsAppearanceTabViewModel(new InMemoryBootstrapConfigService());
        await vm.LoadAsync(TestContext.Current.CancellationToken);

        Assert.False(vm.ThemeChanged);

        vm.SelectedFlavour = vm.AvailableFlavours.First(f => f.Flavour == ThemeFlavour.Dark);

        Assert.True(vm.ThemeChanged);
    }

    [Fact]
    public async Task SaveThenLoad_RoundTripsTheFlavour()
    {
        var bootstrapConfig = new InMemoryBootstrapConfigService();
        var save = new SettingsAppearanceTabViewModel(bootstrapConfig);
        await save.LoadAsync(TestContext.Current.CancellationToken);
        save.SelectedFlavour = save.AvailableFlavours.First(f => f.Flavour == ThemeFlavour.Vibrant);
        await save.SaveAsync(TestContext.Current.CancellationToken);

        var load = new SettingsAppearanceTabViewModel(bootstrapConfig);
        await load.LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ThemeFlavour.Vibrant, load.SelectedFlavour!.Flavour);
    }
}
