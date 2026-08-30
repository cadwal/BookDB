using Android.Content;
using Android.Net;
using Android.Provider;
using BookDB.Mobile.Services;

namespace BookDB.Mobile.Android;

/// <summary>Opens this app's own page in Android's settings — the only place a permission the user has
/// refused for good can be granted again.</summary>
internal sealed class AndroidDeviceSettings : IDeviceSettings
{
    private readonly Context _context;

    public AndroidDeviceSettings(Context context) => _context = context;

    public void OpenAppSettings()
    {
        var intent = new Intent(
            Settings.ActionApplicationDetailsSettings,
            Uri.FromParts("package", _context.PackageName, null));

        // Started from the application context rather than an activity, so it needs its own task.
        intent.AddFlags(ActivityFlags.NewTask);

        // A heavily customised build can lack the page; the notice that offered this still says in words
        // what to turn on, so there is nothing further to report.
        if (intent.ResolveActivity(_context.PackageManager!) is not null)
        {
            _context.StartActivity(intent);
        }
    }
}
