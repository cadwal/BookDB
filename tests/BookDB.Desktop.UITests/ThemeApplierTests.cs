using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using BookDB.Desktop.Messages;
using BookDB.Desktop.Theming;
using CommunityToolkit.Mvvm.Messaging;
using Xunit;

namespace BookDB.Desktop.UITests;

/// <summary>
/// Runtime theme switching. The applier is re-entrant: switching flavours removes the previous override before
/// merging the next (no stacking), flips the theme variant both into and out of Dark, restores the pinned accent on
/// return to Default, and raises a ThemeAppliedMessage every time so imperative colour consumers can re-resolve.
/// Every test restores the Default flavour before returning — the headless app is shared across the session.
/// </summary>
public class ThemeApplierTests : HeadlessTest
{
    [Fact]
    public async Task Apply_IsReentrant_SwapsVariantAndNeverStacksOverrides()
    {
        await RunUi(() =>
        {
            var app = Application.Current!;
            try
            {
                ThemeApplier.Apply(ThemeFlavour.Dark);
                Assert.Equal(ThemeVariant.Dark, app.RequestedThemeVariant);
                Assert.Equal(1, OverrideIncludeCount());

                // Dark → Vibrant: the Dark override is removed (not stacked) and the variant returns to Light.
                ThemeApplier.Apply(ThemeFlavour.Vibrant);
                Assert.Equal(ThemeVariant.Light, app.RequestedThemeVariant);
                Assert.Equal(1, OverrideIncludeCount());

                // Vibrant → Default: the override is removed entirely, base palette stands.
                ThemeApplier.Apply(ThemeFlavour.Default);
                Assert.Equal(ThemeVariant.Light, app.RequestedThemeVariant);
                Assert.Equal(0, OverrideIncludeCount());
            }
            finally
            {
                ThemeApplier.Apply(ThemeFlavour.Default);
            }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task Apply_ReturningToDefault_RestoresThePinnedAccent()
    {
        await RunUi(() =>
        {
            var fluent = Application.Current!.Styles.OfType<FluentTheme>().First();
            try
            {
                ThemeApplier.Apply(ThemeFlavour.Vibrant);
                // Vibrant re-points the accent at its own selection blue.
                Assert.Equal(Color.Parse("#2563eb"), fluent.Palettes[ThemeVariant.Light].Accent);

                ThemeApplier.Apply(ThemeFlavour.Default);
                // Back to the base #1976d2 pinned in App.axaml.
                Assert.Equal(Color.Parse("#1976d2"), fluent.Palettes[ThemeVariant.Light].Accent);
            }
            finally
            {
                ThemeApplier.Apply(ThemeFlavour.Default);
            }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task Apply_RaisesThemeAppliedMessage_WithTheFlavour()
    {
        await RunUi(() =>
        {
            ThemeFlavour? received = null;
            var token = new object();
            WeakReferenceMessenger.Default.Register<ThemeAppliedMessage>(token, (_, m) => received = m.Value);
            try
            {
                ThemeApplier.Apply(ThemeFlavour.HighContrast);
                Assert.Equal(ThemeFlavour.HighContrast, received);
            }
            finally
            {
                WeakReferenceMessenger.Default.Unregister<ThemeAppliedMessage>(token);
                ThemeApplier.Apply(ThemeFlavour.Default);
            }
            return Task.CompletedTask;
        });
    }

    // The three override flavours the applier can merge; App.axaml always carries Default.axaml as the base, so it is
    // deliberately excluded — this counts only the applier's own override.
    private static int OverrideIncludeCount() =>
        Application.Current!.Resources.MergedDictionaries
            .OfType<ResourceInclude>()
            .Count(d => d.Source is { } s
                && (s.AbsoluteUri.EndsWith("/Themes/Vibrant.axaml")
                    || s.AbsoluteUri.EndsWith("/Themes/HighContrast.axaml")
                    || s.AbsoluteUri.EndsWith("/Themes/Dark.axaml")));
}
