using System;
using System.Text;

namespace BookDB.Isbn;

/// <summary>
/// Check-digit state of a hand-typed ISBN candidate, for a non-blocking input indicator.
/// <see cref="None"/> is shown as nothing and means only "empty"; any non-empty text that is not a
/// complete valid ISBN — including a wrong length — is <see cref="Invalid"/>.
/// </summary>
public enum IsbnValidity
{
    None,
    Valid,
    Invalid,
}

/// <summary>
/// The one ISBN rule the whole product obeys. It stands on its own — no dependencies, no framework — so the
/// desktop domain and the phone can both have it, and what the phone scans or types is validated with exactly
/// the arithmetic the desktop applies to what arrives.
/// </summary>
public static class IsbnNormalizer
{
    public static string Normalize(string input)
    {
        if (input is null) throw new ArgumentNullException(nameof(input));

        // Whitespace and hyphens are the only separators a printed ISBN uses; anything else is kept so that
        // rubbish stays visibly invalid instead of being cleaned into something plausible.
        var builder = new StringBuilder(input.Length);
        foreach (char character in input)
        {
            if (!char.IsWhiteSpace(character) && character != '-')
                builder.Append(character);
        }

        return builder.ToString();
    }

    public static bool IsValid(string isbn) => GetValidity(isbn) == IsbnValidity.Valid;

    /// <summary>
    /// Classifies a hand-typed ISBN for the input indicator. Empty input is
    /// <see cref="IsbnValidity.None"/> (show nothing). A 10- or 13-character candidate is Valid or
    /// Invalid by its check digit; any other non-empty length can't be a valid ISBN and is Invalid
    /// too. This only reports; it never blocks saving or lookup (misprinted ISBNs exist on real books).
    /// </summary>
    public static IsbnValidity GetValidity(string? input)
    {
        if (input is null) return IsbnValidity.None;
        var normalized = Normalize(input);
        if (normalized.Length == 0) return IsbnValidity.None;
        return normalized.Length switch
        {
            10 => IsValidIsbn10(normalized) ? IsbnValidity.Valid : IsbnValidity.Invalid,
            13 => IsValidIsbn13(normalized) ? IsbnValidity.Valid : IsbnValidity.Invalid,
            _ => IsbnValidity.Invalid,
        };
    }

    public static bool TryConvertToIsbn13(string isbn10, out string isbn13)
    {
        isbn13 = string.Empty;
        var normalized = Normalize(isbn10);
        if (normalized.Length != 10 || !IsValidIsbn10(normalized))
            return false;

        // Strip check digit, prepend 978
        var base12 = "978" + normalized.Substring(0, 9);
        var checkDigit = ComputeIsbn13Check(base12);
        isbn13 = base12 + checkDigit;
        return true;
    }

    private static bool IsValidIsbn10(string isbn10)
    {
        int sum = 0;
        for (int i = 0; i < 9; i++)
        {
            if (!char.IsDigit(isbn10[i])) return false;
            sum += (isbn10[i] - '0') * (10 - i);
        }
        char last = isbn10[9];
        int lastVal = last == 'X' || last == 'x' ? 10 : (char.IsDigit(last) ? last - '0' : -1);
        if (lastVal < 0) return false;
        sum += lastVal;
        return sum % 11 == 0;
    }

    private static bool IsValidIsbn13(string isbn13)
    {
        if (!long.TryParse(isbn13, out _)) return false;
        int sum = 0;
        for (int i = 0; i < 12; i++)
        {
            if (!char.IsDigit(isbn13[i])) return false;
            int digit = isbn13[i] - '0';
            sum += i % 2 == 0 ? digit : digit * 3;
        }
        int check = (10 - (sum % 10)) % 10;
        return (isbn13[12] - '0') == check;
    }

    private static char ComputeIsbn13Check(string base12)
    {
        int sum = 0;
        for (int i = 0; i < 12; i++)
        {
            int digit = base12[i] - '0';
            sum += i % 2 == 0 ? digit : digit * 3;
        }
        int check = (10 - (sum % 10)) % 10;
        return (char)('0' + check);
    }
}
