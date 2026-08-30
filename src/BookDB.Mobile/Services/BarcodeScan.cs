namespace BookDB.Mobile.Services;

/// <summary>How a scan ended. A closed camera and a refused camera look identical to the caller unless they
/// are told apart, and only one of them has a way out the user can act on.</summary>
public enum BarcodeScanOutcome
{
    /// <summary>A code was read.</summary>
    Read,

    /// <summary>The screen closed without a read — cancelled, or backed out of.</summary>
    NothingRead,

    /// <summary>The camera was refused. The OS will not ask again from inside the app once it is refused for
    /// good, so the only way back is the device's own settings.</summary>
    PermissionDenied,

    /// <summary>No camera to scan with: the device has none, or another app holds the one it has.</summary>
    CameraUnavailable,
}

/// <summary>The result of one scan: what was read, and if nothing was, why not.</summary>
public sealed record BarcodeScan(BarcodeScanOutcome Outcome, string? Text)
{
    public static BarcodeScan Of(string text) => new(BarcodeScanOutcome.Read, text);

    public static BarcodeScan Nothing { get; } = new(BarcodeScanOutcome.NothingRead, null);

    public static BarcodeScan PermissionDenied { get; } = new(BarcodeScanOutcome.PermissionDenied, null);

    public static BarcodeScan CameraUnavailable { get; } = new(BarcodeScanOutcome.CameraUnavailable, null);
}
