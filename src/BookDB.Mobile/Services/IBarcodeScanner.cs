using System.Threading;
using System.Threading.Tasks;

namespace BookDB.Mobile.Services;

/// <summary>Scans a single barcode/QR and reports what came of it. Used for the pairing code, for ISBNs, and
/// for the ownership check; the platform head supplies the real implementation.</summary>
public interface IBarcodeScanner
{
    /// <summary>Opens the camera for one read of <paramref name="kind"/>. The kind is not decoration: it
    /// decides which symbologies the decoder will even look at, so a caller that asks for the wrong one gets
    /// a camera that cannot see what it is pointed at.</summary>
    Task<BarcodeScan> ScanOnceAsync(BarcodeKind kind, CancellationToken ct = default);
}
