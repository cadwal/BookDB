namespace BookDB.Models.Entities;

public static class BatchStatus
{
    public const string Pending = "Pending";
    public const string Processing = "Processing";
    public const string Done = "Done";
    public const string Failed = "Failed";
    public const string Skipped = "Skipped";
    public const string PendingReview = "PendingReview";
    public const string AutoAccepted = "AutoAccepted";
}
