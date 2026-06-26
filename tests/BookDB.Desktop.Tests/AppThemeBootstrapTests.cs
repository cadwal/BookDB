using BookDB.Desktop;
using BookDB.Desktop.Theming;
using BookDB.Models;
using Xunit;

namespace BookDB.Desktop.Tests;

public sealed class AppThemeBootstrapTests
{
    [Theory]
    [InlineData("Default", ThemeFlavour.Default)]
    [InlineData("Vibrant", ThemeFlavour.Vibrant)]
    [InlineData("HighContrast", ThemeFlavour.HighContrast)]
    [InlineData("Dark", ThemeFlavour.Dark)]
    public void ApplyThemeBootstrap_FlavourInConfig_ReturnsThatFlavour(string stored, ThemeFlavour expected)
        => Assert.Equal(expected, AppHost.ApplyThemeBootstrap(new BootstrapConfig { UiTheme = stored }));

    [Fact]
    public void ApplyThemeBootstrap_NullValue_ReturnsDefault()
        => Assert.Equal(ThemeFlavour.Default, AppHost.ApplyThemeBootstrap(new BootstrapConfig { UiTheme = null }));

    [Fact]
    public void ApplyThemeBootstrap_InvalidValue_ReturnsDefaultWithoutThrowing()
    {
        var ex = Record.Exception(() =>
            Assert.Equal(
                ThemeFlavour.Default,
                AppHost.ApplyThemeBootstrap(new BootstrapConfig { UiTheme = "DROP TABLE Settings;--" })));

        Assert.Null(ex);
    }
}
