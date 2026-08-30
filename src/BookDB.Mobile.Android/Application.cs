using Android.App;
using Android.Runtime;
using Avalonia;
using Avalonia.Android;

namespace BookDB.Mobile.Android;

// The Android application object is where the shared Avalonia App is wired in.
[Application]
public sealed class MainApplication : AvaloniaAndroidApplication<App>
{
    public MainApplication(nint javaReference, JniHandleOwnership transfer) : base(javaReference, transfer)
    {
    }

    public override void OnCreate()
    {
        // Hand the shared app its platform services before Avalonia (and the shell) start.
        App.Platform = new AndroidPlatform(this);
        base.OnCreate();
    }

    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder) =>
        base.CustomizeAppBuilder(builder).WithInterFont();
}
