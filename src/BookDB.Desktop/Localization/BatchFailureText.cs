using System;
using BookDB.Models.Entities;

namespace BookDB.Desktop.Localization;

/// <summary>
/// Maps <see cref="BatchFailureReason"/> codes to localized strings. Stored codes come back from the
/// database, where legacy rows may hold null or an unknown value — those fall back to the generic text
/// rather than ever surfacing raw.
/// </summary>
public static class BatchFailureText
{
    public static string Describe(BatchFailureReason reason) => reason switch
    {
        BatchFailureReason.NoResults          => Resources.BatchQueue_Failure_NoResults,
        BatchFailureReason.NetworkError       => Resources.BatchQueue_Failure_NetworkError,
        BatchFailureReason.RateLimited        => Resources.BatchQueue_Failure_RateLimited,
        BatchFailureReason.AllSourcesDisabled => Resources.BatchQueue_Failure_AllSourcesDisabled,
        _                                     => Resources.BatchQueue_Failure_Unexpected,
    };

    public static string DescribeCode(string? code) =>
        Enum.TryParse<BatchFailureReason>(code, out var reason)
            ? Describe(reason)
            : Resources.BatchQueue_Failure_Unexpected;
}
