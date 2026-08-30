using BookDB.Isbn;
using Xunit;

namespace BookDB.Logic.Tests.Isbn;

/// <summary>
/// The shared ISBN check-digit validator behind the non-blocking input indicator. Empty or
/// still-being-typed input is indeterminate (None → show nothing); a full 10/13-length candidate is
/// Valid or Invalid by its check digit. IsValid is the same judgement reduced to a bool.
/// </summary>
public class IsbnValidityTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]      // only separators — normalizes to empty
    [InlineData("- - -")]
    public void GetValidity_IsNone_ForEmptyInput(string? input)
        => Assert.Equal(IsbnValidity.None, IsbnNormalizer.GetValidity(input));

    [Theory]
    [InlineData("9780306406157")]        // ISBN-13
    [InlineData("978-0-306-40615-7")]    // separators are stripped first
    [InlineData("0306406152")]           // ISBN-10
    [InlineData("080442957X")]           // ISBN-10 with an X check digit
    public void GetValidity_IsValid_ForAGoodCheckDigit(string input)
        => Assert.Equal(IsbnValidity.Valid, IsbnNormalizer.GetValidity(input));

    [Theory]
    [InlineData("9780306406158")]  // ISBN-13, last digit wrong
    [InlineData("0306406153")]     // ISBN-10, wrong check
    [InlineData("978030640615A")]  // 13 chars, non-digit
    [InlineData("978")]            // too short to be an ISBN
    [InlineData("978045152")]      // 9 digits
    [InlineData("978045152653")]   // 12 digits — one short of 13
    [InlineData("97804515265380")] // 14 digits — past 13
    public void GetValidity_IsInvalid_ForAWrongLengthOrFailedCheckDigit(string input)
        => Assert.Equal(IsbnValidity.Invalid, IsbnNormalizer.GetValidity(input));

    [Theory]
    [InlineData("9780306406157", true)]
    [InlineData("9780306406158", false)]
    [InlineData("", false)]
    [InlineData("978045152", false)]
    public void IsValid_MatchesTheValidState(string input, bool expected)
        => Assert.Equal(expected, IsbnNormalizer.IsValid(input));
}
