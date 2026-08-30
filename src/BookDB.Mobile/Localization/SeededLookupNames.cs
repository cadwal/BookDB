using System;

namespace BookDB.Mobile.Localization;

/// <summary>
/// A book's format and language are lookup rows on the computer. The ones the computer seeded carry a
/// resource key and can therefore be said in this device's language; one the user typed themselves has no
/// key and travels as the word they typed. The key never decides anything — it only picks a string — so a
/// key this app does not know simply leaves the stored name standing.
/// </summary>
public static class SeededLookupNames
{
    /// <summary>Only the seeded lookup vocabulary is addressable this way: a key arriving from the wire must
    /// not be able to name any other string in the app.</summary>
    private static readonly string[] Prefixes = ["Format_", "Language_"];

    public static string? Localize(string? key, string? storedName)
    {
        if (string.IsNullOrEmpty(key) || !IsLookupKey(key))
        {
            return storedName;
        }

        return Resources.Find(key) ?? storedName;
    }

    private static bool IsLookupKey(string key)
    {
        foreach (var prefix in Prefixes)
        {
            if (key.StartsWith(prefix, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
