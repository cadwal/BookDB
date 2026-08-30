namespace BookDB.Mobile.Services;

/// <summary>The few things the shared app needs from whichever head it is running on: where app-private data
/// lives, how to scan a barcode, how to capture a cover, and how to send the user to the device's own
/// settings. The head implements it and hands it to the app at startup.</summary>
public interface IMobilePlatform
{
    string AppDataDirectory { get; }

    IBarcodeScanner CreateBarcodeScanner();

    IDocumentScanner CreateDocumentScanner();

    ICaptureAvailabilityProbe CreateCaptureAvailabilityProbe();

    IDeviceSettings CreateDeviceSettings();
}
