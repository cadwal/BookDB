using Avalonia;
using Avalonia.Controls;

namespace BookDB.Desktop.Behaviors;

public static class ColumnVisibility
{
    private static readonly AttachedProperty<GridLength?> LastWidthProperty =
        AvaloniaProperty.RegisterAttached<ColumnDefinition, GridLength?>(
            "LastWidth", typeof(ColumnVisibility));

    public static readonly AttachedProperty<bool> IsVisibleProperty =
        AvaloniaProperty.RegisterAttached<ColumnDefinition, bool>(
            "IsVisible", typeof(ColumnVisibility), defaultValue: true,
            coerce: (element, visible) =>
            {
                var col = (ColumnDefinition)element;
                if (visible)
                {
                    var last = col.GetValue(LastWidthProperty);
                    if (last.HasValue)
                        col.Width = last.Value;
                }
                else
                {
                    col.SetValue(LastWidthProperty, col.Width);
                    col.Width = new GridLength(0, GridUnitType.Pixel);
                }
                return visible;
            });

    public static bool GetIsVisible(ColumnDefinition element) =>
        element.GetValue(IsVisibleProperty);

    public static void SetIsVisible(ColumnDefinition element, bool value) =>
        element.SetValue(IsVisibleProperty, value);
}
