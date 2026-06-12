using Avalonia;
using Avalonia.Media;

namespace BookDB.Desktop.Helpers;

/// <summary>
/// Resolves theme palette brushes/colors (defined in <c>Styles/Colors.axaml</c>) from code, with a
/// fallback for when the app or resource isn't available (e.g. unit tests). Lets code-built UI and
/// converters follow the active theme/flavour instead of hardcoding colors.
/// </summary>
public static class Palette
{
    public static IBrush Brush(string key, IBrush fallback)
    {
        if (Application.Current is { } app &&
            app.TryGetResource(key, app.ActualThemeVariant, out var value) &&
            value is IBrush brush)
        {
            return brush;
        }
        return fallback;
    }

    /// <summary>The brush's color, or <paramref name="fallback"/> when the key isn't a solid brush.</summary>
    public static Color Color(string key, Color fallback) =>
        Brush(key, new SolidColorBrush(fallback)) is ISolidColorBrush scb ? scb.Color : fallback;
}
