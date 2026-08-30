using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using BookDB.Mobile.Services;

namespace BookDB.Mobile.Converters;

/// <summary>Colours the status-chip dot: green connected, amber reconnecting, grey offline, red revoked —
/// revoked is the one state the user has to act on, so it is the one that does not read as a wait.</summary>
public sealed class ConnectionStatusToBrushConverter : IValueConverter
{
    private static readonly IBrush Connected = new SolidColorBrush(Color.FromRgb(0x2E, 0xA0, 0x43));
    private static readonly IBrush Reconnecting = new SolidColorBrush(Color.FromRgb(0xD8, 0x9E, 0x00));
    private static readonly IBrush Offline = new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0x9A));
    private static readonly IBrush Revoked = new SolidColorBrush(Color.FromRgb(0xC0, 0x39, 0x2B));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        ConnectionStatus.Connected => Connected,
        ConnectionStatus.Reconnecting => Reconnecting,
        ConnectionStatus.Revoked => Revoked,
        _ => Offline,
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
