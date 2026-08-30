using Android.App;
using Android.Content.PM;
using Android.OS;
using Avalonia.Android;
using Avalonia.Controls.ApplicationLifetimes;
using BookDB.Mobile.ViewModels;

namespace BookDB.Mobile.Android;

[Activity(
    Label = "BookDB",
    Theme = "@style/MyTheme.NoActionBar",
    Icon = "@drawable/Icon",
    MainLauncher = true,
    // The app lays itself out for rotation and tablet widths, so it handles these configuration
    // changes itself rather than being torn down and recreated.
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode,
    // The soft keyboard must shrink the window rather than cover it: the ISBN screen's manual-entry field and
    // its button sit at the bottom, and the ScrollViewer they live in can only reach them if the window it is
    // measured against gives way.
    WindowSoftInputMode = global::Android.Views.SoftInput.AdjustResize)]
public sealed class MainActivity : AvaloniaMainActivity
{
    /// <summary>
    /// Android's Back has to drive the shell's own back stack, or the gesture closes the app from whichever
    /// screen the user is on. Taken through Avalonia's <c>BackRequested</c> rather than by overriding
    /// <c>OnBackPressed</c>: Avalonia already owns the platform side — it registers the AndroidX callback and
    /// answers the predictive-back API — and the override is obsolete from API 33 (CA1422). Handled keeps the
    /// press; leaving it unhandled lets the platform do what it would have done, which at the hub means
    /// leaving the app.
    /// </summary>
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        BackRequested += (_, e) => e.Handled = Shell()?.HandleSystemBack() == true;
    }

    private static MainViewModel? Shell() =>
        (Avalonia.Application.Current?.ApplicationLifetime as ISingleViewApplicationLifetime)?.MainView?.DataContext
            as MainViewModel;
}
