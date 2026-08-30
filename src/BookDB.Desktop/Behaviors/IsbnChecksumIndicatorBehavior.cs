using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Xaml.Interactivity;
using BookDB.Desktop.Localization;
using BookDB.Isbn;
using BookDB.Models;

namespace BookDB.Desktop.Behaviors;

/// <summary>
/// Drives a small ISBN check-digit indicator on the attached <see cref="Image"/>: a valid full-length
/// ISBN shows the success glyph, a failed check digit shows the warning glyph with a localized tooltip,
/// and empty or still-being-typed input hides the image. Purely advisory — it never disables the
/// save/lookup it sits beside. One shared component so every hand-typed ISBN field behaves identically.
/// </summary>
public class IsbnChecksumIndicatorBehavior : Behavior<Image>
{
    public static readonly StyledProperty<string?> IsbnProperty =
        AvaloniaProperty.Register<IsbnChecksumIndicatorBehavior, string?>(nameof(Isbn));

    public string? Isbn
    {
        get => GetValue(IsbnProperty);
        set => SetValue(IsbnProperty, value);
    }

    protected override void OnAttached()
    {
        base.OnAttached();
        if (AssociatedObject is not null)
            AssociatedObject.Loaded += OnLoaded;
    }

    protected override void OnDetaching()
    {
        if (AssociatedObject is not null)
            AssociatedObject.Loaded -= OnLoaded;
        base.OnDetaching();
    }

    // The keyed icon resources live at application scope, reachable only once the image is in the tree.
    private void OnLoaded(object? sender, RoutedEventArgs e) => Update();

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsbnProperty)
            Update();
    }

    private void Update()
    {
        if (AssociatedObject is null || !AssociatedObject.IsLoaded) return;

        switch (IsbnNormalizer.GetValidity(Isbn))
        {
            case IsbnValidity.Valid:
                Apply("Icon.IsbnValid", Resources.Isbn_ChecksumValid);
                break;
            case IsbnValidity.Invalid:
                Apply("Icon.IsbnWarning", Resources.Isbn_ChecksumWarning);
                break;
            default:
                AssociatedObject.Source = null;
                ToolTip.SetTip(AssociatedObject, null);
                AssociatedObject.IsVisible = false;
                break;
        }
    }

    private void Apply(string iconKey, string tooltip)
    {
        AssociatedObject!.Source = AssociatedObject.FindResource(iconKey) as IImage;
        ToolTip.SetTip(AssociatedObject, tooltip);
        AssociatedObject.IsVisible = true;
    }
}
