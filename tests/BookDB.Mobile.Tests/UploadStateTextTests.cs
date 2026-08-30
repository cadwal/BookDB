using System;
using System.Linq;
using BookDB.Contracts;
using BookDB.Mobile.Localization;
using Xunit;

namespace BookDB.Mobile.Tests;

/// <summary>
/// The wire carries states and failure codes; the reader gets words. These guard the seam: a value added to
/// either enum without a line to go with it fails here rather than reaching a screen untranslated.
/// </summary>
public class UploadStateTextTests
{
    [Theory]
    [MemberData(nameof(States))]
    public void EveryStateTheComputerCanReport_HasSomethingToSay(BatchItemState state)
    {
        var text = UploadStateText.ForState(state, title: null);

        Assert.False(string.IsNullOrWhiteSpace(text));

        // Resources.Get hands back the key itself when the key is missing.
        Assert.DoesNotContain("Upload_", text, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Failures))]
    public void EveryFailureCode_HasSomethingToSay(BatchItemFailure failure)
    {
        var text = UploadStateText.ForFailure(failure);

        Assert.False(string.IsNullOrWhiteSpace(text));
        Assert.DoesNotContain("Upload_", text, StringComparison.Ordinal);
    }

    [Fact]
    public void AFailedBook_IsExplainedByItsCode_NotByTheStateItFailedIn()
    {
        var status = new BatchItemStatus
        {
            State = BatchItemState.Failed,
            Failure = BatchItemFailure.ImageRejected,
        };

        Assert.Equal(Resources.Upload_Failure_ImageRejected, UploadStateText.For(status));
    }

    [Fact]
    public void AKnownTitle_IsNamedAndAMissingOneIsNotLeftAsAHole()
    {
        var named = UploadStateText.ForState(BatchItemState.Done, "Dune");
        var unnamed = UploadStateText.ForState(BatchItemState.Done, title: null);

        Assert.Contains("Dune", named, StringComparison.Ordinal);
        Assert.DoesNotContain("{0}", unnamed, StringComparison.Ordinal);
        Assert.NotEqual(named, unnamed);
    }

    /// <summary>Blank is not a title — a computer that has no title for a book must not produce an empty
    /// pair of quotation marks.</summary>
    [Fact]
    public void ABlankTitle_CountsAsNoTitle()
    {
        Assert.Equal(
            UploadStateText.ForState(BatchItemState.AddedToExisting, title: null),
            UploadStateText.ForState(BatchItemState.AddedToExisting, "   "));
    }

    public static TheoryData<BatchItemState> States() =>
        [.. Enum.GetValues<BatchItemState>()];

    public static TheoryData<BatchItemFailure> Failures() =>
        [.. Enum.GetValues<BatchItemFailure>()];
}
