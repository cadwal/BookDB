using System.Globalization;
using System.Threading;
using BookDB.Mobile.Localization;
using Xunit;

namespace BookDB.Mobile.Tests;

/// <summary>
/// A book's format and language are words the computer seeded, and the phone has the same words in its own
/// resources — so they read in the language of the device holding the book rather than the one the library
/// was set up in. What the computer typed itself has no key and stands as it is.
/// </summary>
public class SeededLookupNamesTests
{
    [Fact]
    public void ASeededKey_IsSaidInThisAppsOwnWords()
    {
        // The stored name is deliberately not the word the phone would use: the key has to win.
        Assert.Equal(Resources.Format_Hardcover, SeededLookupNames.Localize("Format_Hardcover", "Gebundenes Buch"));
        Assert.Equal(Resources.Language_Swedish, SeededLookupNames.Localize("Language_Swedish", "Schwedisch"));
    }

    [Fact]
    public void TheSameKey_ReadsDifferentlyInADifferentLanguage()
    {
        var original = Thread.CurrentThread.CurrentUICulture;
        try
        {
            Thread.CurrentThread.CurrentUICulture = new CultureInfo("sv");
            var swedish = SeededLookupNames.Localize("Format_Hardcover", "Hardcover");

            Thread.CurrentThread.CurrentUICulture = new CultureInfo("fr");
            var french = SeededLookupNames.Localize("Format_Hardcover", "Hardcover");

            Assert.NotEqual(swedish, french);
        }
        finally
        {
            Thread.CurrentThread.CurrentUICulture = original;
        }
    }

    /// <summary>A row the user typed carries no key, and one this app has never heard of is no better than
    /// none — either way the computer's own word is the best answer available.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Format_SomethingThisAppDoesNotKnow")]
    public void WithoutAKeyItKnows_TheStoredNameStands(string? key)
    {
        Assert.Equal("Zine", SeededLookupNames.Localize(key, "Zine"));
    }

    /// <summary>The key arrives over the wire, so it must only ever be able to name the lookup vocabulary —
    /// never a button, a warning, or anything else this app happens to have a string for.</summary>
    [Fact]
    public void AKeyOutsideTheLookupVocabulary_IsNotResolvedAtAll()
    {
        Assert.Equal("Paperback", SeededLookupNames.Localize("Nav_Back", "Paperback"));
        Assert.Equal("Paperback", SeededLookupNames.Localize("Camera_OpenSettings", "Paperback"));
    }

    [Fact]
    public void NothingAtAll_StaysNothing()
    {
        Assert.Null(SeededLookupNames.Localize(null, null));
    }
}
