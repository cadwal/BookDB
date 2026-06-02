using System;
using System.Text;

namespace BookDB.Logic.Helpers;

public static class StringSimilarityHelper
{
    public const int SuspectedDuplicateThreshold = 2;

    public static string Normalize(string s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        var sb = new StringBuilder(s.Length);
        foreach (var c in s.ToLowerInvariant())
            if (char.IsLetterOrDigit(c)) sb.Append(c);
        return sb.ToString();
    }

    public static int Levenshtein(string a, string b)
    {
        a ??= string.Empty;
        b ??= string.Empty;
        if (a.Length == 0) return b.Length;
        if (b.Length == 0) return a.Length;

        // Ensure b is the shorter string to minimize memory
        if (a.Length < b.Length)
            (a, b) = (b, a);

        var prev = new int[b.Length + 1];
        var curr = new int[b.Length + 1];

        for (var j = 0; j <= b.Length; j++) prev[j] = j;

        for (var i = 1; i <= a.Length; i++)
        {
            curr[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                curr[j] = Math.Min(
                    Math.Min(prev[j] + 1, curr[j - 1] + 1),
                    prev[j - 1] + cost);
            }
            (prev, curr) = (curr, prev);
        }
        return prev[b.Length];
    }

    public static bool IsSuspectedDuplicate(string nameA, string nameB)
    {
        var na = Normalize(nameA ?? string.Empty);
        var nb = Normalize(nameB ?? string.Empty);
        if (na.Length == 0 && nb.Length == 0) return true;
        if (na == nb) return true;
        return Levenshtein(na, nb) <= SuspectedDuplicateThreshold;
    }
}
