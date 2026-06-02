using BookDB.Logic.Helpers;
using Xunit;

namespace BookDB.Logic.Tests;

public class PersonNameHelperTests
{
    [Theory]
    [InlineData("by Stephen King", "Stephen King")]
    [InlineData("By Stephen King", "Stephen King")]
    [InlineData("BY Stephen King", "Stephen King")]
    [InlineData("John Doe.", "John Doe")]
    [InlineData("John Doe...", "John Doe")]
    [InlineData("Jane Smith (editor)", "Jane Smith")]
    [InlineData("  Alice  ", "Alice")]
    [InlineData("No Change", "No Change")]
    [InlineData("", "")]
    public void DeriveDisplayName_StripsNoise(string input, string expected)
        => Assert.Equal(expected, PersonNameHelper.DeriveDisplayName(input));

    [Theory]
    [InlineData("Stephen King", "King, Stephen")]
    [InlineData("King, Stephen", "King, Stephen")]
    [InlineData("Madonna", "Madonna")]
    [InlineData("J. R. R. Tolkien", "Tolkien, J. R. R.")]
    [InlineData("", "")]
    public void DeriveSortName_InvertsFirstLast(string input, string expected)
        => Assert.Equal(expected, PersonNameHelper.DeriveSortName(input));

    // Gap 1: Step 3b — unbalanced trailing paren strip
    [Theory]
    [InlineData("John Smith (ed.", "John Smith")]
    [InlineData("Jane Doe (auth", "Jane Doe")]
    [InlineData("Normal Name", "Normal Name")]
    [InlineData("Author (Editor)", "Author")]
    [InlineData("Alice Brown [editor]", "Alice Brown")]
    [InlineData("Bob Smith [ed.", "Bob Smith")]
    [InlineData("Carol White [auth", "Carol White")]
    public void DeriveDisplayName_StripsUnbalancedParenOrBracket(string input, string expected)
        => Assert.Equal(expected, PersonNameHelper.DeriveDisplayName(input));

    // Gap 2: SplitSquished separator detection
    [Theory]
    [InlineData("Smith, John / Jones, Mary", 2, new[] { "Smith, John", "Jones, Mary" })]
    [InlineData("A; B ; C", 3, new[] { "A", "B", "C" })]
    [InlineData("A | B", 2, new[] { "A", "B" })]
    [InlineData("Smith, John", 1, new[] { "Smith, John" })]
    [InlineData("Smith, John / Jones, Mary; Extra", 2, new[] { "Smith, John", "Jones, Mary; Extra" })]
    public void SplitSquished_SplitsOnCorrectSeparator(string input, int expectedCount, string[] expectedElements)
    {
        var result = PersonNameHelper.SplitSquished(input);
        Assert.Equal(expectedCount, result.Count);
        Assert.Equal(expectedElements, result);
    }

    [Fact]
    public void SplitSquished_EmptyString_ReturnsEmptyList()
    {
        var result = PersonNameHelper.SplitSquished("");
        Assert.Empty(result);
    }

    [Fact]
    public void SplitSquished_WhitespaceOnly_ReturnsEmptyList()
    {
        var result = PersonNameHelper.SplitSquished("  ");
        Assert.Empty(result);
    }
}
