using BookDB.Desktop.Theming;
using CommunityToolkit.Mvvm.Messaging.Messages;

namespace BookDB.Desktop.Messages;

/// <summary>
/// Raised by <see cref="ThemeApplier"/> after a flavour has been applied to the running app. Consumers that
/// resolve brushes imperatively (rather than through a DynamicResource binding that refreshes on its own) subscribe
/// to re-resolve their colours. A flavour swap that does not change the theme variant (e.g. Vibrant → HighContrast)
/// raises no <c>ActualThemeVariantChanged</c>, so this message is the single reliable signal.
/// </summary>
public sealed class ThemeAppliedMessage : ValueChangedMessage<ThemeFlavour>
{
    public ThemeAppliedMessage(ThemeFlavour flavour) : base(flavour) { }
}
