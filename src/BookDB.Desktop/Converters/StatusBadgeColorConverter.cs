using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace BookDB.Desktop.Converters;

public sealed class StatusBadgeColorConverter : IValueConverter
{
    public static readonly StatusBadgeColorConverter Instance = new();

    private static readonly Dictionary<string, ISolidColorBrush> StatusBrushes = new(StringComparer.OrdinalIgnoreCase)
    {
        { "Read",     new SolidColorBrush(Color.Parse("#4caf50")) },
        { "Reading",  new SolidColorBrush(Color.Parse("#2196f3")) },
        { "Unread",   new SolidColorBrush(Color.Parse("#f44336")) },
        { "Archive",  new SolidColorBrush(Color.Parse("#9e9e9e")) },
        { "Wishlist", new SolidColorBrush(Color.Parse("#9c27b0")) },
    };

    private static readonly ISolidColorBrush DefaultBrush = new SolidColorBrush(Colors.Gray);

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string name && StatusBrushes.TryGetValue(name, out var brush))
            return brush;
        return DefaultBrush;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
