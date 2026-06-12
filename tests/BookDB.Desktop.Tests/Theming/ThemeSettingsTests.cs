using BookDB.Desktop.Theming;
using Xunit;

namespace BookDB.Desktop.Tests.Theming;

public sealed class ThemeSettingsTests
{
    [Theory]
    [InlineData(ThemeFlavour.Default)]
    [InlineData(ThemeFlavour.Vibrant)]
    [InlineData(ThemeFlavour.HighContrast)]
    [InlineData(ThemeFlavour.Dark)]
    public void RoundTrips_EveryFlavour(ThemeFlavour flavour)
    {
        var stored = ThemeSettings.ToStorageValue(flavour);
        Assert.Equal(flavour, ThemeSettings.Parse(stored));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("nonsense")]
    [InlineData("DROP TABLE Settings;--")]
    public void Parse_FallsBackToDefault_ForMissingOrUnrecognisedValues(string? stored)
    {
        Assert.Equal(ThemeFlavour.Default, ThemeSettings.Parse(stored));
    }

    [Fact]
    public void Parse_IsCaseInsensitive()
    {
        Assert.Equal(ThemeFlavour.Vibrant, ThemeSettings.Parse("vibrant"));
    }

    [Fact]
    public void Parse_RejectsNumericStrings()
    {
        // A stray "1" must not silently resolve to a flavour by ordinal.
        Assert.Equal(ThemeFlavour.Default, ThemeSettings.Parse("1"));
    }

    [Fact]
    public void Key_IsTheExpectedSettingsKey()
    {
        Assert.Equal("UiTheme", ThemeSettings.Key);
    }
}
