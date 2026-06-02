using System;
using System.Globalization;
using BookDB.Desktop.Converters;
using BookDB.Desktop.Localization;
using BookDB.Logic.Services;
using Xunit;

namespace BookDB.Desktop.Tests.Converters;

public sealed class StandardPresetNameConverterTests
{
    private static readonly StandardPresetNameConverter _sut = StandardPresetNameConverter.Instance;
    private static readonly CultureInfo _culture = CultureInfo.InvariantCulture;

    [Fact]
    public void Convert_StandardName_ReturnsResourceValue()
    {
        var result = _sut.Convert(PrintPreset.StandardPresetName, typeof(string), null, _culture);
        Assert.Equal(Resources.Print_StandardPresetDisplayName, result);
    }

    [Fact]
    public void Convert_NonStandardName_ReturnsNameUnchanged()
    {
        const string name = "MyCustomPreset";
        var result = _sut.Convert(name, typeof(string), null, _culture);
        Assert.Equal(name, result);
    }

    [Fact]
    public void Convert_Null_ReturnsNull()
    {
        var result = _sut.Convert(null, typeof(string), null, _culture);
        Assert.Null(result);
    }

    [Fact]
    public void Convert_NonStringValue_ReturnsValueUnchanged()
    {
        var result = _sut.Convert(42, typeof(object), null, _culture);
        Assert.Equal(42, result);
    }

    [Fact]
    public void ConvertBack_ThrowsNotSupportedException()
        => Assert.Throws<NotSupportedException>(
            () => _sut.ConvertBack(null, typeof(string), null, _culture));
}
