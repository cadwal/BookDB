using System.Threading;
using System.Threading.Tasks;

namespace BookDB.Mobile.Services;

/// <summary>Captures one page — a cover — through the platform's own document scanner, which owns the camera
/// UI and hands back a perspective-corrected crop at full resolution. Sizing it is the app's job, not the
/// scanner's.</summary>
public interface IDocumentScanner
{
    Task<DocumentScanResult> ScanPageAsync(CancellationToken ct = default);
}
