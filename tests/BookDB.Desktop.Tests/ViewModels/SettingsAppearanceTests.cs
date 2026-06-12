using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BookDB.Desktop.Theming;
using BookDB.Desktop.ViewModels;
using BookDB.Logic.Services;
using Xunit;

namespace BookDB.Desktop.Tests.ViewModels;

public sealed class SettingsAppearanceTests
{
    [Fact]
    public void AvailableFlavours_ContainsEveryFlavourInOrder()
    {
        var vm = new SettingsAppearanceTabViewModel(new InMemorySettingsService());

        Assert.Equal(
            new[] { ThemeFlavour.Default, ThemeFlavour.Vibrant, ThemeFlavour.HighContrast, ThemeFlavour.Dark },
            vm.AvailableFlavours.Select(f => f.Flavour));
    }

    [Fact]
    public async Task LoadAsync_WithNoStoredValue_SelectsDefault()
    {
        var vm = new SettingsAppearanceTabViewModel(new InMemorySettingsService());

        await vm.LoadAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(vm.SelectedFlavour);
        Assert.Equal(ThemeFlavour.Default, vm.SelectedFlavour!.Flavour);
    }

    [Fact]
    public async Task LoadAsync_WithStoredFlavour_SelectsIt()
    {
        var store = new InMemorySettingsService();
        await store.SetAsync(ThemeSettings.Key, "Vibrant", TestContext.Current.CancellationToken);
        var vm = new SettingsAppearanceTabViewModel(store);

        await vm.LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ThemeFlavour.Vibrant, vm.SelectedFlavour!.Flavour);
    }

    [Fact]
    public async Task SaveAsync_PersistsTheSelectedFlavourName()
    {
        var store = new InMemorySettingsService();
        var vm = new SettingsAppearanceTabViewModel(store);
        await vm.LoadAsync(TestContext.Current.CancellationToken);

        vm.SelectedFlavour = vm.AvailableFlavours.First(f => f.Flavour == ThemeFlavour.HighContrast);
        await vm.SaveAsync(TestContext.Current.CancellationToken);

        Assert.Equal("HighContrast", await store.GetAsync(ThemeSettings.Key, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SaveThenLoad_RoundTripsTheFlavour()
    {
        var store = new InMemorySettingsService();
        var save = new SettingsAppearanceTabViewModel(store);
        await save.LoadAsync(TestContext.Current.CancellationToken);
        save.SelectedFlavour = save.AvailableFlavours.First(f => f.Flavour == ThemeFlavour.Vibrant);
        await save.SaveAsync(TestContext.Current.CancellationToken);

        var load = new SettingsAppearanceTabViewModel(store);
        await load.LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ThemeFlavour.Vibrant, load.SelectedFlavour!.Flavour);
    }

    private sealed class InMemorySettingsService : ISettingsService
    {
        private readonly Dictionary<string, string?> _store = [];

        public Task<string?> GetAsync(string key, CancellationToken ct = default)
            => Task.FromResult(_store.TryGetValue(key, out var v) ? v : null);

        public Task SetAsync(string key, string? value, CancellationToken ct = default)
        {
            _store[key] = value;
            return Task.CompletedTask;
        }
    }
}
