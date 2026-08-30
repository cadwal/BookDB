using Android.Content;
using Android.Gms.Common;
using BookDB.Mobile.Services;

namespace BookDB.Mobile.Android;

/// <summary>
/// Asks Google Play services whether it is there and current, because the document scanner is delivered
/// through it. Answered before a capture session so a device that cannot scan says so instead of opening a
/// screen that fails.
/// </summary>
internal sealed class PlayServicesProbe : ICaptureAvailabilityProbe
{
    private readonly Context _context;

    public PlayServicesProbe(Context context) => _context = context;

    public CaptureAvailability Check() =>
        GoogleApiAvailability.Instance.IsGooglePlayServicesAvailable(_context) switch
        {
            ConnectionResult.Success => CaptureAvailability.Available,

            // Present but not usable as-is — an update or an enable in Settings clears these.
            ConnectionResult.ServiceVersionUpdateRequired or
            ConnectionResult.ServiceUpdating or
            ConnectionResult.ServiceDisabled => CaptureAvailability.PlayServicesOutdated,

            _ => CaptureAvailability.PlayServicesMissing,
        };
}
