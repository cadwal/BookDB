using System;
using System.IO;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using BookDB.Mobile.Services;
using Google.MLKit.Vision.Documentscanner;

namespace BookDB.Mobile.Android;

/// <summary>
/// Hands the capture moment to ML Kit's document scanner, which brings its own full-screen camera with live
/// edge guidance and a crop/rotate review, and gives back one perspective-corrected page. This activity has
/// no UI of its own — it exists only because the scanner is launched through an <c>IntentSender</c>, which
/// needs an activity to start it and to receive the result.
/// </summary>
[Activity(
    Theme = "@style/MyTheme.Invisible",
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
internal sealed class DocumentScanActivity : Activity
{
    private const int ScanRequestCode = 7002;

    /// <summary>The capture in flight. One slot is enough: the checklist opens one scanner at a time, and a
    /// second start abandons the first rather than leaving it hanging.</summary>
    private static TaskCompletionSource<DocumentScanResult>? _pending;

    internal static Task<DocumentScanResult> ScanAsync(Context context)
    {
        _pending?.TrySetResult(DocumentScanResult.Cancelled);
        var completion = new TaskCompletionSource<DocumentScanResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending = completion;

        var intent = new Intent(context, typeof(DocumentScanActivity));

        // Started from the application context, so it needs to say which task it belongs to.
        intent.AddFlags(ActivityFlags.NewTask);
        context.StartActivity(intent);

        return completion.Task;
    }

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        var options = new GmsDocumentScannerOptions.Builder()
            // One page: a slot holds exactly one cover, and the checklist is what collects the set.
            .SetPageLimit(1)
            .SetGalleryImportAllowed(false)
            .SetResultFormats(GmsDocumentScannerOptions.ResultFormatJpeg)
            .SetScannerMode(GmsDocumentScannerOptions.ScannerModeFull)
            .Build();

        GmsDocumentScanning.GetClient(options)
            .GetStartScanIntent(this)
            .AddOnSuccessListener(new StartListener(this))
            .AddOnFailureListener(new StartFailureListener(this));
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);

        if (requestCode != ScanRequestCode)
            return;

        if (resultCode != Result.Ok)
        {
            Complete(DocumentScanResult.Cancelled);
            return;
        }

        var pages = GmsDocumentScanningResult.FromActivityResultIntent(data)?.Pages;
        var page = pages is { Count: > 0 } ? pages[0] : null;
        if (page?.ImageUri is null)
        {
            Complete(DocumentScanResult.Failed);
            return;
        }

        try
        {
            using var input = ContentResolver!.OpenInputStream(page.ImageUri)!;
            using var buffer = new MemoryStream();
            input.CopyTo(buffer);
            Complete(DocumentScanResult.Captured(buffer.ToArray()));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The scanner's page lives in its own cache; a read that fails here leaves nothing to review.
            Complete(DocumentScanResult.Failed);
        }
    }

    protected override void OnDestroy()
    {
        // Covers the ways out that are not a result — the back gesture, or the task being cleared.
        _pending?.TrySetResult(DocumentScanResult.Cancelled);
        _pending = null;
        base.OnDestroy();
    }

    private void Complete(DocumentScanResult result)
    {
        _pending?.TrySetResult(result);
        _pending = null;
        Finish();
    }

    private sealed class StartListener : Java.Lang.Object, global::Android.Gms.Tasks.IOnSuccessListener
    {
        private readonly DocumentScanActivity _owner;

        public StartListener(DocumentScanActivity owner) => _owner = owner;

        public void OnSuccess(Java.Lang.Object? result)
        {
            if (result is IntentSender sender)
                _owner.StartIntentSenderForResult(sender, ScanRequestCode, null, 0, 0, 0);
            else
                _owner.Complete(DocumentScanResult.Failed);
        }
    }

    private sealed class StartFailureListener : Java.Lang.Object, global::Android.Gms.Tasks.IOnFailureListener
    {
        private readonly DocumentScanActivity _owner;

        public StartFailureListener(DocumentScanActivity owner) => _owner = owner;

        /// <summary>The scanner ships as an on-demand Play-services module, and failing to get an intent for
        /// it means the module has not been delivered — which a network and another try can fix. That is the
        /// only way this call is documented to fail, so it is what the user is told.</summary>
        public void OnFailure(Java.Lang.Exception e) => _owner.Complete(DocumentScanResult.ModuleUnavailable);
    }
}
