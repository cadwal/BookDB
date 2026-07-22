using System;
using System.Linq;
using Avalonia;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using BookDB.Desktop.Messages;
using CommunityToolkit.Mvvm.Messaging;
using Serilog;

namespace BookDB.Desktop.Theming;

/// <summary>
/// Applies a colour flavour to the running application. App.axaml loads the base palette and the pinned accent, so
/// the Default flavour needs no override dictionary; other flavours merge their brush-override dictionary on top of
/// the base (overrides win) and re-point the FluentTheme accent at the flavour's blue so native accent controls stay
/// in step. Applying is best-effort — a missing or broken flavour dictionary degrades to the base look instead of
/// throwing. It is re-entrant: called once at startup and again whenever the user saves a new flavour in Settings,
/// so each apply first removes the previously merged override, resets the theme variant both ways, and restores the
/// pinned accent when returning to Default. Every apply raises a <see cref="ThemeAppliedMessage"/> so imperative
/// colour consumers can re-resolve (a flavour swap that keeps the variant fires no <c>ActualThemeVariantChanged</c>).
/// </summary>
public static class ThemeApplier
{
    // The override dictionary merged by the last non-Default apply, tracked so the next apply can remove it before
    // adding another — otherwise switching flavours would stack dictionaries and never fully revert.
    private static ResourceInclude? _appliedFlavourInclude;

    public static void Apply(ThemeFlavour flavour)
    {
        if (Application.Current is not { } app) return;   // no UI (e.g. unit tests) — nothing to theme

        try
        {
            if (_appliedFlavourInclude is not null)
            {
                app.Resources.MergedDictionaries.Remove(_appliedFlavourInclude);
                _appliedFlavourInclude = null;
            }

            // Only the Dark flavour runs on the Dark variant; every other flavour is a Light-variant look. Setting
            // this both ways (not just Dark ⇒ Dark) is what lets a switch back out of Dark restore light chrome.
            var variant = flavour == ThemeFlavour.Dark ? ThemeVariant.Dark : ThemeVariant.Light;
            app.RequestedThemeVariant = variant;

            if (flavour != ThemeFlavour.Default)
            {
                var uri = new Uri($"avares://BookDB.Desktop/Styles/Themes/{flavour}.axaml");
                var include = new ResourceInclude(uri) { Source = uri };
                app.Resources.MergedDictionaries.Add(include);
                _appliedFlavourInclude = include;
            }

            SyncFluentAccent(app, variant);
        }
        catch (Exception ex)
        {
            Log.Warning("ThemeApplier: could not apply flavour {Flavour}; using base palette: {Error}",
                flavour, ex.Message);
        }

        WeakReferenceMessenger.Default.Send(new ThemeAppliedMessage(flavour));
    }

    // Native Fluent accent controls read the FluentTheme palette, not our brushes; keep them in step by pointing the
    // (runtime-updatable) accent at the target variant's selection blue after the flavour override has merged (or
    // been removed for Default, which restores the base #1976d2 pinned in App.axaml). We use BrushRowSelected (the
    // accent-as-background blue that white text sits on) rather than BrushPrimaryBlue, which in Dark is a lighter
    // text/link blue that white would fail against. In the light flavours the two keys are the same value.
    private static void SyncFluentAccent(Application app, ThemeVariant variant)
    {
        if (app.Styles.OfType<FluentTheme>().FirstOrDefault() is not { } fluent) return;
        if (!fluent.Palettes.TryGetValue(variant, out var palette)) return;
        if (app.TryGetResource("BrushRowSelected", variant, out var res) && res is ISolidColorBrush brush)
            palette.Accent = brush.Color;
    }
}
