using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using BookDB.Desktop.Helpers;

namespace BookDB.Desktop.Converters;

public sealed class StatusBadgeColorConverter : IValueConverter
{
    public static readonly StatusBadgeColorConverter Instance = new();

    // Status name -> palette brush key (defined in Styles/Colors.axaml), so a flavour recolors
    // status badges in one place instead of these being hardcoded here too.
    private static readonly Dictionary<string, string> StatusKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        { "Read",     "BrushStatusRead" },
        { "Reading",  "BrushStatusReading" },
        { "Unread",   "BrushStatusUnread" },
        { "Archive",  "BrushStatusArchive" },
        { "Wishlist", "BrushStatusWishlist" },
    };

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string name && StatusKeys.TryGetValue(name, out var key))
            return Palette.Brush(key, Brushes.Gray);
        return Brushes.Gray;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
