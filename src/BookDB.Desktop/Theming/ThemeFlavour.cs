namespace BookDB.Desktop.Theming;

/// <summary>
/// The selectable colour flavours. Each maps to a brush-override dictionary and a FluentTheme
/// palette; the chosen flavour is applied once at startup (changing it requires a restart).
/// </summary>
public enum ThemeFlavour
{
    /// <summary>The base palette, verbatim — no brush or FluentTheme overrides.</summary>
    Default,

    /// <summary>More saturated, higher-energy colours.</summary>
    Vibrant,

    /// <summary>Maximum contrast for low-vision use; overrides base/chrome colours too.</summary>
    HighContrast,

    /// <summary>Dark surfaces with light text; also flips the app to the Dark theme variant.</summary>
    Dark,
}
