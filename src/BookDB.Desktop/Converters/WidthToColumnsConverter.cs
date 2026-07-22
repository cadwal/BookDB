using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace BookDB.Desktop.Converters;

/// <summary>
/// Maps an available width to a card-grid column count: two columns when there is room, one below the
/// breakpoint. Lets the statistics window's card grid reflow to a single column on narrow widths without
/// code-behind (project code-behind rule).
/// </summary>
public sealed class WidthToColumnsConverter : IValueConverter
{
    public static readonly WidthToColumnsConverter Instance = new();

    /// <summary>Below this width the two-column card grid collapses to one column.</summary>
    public const double Breakpoint = 700;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is double width && width >= Breakpoint ? 2 : 1;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
