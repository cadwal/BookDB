using Avalonia;
using Avalonia.Controls;

namespace BookDB.Desktop.Behaviors;

/// <summary>
/// Provides a stable, locale-independent name for DataGrid columns so behaviors can
/// identify columns without matching translated header text.
/// </summary>
public static class DataGridColumnEx
{
    public static readonly AttachedProperty<string?> NameProperty =
        AvaloniaProperty.RegisterAttached<DataGridColumn, string?>(
            "Name", typeof(DataGridColumnEx));

    public static string? GetName(DataGridColumn element) => element.GetValue(NameProperty);
    public static void SetName(DataGridColumn element, string? value) => element.SetValue(NameProperty, value);
}
