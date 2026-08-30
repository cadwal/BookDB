using System.Collections.Generic;

namespace BookDB.Mobile.Services;

/// <summary>What a scan is looking for.</summary>
public enum BarcodeKind
{
    /// <summary>The number printed on a book.</summary>
    BookNumber,

    /// <summary>The pairing code a computer shows.</summary>
    PairingCode,
}

/// <summary>A barcode language, named the way the reader names it rather than the way any one decoder does.</summary>
public enum BarcodeSymbology
{
    Ean13,

    /// <summary>Named but never asked for — see <see cref="BarcodeKinds.Symbologies"/>.</summary>
    Ean8,

    UpcA,
    QrCode,
}

/// <summary>
/// Which symbologies each kind of scan accepts. A decoder reads only what it is asked for, so this list is
/// the difference between a screen that works and one that stares at the right code and says nothing — and
/// narrowing it per purpose is deliberate: a scan that accepted everything would let the QR on a jacket win
/// a read the user meant for the book's number, and the pairing camera answer a book.
/// </summary>
public static class BarcodeKinds
{
    /// <summary>
    /// A book's number is a Bookland EAN-13, or a UPC-A on the American editions that predate it. EAN-8 is
    /// deliberately absent: it is eight digits for small retail packaging and can never encode an ISBN, so a
    /// decoder told to accept it will hand back the wrong barcode off the same cover — a shop's own label,
    /// say — and the wizard will stage it as though it were the book.
    /// </summary>
    public static IReadOnlyList<BarcodeSymbology> Symbologies(this BarcodeKind kind) => kind switch
    {
        BarcodeKind.PairingCode => [BarcodeSymbology.QrCode],
        _ => [BarcodeSymbology.Ean13, BarcodeSymbology.UpcA],
    };
}
