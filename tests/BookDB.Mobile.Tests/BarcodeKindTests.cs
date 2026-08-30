using System.Linq;
using BookDB.Mobile.Services;
using Xunit;

namespace BookDB.Mobile.Tests;

/// <summary>
/// What each kind of scan accepts. A decoder reads only the symbologies it is handed, so this list is the
/// difference between a camera that works and one that looks straight at the right code and reports nothing —
/// which is exactly how the pairing screen shipped until a device was pointed at a real pairing code.
/// </summary>
public class BarcodeKindTests
{
    [Fact]
    public void APairingScan_LooksForAQrCode()
    {
        Assert.Contains(BarcodeSymbology.QrCode, BarcodeKind.PairingCode.Symbologies());
    }

    [Fact]
    public void ABookScan_LooksForThePrintedNumberFormats()
    {
        var symbologies = BarcodeKind.BookNumber.Symbologies();

        Assert.Contains(BarcodeSymbology.Ean13, symbologies);
        Assert.Contains(BarcodeSymbology.UpcA, symbologies);
    }

    /// <summary>Found in UAT: a cover was scanned and the wizard staged 60367310 — eight digits, a perfectly
    /// valid EAN-8 off some other label on the same cover, and an impossible ISBN. No ISBN is eight digits,
    /// so accepting the symbology can only ever hand back something that is not the book.</summary>
    [Fact]
    public void ABookScan_DoesNotAcceptTheEightDigitRetailFormat()
    {
        Assert.DoesNotContain(BarcodeSymbology.Ean8, BarcodeKind.BookNumber.Symbologies());
    }

    /// <summary>The narrowing is the point: a scan that took everything would let the QR on a jacket answer a
    /// read the user meant for the book's number, and a book's number answer the pairing screen.</summary>
    [Fact]
    public void NeitherKindAcceptsTheOthers()
    {
        Assert.DoesNotContain(BarcodeSymbology.QrCode, BarcodeKind.BookNumber.Symbologies());
        Assert.Empty(BarcodeKind.PairingCode.Symbologies()
            .Except([BarcodeSymbology.QrCode]));
    }
}
