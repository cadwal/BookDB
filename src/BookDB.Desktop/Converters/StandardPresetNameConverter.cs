using System;
using System.Globalization;
using Avalonia.Data.Converters;
using BookDB.Desktop.Localization;
using BookDB.Logic.Services;

namespace BookDB.Desktop.Converters;

/// <summary>
/// Converts a raw PrintPreset.Name string to its localised display name.
/// When the name equals the invariant identity "Standard", returns the
/// resource value for Print_StandardPresetDisplayName; otherwise returns
/// the name unchanged.
/// </summary>
public sealed class StandardPresetNameConverter : IValueConverter
{
    public static readonly StandardPresetNameConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is string name && name == PrintPreset.StandardPresetName
            ? Resources.Print_StandardPresetDisplayName
            : value;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
