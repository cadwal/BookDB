using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;

namespace BookDB.Mobile.Converters;

/// <summary>
/// How wide the page column may be, given the size of the window it is in. A tablet gets a capped, centred
/// column so a screen is not a stretched phone screen; a handset gets everything there is.
/// <para>
/// The test is the <em>smaller</em> of the two sides, not the width: a handset turned on its side is wider
/// than the cap, and capping it leaves a dead strip down each edge that no thumb can scroll and puts the
/// scroll bar in the middle of the glass. Android draws the same line in the same place — its own tablet
/// breakpoint is a smallest width of 600 — so a phone stays a phone in both orientations. Raised in UAT 5b.
/// </para>
/// </summary>
public sealed class ViewportToColumnCapConverter : IValueConverter
{
    /// <summary>Smallest width at or above which a device is a tablet, in device-independent pixels. Android's
    /// own <c>sw600dp</c> qualifier, so the phone agrees with the platform about what it is running on.</summary>
    public const double TabletSmallestWidth = 600;

    /// <summary>The column a tablet gets. Wide enough for a row of book metadata, narrow enough to read.</summary>
    public const double ColumnWidth = 640;

    public static double CapFor(Size viewport) =>
        Math.Min(viewport.Width, viewport.Height) >= TabletSmallestWidth ? ColumnWidth : double.PositiveInfinity;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Rect bounds ? CapFor(bounds.Size) : double.PositiveInfinity;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
