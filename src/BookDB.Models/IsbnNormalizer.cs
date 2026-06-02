using System;
using System.Text.RegularExpressions;

namespace BookDB.Models;

public static partial class IsbnNormalizer
{
    [GeneratedRegex(@"[\s\-]")]
    private static partial Regex StripRegex();

    public static string Normalize(string input)
    {
        if (input is null) throw new ArgumentNullException(nameof(input));
        var result = StripRegex().Replace(input, string.Empty);
        return result;
    }

    public static bool IsValid(string isbn)
    {
        if (isbn is null) return false;
        var normalized = Normalize(isbn);
        return normalized.Length == 10 ? IsValidIsbn10(normalized)
             : normalized.Length == 13 ? IsValidIsbn13(normalized)
             : false;
    }

    public static bool TryConvertToIsbn13(string isbn10, out string isbn13)
    {
        isbn13 = string.Empty;
        var normalized = Normalize(isbn10);
        if (normalized.Length != 10 || !IsValidIsbn10(normalized))
            return false;

        // Strip check digit, prepend 978
        var base12 = "978" + normalized[..9];
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
