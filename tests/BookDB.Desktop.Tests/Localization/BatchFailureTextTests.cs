using System;
using BookDB.Desktop.Localization;
using BookDB.Models.Entities;
using Xunit;

namespace BookDB.Desktop.Tests.Localization;

/// <summary>
/// Guards the enum↔resource contract for batch failure reasons: every <see cref="BatchFailureReason"/>
/// maps to a non-empty localized string, and stored codes that are null or unknown (legacy rows) fall back
/// to the generic text instead of surfacing raw.
/// </summary>
public sealed class BatchFailureTextTests
{
    [Fact]
    public void EveryBatchFailureReason_MapsToNonEmptyString()
    {
        foreach (var reason in Enum.GetValues<BatchFailureReason>())
        {
            var text = BatchFailureText.Describe(reason);
            Assert.False(string.IsNullOrWhiteSpace(text), $"BatchFailureReason.{reason} has no localized text");
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("SomeCodeFromAFutureVersion")]
    public void DescribeCode_FallsBackToGenericText_ForNullOrUnknownCodes(string? code)
    {
        Assert.Equal(
            BatchFailureText.Describe(BatchFailureReason.Unexpected),
            BatchFailureText.DescribeCode(code));
    }

    [Fact]
    public void DescribeCode_ResolvesAStoredEnumName()
    {
        Assert.Equal(
            BatchFailureText.Describe(BatchFailureReason.NoResults),
            BatchFailureText.DescribeCode(nameof(BatchFailureReason.NoResults)));
    }
}
