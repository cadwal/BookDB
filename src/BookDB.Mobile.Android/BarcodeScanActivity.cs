using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Media;
using Android.Runtime;
using Android.OS;
using Android.Views;
using Android.Widget;
using AndroidX.AppCompat.App;
using AndroidX.Camera.Core;
using AndroidX.Camera.Core.ResolutionSelector;
using AndroidX.Camera.Lifecycle;
using AndroidX.Camera.View;
using AndroidX.Core.Content;
using BookDB.Mobile.Services;
using Xamarin.Google.MLKit.Vision.BarCode;
using Xamarin.Google.MLKit.Vision.Common;
using MlKitBarcode = Xamarin.Google.MLKit.Vision.Barcode.Common.Barcode;

// Both this app and ML Kit have a thing called a barcode scanner; the alias keeps the two apart.
using MlKitScanner = Xamarin.Google.MLKit.Vision.BarCode.IBarcodeScanner;

namespace BookDB.Mobile.Android;

/// <summary>
/// The live camera, for a book's number and for a computer's pairing code alike: our own screen — reticle,
/// torch, close — with CameraX supplying the preview and the frames, and ML Kit decoding them from its
/// bundled model (no Play services, no first-use download). The first code of the kind asked for wins: buzz,
/// beep, close, hand the value back.
/// </summary>
[Activity(
    Theme = "@style/MyTheme.NoActionBar",
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
internal sealed class BarcodeScanActivity : AppCompatActivity
{
    internal const string ExtraKind = "kind";
    internal const string ExtraInstruction = "instruction";
    internal const string ExtraClose = "close";
    internal const string ExtraTorch = "torch";
    internal const string ExtraRationale = "rationale";

    private const int CameraPermissionRequest = 7001;

    /// <summary>The scan in flight. The interface hands out one scan at a time, and the UI only ever has one
    /// open, so a single slot is enough — a second start abandons the first rather than leaving it hanging.</summary>
    private static TaskCompletionSource<BarcodeScan>? _pending;

    private PreviewView? _preview;
    private TextView? _instruction;
    private ICamera? _camera;
    private MlKitScanner? _barcodes;
    private BarcodeKind _kind;
    private bool _torchOn;

    internal static Task<BarcodeScan> ScanAsync(Context context, Intent intent)
    {
        _pending?.TrySetResult(BarcodeScan.Nothing);
        var completion = new TaskCompletionSource<BarcodeScan>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending = completion;
        context.StartActivity(intent);
        return completion.Task;
    }

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        SetContentView(Resource.Layout.barcode_scanner);

        _kind = (BarcodeKind)(Intent?.GetIntExtra(ExtraKind, (int)BarcodeKind.BookNumber)
            ?? (int)BarcodeKind.BookNumber);

        _preview = FindViewById<PreviewView>(Resource.Id.scan_preview);
        _instruction = FindViewById<TextView>(Resource.Id.scan_instruction);
        _instruction!.Text = Intent?.GetStringExtra(ExtraInstruction);
        ShapeReticle();

        var close = FindViewById<Button>(Resource.Id.scan_close)!;
        close.Text = Intent?.GetStringExtra(ExtraClose);
        close.Click += (_, _) => Complete(BarcodeScan.Nothing);

        var torch = FindViewById<Button>(Resource.Id.scan_torch)!;
        torch.Text = Intent?.GetStringExtra(ExtraTorch);
        torch.Click += (_, _) => ToggleTorch();

        if (ContextCompat.CheckSelfPermission(this, global::Android.Manifest.Permission.Camera)
            == Permission.Granted)
        {
            StartCamera();
        }
        else
        {
            // The reason stands where the picture would be, so it is on screen while the system asks — the
            // prompt itself has no room to say why an app that catalogues books wants a camera.
            _instruction.Text = Intent?.GetStringExtra(ExtraRationale);
            RequestPermissions([global::Android.Manifest.Permission.Camera], CameraPermissionRequest);
        }
    }

    public override void OnRequestPermissionsResult(
        int requestCode, string[] permissions, Permission[] grantResults)
    {
        base.OnRequestPermissionsResult(requestCode, permissions, grantResults);

        if (requestCode != CameraPermissionRequest)
            return;

        if (grantResults.Length > 0 && grantResults[0] == Permission.Granted)
        {
            _instruction!.Text = Intent?.GetStringExtra(ExtraInstruction);
            StartCamera();
        }
        else
        {
            // Reported as a refusal rather than as an empty read: the screen that asked for the scan is the
            // one with room to offer the way back, and it can only offer it if it knows.
            Complete(BarcodeScan.PermissionDenied);
        }
    }

    protected override void OnDestroy()
    {
        // Covers the ways out that are not a decode — the system back gesture, or the task being cleared.
        _pending?.TrySetResult(BarcodeScan.Nothing);
        _pending = null;
        _barcodes?.Close();
        _barcodes = null;
        base.OnDestroy();
    }

    /// <summary>A book's number is a wide, short thing and a pairing code is square; the frame has to say
    /// which, or the eye lines up the code the way the frame suggests and it never fills enough of the
    /// picture to be read.</summary>
    private void ShapeReticle()
    {
        var reticle = FindViewById<View>(Resource.Id.scan_reticle);
        if (reticle?.LayoutParameters is not { } layout)
            return;

        var (width, height) = _kind == BarcodeKind.PairingCode ? (280, 280) : (300, 160);
        layout.Width = (int)(width * Resources!.DisplayMetrics!.Density);
        layout.Height = (int)(height * Resources.DisplayMetrics.Density);
        reticle.LayoutParameters = layout;
    }

    private void StartCamera()
    {
        var formats = _kind.Symbologies().Select(ToMlKitFormat).ToArray();
        _barcodes = BarcodeScanning.GetClient(
            new BarcodeScannerOptions.Builder()
                .SetBarcodeFormats(formats[0], formats[1..])
                .Build());

        var future = ProcessCameraProvider.GetInstance(this);
        future.AddListener(
            new global::Java.Lang.Runnable(() =>
            {
                if (future.Get() is not ProcessCameraProvider provider || _preview is null)
                    return;

                // The back camera is the one to read a barcode with, but a device that only has a front one
                // (some tablets) should still be able to scan rather than be turned away.
                var selector = SelectCamera(provider);
                if (selector is null)
                {
                    Complete(BarcodeScan.CameraUnavailable);
                    return;
                }

                var preview = new Preview.Builder().Build()!;
                preview.SurfaceProvider = _preview.SurfaceProvider;

                var analysisBuilder = new ImageAnalysis.Builder()
                    .SetBackpressureStrategy(ImageAnalysis.StrategyKeepOnlyLatest)!;

                // CameraX analyses at 640x480 unless told otherwise, which is ample for the dozen-odd bars of
                // a book number and nowhere near enough for a pairing code: that one is ~150 modules across,
                // so at 640 the camera resolves two or three pixels per module and the decoder never sees a
                // grid. Asked for only where it is needed — a bigger frame is more to chew through per read.
                if (_kind == BarcodeKind.PairingCode)
                {
                    analysisBuilder.SetResolutionSelector(
                        new ResolutionSelector.Builder()
                            .SetResolutionStrategy(new ResolutionStrategy(
                                new global::Android.Util.Size(1920, 1080),
                                ResolutionStrategy.FallbackRuleClosestHigherThenLower))!
                            .Build()!);
                }

                var analysis = analysisBuilder.Build()!;
                analysis.SetAnalyzer(
                    ContextCompat.GetMainExecutor(this)!, new BarcodeAnalyzer(_barcodes!, OnDecoded));

                provider.UnbindAll();
                try
                {
                    _camera = provider.BindToLifecycle(this, selector, preview, analysis);
                }
                catch (global::Java.Lang.IllegalArgumentException)
                {
                    // The camera went away between the check and the bind (another app took it, or the
                    // device has none usable).
                    Complete(BarcodeScan.CameraUnavailable);
                }
            }),
            ContextCompat.GetMainExecutor(this)!);
    }

    private static int ToMlKitFormat(BarcodeSymbology symbology) => symbology switch
    {
        BarcodeSymbology.Ean13 => MlKitBarcode.FormatEan13,
        BarcodeSymbology.Ean8 => MlKitBarcode.FormatEan8,
        BarcodeSymbology.UpcA => MlKitBarcode.FormatUpcA,
        BarcodeSymbology.QrCode => MlKitBarcode.FormatQrCode,
        _ => MlKitBarcode.FormatAllFormats,
    };

    private static CameraSelector? SelectCamera(ProcessCameraProvider provider)
    {
        foreach (var candidate in new[] { CameraSelector.DefaultBackCamera, CameraSelector.DefaultFrontCamera })
        {
            if (candidate is not null && provider.HasCamera(candidate))
                return candidate;
        }

        return null;
    }

    private void ToggleTorch()
    {
        if (_camera?.CameraInfo?.HasFlashUnit != true)
            return;

        _torchOn = !_torchOn;
        _camera.CameraControl!.EnableTorch(_torchOn);
    }

    private void OnDecoded(string value)
    {
        if (_pending is null)
            return;

        // Confirm the read before the screen disappears: the eye is on the book, not on the phone.
        _preview?.PerformHapticFeedback(FeedbackConstants.VirtualKey);
        try
        {
            using var tone = new ToneGenerator(Stream.Notification, 60);
            tone.StartTone(Tone.PropBeep, 120);
        }
        catch (Exception ex) when (ex is Java.Lang.RuntimeException)
        {
            // Some devices refuse a tone generator while another app owns the audio focus; the buzz is enough.
        }

        Complete(BarcodeScan.Of(value));
    }

    private void Complete(BarcodeScan result)
    {
        _pending?.TrySetResult(result);
        _pending = null;
        Finish();
    }

    /// <summary>Feeds camera frames to ML Kit and reports the first barcode that carries a book number.</summary>
    private sealed class BarcodeAnalyzer : global::Java.Lang.Object, ImageAnalysis.IAnalyzer
    {
        private readonly MlKitScanner _scanner;
        private readonly Action<string> _onDecoded;
        private int _done;

        public BarcodeAnalyzer(MlKitScanner scanner, Action<string> onDecoded)
        {
            _scanner = scanner;
            _onDecoded = onDecoded;
        }

        public void Analyze(IImageProxy? image)
        {
            if (image is null)
                return;

            var frame = image.Image;
            if (frame is null || Volatile.Read(ref _done) != 0)
            {
                image.Close();
                return;
            }

            var input = InputImage.FromMediaImage(frame, image.ImageInfo!.RotationDegrees);
            _scanner.Process(input)
                .AddOnSuccessListener(new SuccessListener(Report))
                .AddOnCompleteListener(new CompleteListener(image.Close));
        }

        private void Report(global::Java.Lang.Object? result)
        {
            if (result is not global::Android.Runtime.JavaList barcodes)
                return;

            foreach (var item in barcodes)
            {
                // The list arrives as raw Java objects; JavaCast is what turns one into a barcode.
                if (item is not global::Java.Lang.Object java)
                    continue;

                var barcode = java.JavaCast<MlKitBarcode>();
                if (barcode is null || string.IsNullOrEmpty(barcode.RawValue))
                    continue;

                // Only the first read counts: analysis keeps running until the screen actually closes.
                if (Interlocked.Exchange(ref _done, 1) == 0)
                    _onDecoded(barcode.RawValue!);

                return;
            }
        }
    }

    private sealed class SuccessListener : global::Java.Lang.Object, global::Android.Gms.Tasks.IOnSuccessListener
    {
        private readonly Action<global::Java.Lang.Object?> _onSuccess;

        public SuccessListener(Action<global::Java.Lang.Object?> onSuccess) => _onSuccess = onSuccess;

        public void OnSuccess(global::Java.Lang.Object? result) => _onSuccess(result);
    }

    private sealed class CompleteListener : global::Java.Lang.Object, global::Android.Gms.Tasks.IOnCompleteListener
    {
        private readonly Action _onComplete;

        public CompleteListener(Action onComplete) => _onComplete = onComplete;

        public void OnComplete(global::Android.Gms.Tasks.Task task) => _onComplete();
    }
}
