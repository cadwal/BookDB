using System.Threading;
using System.Threading.Tasks;
using Android.Content;
using BookDB.Mobile.Localization;
using BookDB.Mobile.Services;

namespace BookDB.Mobile.Android;

/// <summary>Opens the camera screen and waits for what it reads. The screen's own labels are handed over as
/// extras, translated by the app, so the camera speaks the same language as everything else.</summary>
internal sealed class AndroidBarcodeScanner : IBarcodeScanner
{
    private readonly Context _context;

    public AndroidBarcodeScanner(Context context) => _context = context;

    public Task<BarcodeScan> ScanOnceAsync(BarcodeKind kind, CancellationToken ct = default)
    {
        var pairing = kind == BarcodeKind.PairingCode;
        var intent = new Intent(_context, typeof(BarcodeScanActivity));

        // Started from the application context, so it needs to say which task it belongs to.
        intent.AddFlags(ActivityFlags.NewTask);
        intent.PutExtra(BarcodeScanActivity.ExtraKind, (int)kind);
        intent.PutExtra(
            BarcodeScanActivity.ExtraInstruction,
            pairing ? Resources.Camera_PairingInstruction : Resources.Camera_Instruction);
        intent.PutExtra(BarcodeScanActivity.ExtraClose, Resources.Camera_Close);
        intent.PutExtra(BarcodeScanActivity.ExtraTorch, Resources.Camera_Torch);
        intent.PutExtra(
            BarcodeScanActivity.ExtraRationale,
            pairing ? Resources.Camera_PairingRationale : Resources.Camera_Rationale);

        return BarcodeScanActivity.ScanAsync(_context, intent);
    }
}
