using System;
using System.Globalization;
using Avalonia.Data.Converters;
using BookDB.Desktop.Localization;

namespace BookDB.Desktop.Converters;

/// <summary>Converts a resource key string to its localized value from Resources.resx.</summary>
public class ResourceKeyConverter : IValueConverter
{
    public static readonly ResourceKeyConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string key && !string.IsNullOrEmpty(key))
            return Resources.ResourceManager.GetString(key, CultureInfo.CurrentUICulture) ?? key;
        return value?.ToString() ?? string.Empty;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
