using Android.Content;
using BookDB.Mobile.Services;

namespace BookDB.Mobile.Android;

/// <summary>Supplies the shared app with the things only the Android head can provide.</summary>
internal sealed class AndroidPlatform : IMobilePlatform
{
    private readonly Context _context;

    public AndroidPlatform(Context context) => _context = context;

    // The app's private files directory — sandboxed to this app, which is where the paired identity lives.
    public string AppDataDirectory => _context.FilesDir!.AbsolutePath;

    public IBarcodeScanner CreateBarcodeScanner() => new AndroidBarcodeScanner(_context);

    public IDocumentScanner CreateDocumentScanner() => new AndroidDocumentScanner(_context);

    public ICaptureAvailabilityProbe CreateCaptureAvailabilityProbe() => new PlayServicesProbe(_context);

    public IDeviceSettings CreateDeviceSettings() => new AndroidDeviceSettings(_context);
}
