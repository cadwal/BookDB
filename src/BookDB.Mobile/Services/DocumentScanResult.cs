namespace BookDB.Mobile.Services;

/// <summary>How a capture ended. <see cref="ModuleUnavailable"/> is kept apart from <see cref="Failed"/>
/// because it is the one failure the user can clear themselves: the scanner is delivered on demand and its
/// first use needs a network.</summary>
public enum DocumentScanOutcome
{
    Captured,
    Cancelled,
    ModuleUnavailable,
    Failed,
}

/// <summary>A capture and its bytes: the platform scanner's crop, untouched and full size.</summary>
public sealed class DocumentScanResult
{
    private DocumentScanResult(DocumentScanOutcome outcome, byte[] page)
    {
        Outcome = outcome;
        Page = page;
    }

    public DocumentScanOutcome Outcome { get; }

    public byte[] Page { get; }

    public static DocumentScanResult Captured(byte[] page) => new(DocumentScanOutcome.Captured, page);

    public static DocumentScanResult Cancelled { get; } = new(DocumentScanOutcome.Cancelled, []);

    public static DocumentScanResult ModuleUnavailable { get; } = new(DocumentScanOutcome.ModuleUnavailable, []);

    public static DocumentScanResult Failed { get; } = new(DocumentScanOutcome.Failed, []);
}
