namespace BookDB.Mobile.Services;

/// <summary>Whether cover capture can run at all, and if not, what the user can do about it. Asked before a
/// capture session so a device that cannot scan says so instead of opening a screen that fails.</summary>
public enum CaptureAvailability
{
    Available,

    /// <summary>No Google Play services on the device — nothing the user can install their way out of on a
    /// de-Googled build, so the app offers ISBN-only staging instead.</summary>
    PlayServicesMissing,

    /// <summary>Google Play services is present but too old for the scanner; updating it fixes this.</summary>
    PlayServicesOutdated,
}

/// <summary>The head answers for its own platform: on Android the document scanner is delivered by Google Play
/// services, so it can genuinely be unavailable; where the scanner ships in the OS the answer is always
/// <see cref="CaptureAvailability.Available"/>.</summary>
public interface ICaptureAvailabilityProbe
{
    CaptureAvailability Check();
}
