using System.Threading;
using System.Threading.Tasks;
using Android.Content;
using BookDB.Mobile.Services;

namespace BookDB.Mobile.Android;

/// <summary>Opens ML Kit's document scanner and waits for the page it returns.</summary>
internal sealed class AndroidDocumentScanner : IDocumentScanner
{
    private readonly Context _context;

    public AndroidDocumentScanner(Context context) => _context = context;

    public Task<DocumentScanResult> ScanPageAsync(CancellationToken ct = default) =>
        DocumentScanActivity.ScanAsync(_context);
}
