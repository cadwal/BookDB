using BookDB.Logic.Services;
using Xunit;

namespace BookDB.Logic.Tests.Services;

public sealed class PrintPresetTests
{
    [Fact]
    public void StandardPresetName_IsLiteralStandard()
        => Assert.Equal("Standard", PrintPreset.StandardPresetName);

    [Fact]
    public void CreateDefault_NoArg_NameIsStandardPresetName()
        => Assert.Equal(PrintPreset.StandardPresetName, PrintPreset.CreateDefault().Name);

    [Fact]
    public void CreateDefault_NoArg_HeaderTextIsBookList()
        => Assert.Equal("Book List", PrintPreset.CreateDefault().HeaderText);

    [Fact]
    public void CreateDefault_CustomTitle_HeaderTextMatchesArgument()
        => Assert.Equal("Boklista", PrintPreset.CreateDefault("Boklista").HeaderText);

    [Fact]
    public void CreateDefault_EmptyTitle_HeaderTextIsEmpty()
        => Assert.Equal(string.Empty, PrintPreset.CreateDefault(string.Empty).HeaderText);
}
