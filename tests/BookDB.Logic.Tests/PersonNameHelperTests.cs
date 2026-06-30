using System.Linq;
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
    [InlineData("Smith, John / Jones, Mary; Extra", 3, new[] { "Smith, John", "Jones, Mary", "Extra" })]
    public void SplitSquished_SplitsOnCorrectSeparator(string input, int expectedCount, string[] expectedElements)
    {
        var result = PersonNameHelper.SplitSquished(input);
        Assert.Equal(expectedCount, result.Count);
        Assert.Equal(expectedElements, result);
    }

    // Per-name cleanup of the noise Readerware online cataloguing leaves in a single author field.
    [Theory]
    [InlineData("[Dan Beattie", "Dan Beattie")]                          // stray leading bracket
    [InlineData("Autotech teknikinformation]", "Autotech teknikinformation")] // stray trailing bracket
    [InlineData("av Pavel Konovaltjuk", "Pavel Konovaltjuk")]            // Swedish "by"
    [InlineData("av Thomas Roth.", "Thomas Roth")]                       // "by" + trailing period
    [InlineData("översättning: Richard Areschoug", "Richard Areschoug")] // role label (translation:)
    [InlineData("[bearbetning: Robert Hermansson].", "Robert Hermansson")] // bracket + role label + trailing ].
    [InlineData("Manus: Fabcaro", "Fabcaro")]                            // role label (script:)
    [InlineData("Writing as Paul French", "Paul French")]                // pseudonym by-line
    [InlineData("et al. George R. R. Martin", "George R. R. Martin")]    // "et al." by-line
    [InlineData("Introduction) Louise Willmot", "Louise Willmot")]       // orphaned "role)" prefix
    [InlineData("Stephen King", "Stephen King")]                         // clean name — unchanged
    public void DeriveDisplayName_StripsSerializedFieldNoise(string input, string expected)
        => Assert.Equal(expected, PersonNameHelper.DeriveDisplayName(input));

    // End-to-end author extraction (split + per-fragment clean) over the real messy shapes from the Readerware
    // backups — the pipeline ImportService runs.
    [Theory]
    [InlineData("[David Smith, Sean McLachlan, Angus Konstam", new[] { "David Smith", "Sean McLachlan", "Angus Konstam" })]
    [InlineData("av Pavel Konovaltjuk och Einar Lyth", new[] { "Pavel Konovaltjuk", "Einar Lyth" })]
    [InlineData("[Dan Beattie och Philip Katcher", new[] { "Dan Beattie", "Philip Katcher" })]
    [InlineData("[författare: Michael McNally, Ross Cowan, Peter Wilcox", new[] { "Michael McNally", "Ross Cowan", "Peter Wilcox" })]
    [InlineData("text: Lennart Rosander & Per Olgarsson", new[] { "Lennart Rosander", "Per Olgarsson" })]
    [InlineData("Tom Clancy with Mark Greaney", new[] { "Tom Clancy", "Mark Greaney" })]
    [InlineData("P. A. Westrin, Stefan Diös, Ingrid Emond och Reine Mårtensson.", new[] { "P. A. Westrin", "Stefan Diös", "Ingrid Emond", "Reine Mårtensson" })]
    [InlineData("Smith, John / Jones, Mary", new[] { "Smith, John", "Jones, Mary" })] // two "Last, First" — not over-split
    [InlineData("García Márquez, Gabriel José", new[] { "García Márquez, Gabriel José" })] // compound "Last, First" — one author
    [InlineData("Gentry Lee Arthur C. Clarke", new[] { "Gentry Lee Arthur C. Clarke" })] // no separator — left as one
    [InlineData("n/a", new string[0])]                                                   // placeholder — not split, not imported
    public void AuthorPipeline_CleansAndSplitsRealMessyData(string input, string[] expected)
        => Assert.Equal(expected, CleanAuthors(input));

    private static string[] CleanAuthors(string raw) =>
        PersonNameHelper.SplitSquished(raw)
            .Select(f => PersonNameHelper.ParseDisplayNameAndRoleHint(f).DisplayName)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .ToArray();

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
