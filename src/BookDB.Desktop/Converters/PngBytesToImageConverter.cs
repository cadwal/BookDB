using System;
using System.Globalization;
using System.IO;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;

namespace BookDB.Desktop.Converters;

/// <summary>
/// Turns encoded PNG bytes into an image for binding. Decoding needs a rendering platform, so it belongs
/// here rather than in a view model — which keeps view models constructible in plain unit tests.
/// </summary>
public sealed class PngBytesToImageConverter : IValueConverter
{
    public static readonly PngBytesToImageConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not byte[] { Length: > 0 } bytes)
        {
            return null;
        }

        using var stream = new MemoryStream(bytes);
        return new Bitmap(stream);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
