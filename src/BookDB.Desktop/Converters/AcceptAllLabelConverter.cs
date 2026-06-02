using System;
using System.Globalization;
using Avalonia.Data.Converters;
using BookDB.Desktop.Localization;

namespace BookDB.Desktop.Converters;

/// <summary>Formats Resources.MergeReview_AcceptAllFromSource ("{0}") with the bound column name.</summary>
public sealed class AcceptAllLabelConverter : IValueConverter
{
    public static readonly AcceptAllLabelConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is string name ? string.Format(Resources.MergeReview_AcceptAllFromSource, name) : value;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
