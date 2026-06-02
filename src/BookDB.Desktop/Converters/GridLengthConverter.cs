using System;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Data.Converters;

namespace BookDB.Desktop.Converters;

public sealed class GridLengthConverter : IValueConverter
{
    public static readonly GridLengthConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is double pixels)
            return new GridLength(pixels, GridUnitType.Pixel);
        return GridLength.Auto;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is GridLength gl)
            return gl.Value;
        return 0.0;
    }
}
