using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace BookDB.Logic.Helpers;

public static class PersonNameHelper
{
    private static readonly Regex _roleSuffixRegex = new(@"\s*[\(\[]\s*(?<role>[^\)\]]+?)\s*[\)\]]\s*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static (string DisplayName, string? RoleHint) ParseDisplayNameAndRoleHint(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return (string.Empty, null);

        var trimmed = raw.Trim();
        var match = _roleSuffixRegex.Match(trimmed);
        if (!match.Success)
            return (DeriveDisplayName(trimmed), null);

        var rawName = trimmed[..match.Index].Trim();
        var roleHint = match.Groups["role"].Value.Trim();
        return (DeriveDisplayName(rawName), string.IsNullOrWhiteSpace(roleHint) ? null : roleHint);
    }

    public static string DeriveDisplayName(string raw)
    {
        if (raw is null) return string.Empty;
        var s = raw.Trim();
        // 1. Strip leading "by " (case-insensitive)
        if (s.Length >= 3 && s.StartsWith("by ", StringComparison.OrdinalIgnoreCase))
            s = s[3..].TrimStart();
        // 2. Strip trailing periods (one or more)
        s = s.TrimEnd('.');
        // 3. Strip a single trailing balanced parenthetical "(...)" or bracketed "[...]"
        var paren = s.LastIndexOf('(');
        var bracket = s.LastIndexOf('[');
        bool suffixWasStripped = false;

        if (paren > 0 && s.EndsWith(')'))
        {
            s = s[..paren].TrimEnd();
            suffixWasStripped = true;
        }
        else if (bracket > 0 && s.EndsWith(']'))
        {
            s = s[..bracket].TrimEnd();
            suffixWasStripped = true;
        }

        // 3b. Strip unbalanced trailing "(" or "[" only if step 3 did not already run
        if (!suffixWasStripped)
        {
            var uParen = s.LastIndexOf('(');
            var uBracket = s.LastIndexOf('[');
            if (uParen > 0 && !s.EndsWith(')'))
                s = s[..uParen].TrimEnd();
            else if (uBracket > 0 && !s.EndsWith(']'))
                s = s[..uBracket].TrimEnd();
        }
        // 4. Final trim
        return s.Trim();
    }

    public static string DeriveSortName(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName)) return string.Empty;
        var s = displayName.Trim();
        // If already "Lastname, Firstname" format -> keep as-is
        if (s.Contains(',')) return s;
        var parts = s.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length <= 1) return s;
        return $"{parts[^1]}, {string.Join(" ", parts[..^1])}";
    }

    private static readonly string[] _squishSeparators = 
    [
        " / ", ";", "|", "&", 
        " and ", " och ", " et ", " und ", " y "
    ];

    /// <summary>
    /// Splits a displayName that may contain multiple authors joined by a separator.
    /// Checks separators in priority order: symbols first, then multilingual conjunctions.
    /// Returns a single-element list if no separator is found.
    /// </summary>
    public static IReadOnlyList<string> SplitSquished(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName)) return [];

        foreach (var sep in _squishSeparators)
        {
            // Using OrdinalIgnoreCase so "And" or "AND" are caught
            if (displayName.Contains(sep, StringComparison.OrdinalIgnoreCase))
            {
                return [.. displayName.Split(sep, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
            }
        }

        return [displayName.Trim()];
    }
}
