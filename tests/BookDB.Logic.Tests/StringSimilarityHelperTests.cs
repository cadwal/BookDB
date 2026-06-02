using BookDB.Logic.Helpers;
using Xunit;

namespace BookDB.Logic.Tests;

public class StringSimilarityHelperTests
{
    [Theory]
    [InlineData("Tolkien, J.R.R.", "tolkienjrr")]
    [InlineData("King, Stephen", "kingstephen")]
    [InlineData("  Mixed 123!", "mixed123")]
    [InlineData("", "")]
    public void Normalize_StripsNonAlnumAndLowercases(string input, string expected)
        => Assert.Equal(expected, StringSimilarityHelper.Normalize(input));

    [Theory]
    [InlineData("kitten", "sitting", 3)]
    [InlineData("", "abc", 3)]
    [InlineData("abc", "", 3)]
    [InlineData("same", "same", 0)]
    [InlineData("a", "b", 1)]
    public void Levenshtein_ClassicCases(string a, string b, int expected)
        => Assert.Equal(expected, StringSimilarityHelper.Levenshtein(a, b));

    [Theory]
    [InlineData("Tolkien, J.R.R.", "Tolkien, J. R. R.", true)]   // normalized equal
    [InlineData("King, Stephen", "King, Steven", true)]            // Levenshtein 1 normalized
    [InlineData("Stephen King", "Dean Koontz", false)]             // distant names
    public void IsSuspectedDuplicate_MatchesExpectedThreshold(string a, string b, bool expected)
        => Assert.Equal(expected, StringSimilarityHelper.IsSuspectedDuplicate(a, b));
}
