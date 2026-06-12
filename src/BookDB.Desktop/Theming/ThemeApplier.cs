using System;
using System.Linq;
using Avalonia;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Serilog;

namespace BookDB.Desktop.Theming;

/// <summary>
/// Applies a colour flavour to the running application at startup. App.axaml already loads the base
/// palette and the pinned accent, so the Default flavour needs nothing; other flavours merge their
/// brush-override dictionary on top of the base (overrides win) and then re-point the FluentTheme accent
/// at the flavour's blue so native accent controls stay in step. Applying is best-effort — a missing or
/// broken flavour dictionary degrades to the base look instead of blocking startup. The flavour is fixed
/// for the session (changing it requires a restart), so this runs once before any window loads.
/// </summary>
public static class ThemeApplier
{
    public static void Apply(ThemeFlavour flavour)
    {
        if (Application.Current is not { } app) return;   // no UI (e.g. unit tests) — nothing to theme
        if (flavour == ThemeFlavour.Default) return;      // base palette + pinned accent already loaded

        try
        {
            // Dark flips the whole app to the Dark theme variant, so Fluent's built-in dark theme drives
            // native controls; our Dark.axaml overrides our own brushes on top.
            if (flavour == ThemeFlavour.Dark)
                app.RequestedThemeVariant = ThemeVariant.Dark;

            var uri = new Uri($"avares://BookDB.Desktop/Styles/Themes/{flavour}.axaml");
            app.Resources.MergedDictionaries.Add(new ResourceInclude(uri) { Source = uri });
            SyncFluentAccent(app);
        }
        catch (Exception ex)
        {
            Log.Warning("ThemeApplier: could not apply flavour {Flavour}; using base palette: {Error}",
                flavour, ex.Message);
        }
    }

    // Native Fluent accent controls read the FluentTheme palette, not our brushes; keep them in step by
    // pointing the (runtime-updatable) accent at the flavour's selection blue once it has been merged.
    // We use BrushRowSelected (the accent-as-background blue that white text sits on) rather than
    // BrushPrimaryBlue, which in Dark is a lighter text/link blue that white would fail against. In the
    // light flavours the two keys are the same value, so this is a no-op there.
    private static void SyncFluentAccent(Application app)
    {
        if (app.Styles.OfType<FluentTheme>().FirstOrDefault() is not { } fluent) return;
        if (!fluent.Palettes.TryGetValue(app.ActualThemeVariant, out var palette)) return;
        if (app.TryGetResource("BrushRowSelected", app.ActualThemeVariant, out var res)
            && res is ISolidColorBrush brush)
        {
            palette.Accent = brush.Color;
        }
    }
}
