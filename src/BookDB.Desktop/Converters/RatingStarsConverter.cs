using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace BookDB.Desktop.Converters;

public sealed class RatingStarsConverter : IValueConverter
{
    public static readonly RatingStarsConverter Instance = new();

    // Maps rating display names like "1 Star", "2 Stars" etc. to Unicode star strings
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string name || string.IsNullOrWhiteSpace(name))
            return null;

        // Extract leading digit(s) from the rating name (e.g., "3 Stars" -> 3)
        var starCount = 0;
        foreach (var ch in name)
        {
            if (char.IsDigit(ch))
                starCount = starCount * 10 + (ch - '0');
            else
                break;
        }

        if (starCount < 1 || starCount > 5)
            return name; // Fallback: show raw name if not parseable

        return new string('\u2605', starCount) + new string('\u2606', 5 - starCount);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
