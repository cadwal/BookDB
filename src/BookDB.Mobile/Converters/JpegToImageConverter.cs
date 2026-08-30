using System;
using System.Globalization;
using System.IO;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;

namespace BookDB.Mobile.Converters;

/// <summary>Renders staged JPEG bytes. View models carry photos as bytes, not bitmaps, so they stay testable
/// without a rendering platform; turning them into something drawable is the view's business.</summary>
public sealed class JpegToImageConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not byte[] { Length: > 0 } jpeg)
        {
            return null;
        }

        try
        {
            return new Bitmap(new MemoryStream(jpeg));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            // Bytes the platform decoder will not take: show the empty slot rather than tear the page down.
            return null;
        }
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
