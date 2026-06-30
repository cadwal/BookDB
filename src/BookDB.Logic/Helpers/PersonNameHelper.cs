using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace BookDB.Logic.Helpers;

public static class PersonNameHelper
{
    // A trailing "(role)" / "[role]" suffix, e.g. "Jane Smith (Editor)".
    private static readonly Regex _roleSuffixRegex = new(@"\s*[\(\[]\s*(?<role>[^\)\]]+?)\s*[\)\]]\s*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // Author-list separators other than the comma: symbols and multilingual conjunctions (incl. Swedish "och").
    // The comma is handled separately — it also means "Last, First" — so it is not in this set. A symbol separator
    // needs an adjacent space, so a glued token like "n/a" is not split.
    private static readonly Regex _listSeparators = new(
        @"\s+[/;|&]\s*|\s*[/;|&]\s+|\s+(?:and|och|et|und|y|with)\s+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // Placeholders that mean "no author" (never a person): n/a, n.a., dashes, question marks.
    private static readonly Regex _placeholder = new(@"^(?:n\s*[/.]\s*a\.?|[-?]+)$", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // Leading by-line noise on a serialized author field: "by"/"av" (Swedish "by"), "writing as", "et al.".
    private static readonly Regex _leadingNoise = new(@"^(?:by|av|writing\s+as|et\s+al\.?)\s+", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    // A leading single-word "role:" label, e.g. Swedish "översättning:", "författare:", "bearbetning:", "text:".
    private static readonly Regex _leadingRoleLabel = new(@"^\p{L}+:\s*", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    // A leading orphaned "role)" where the opening parenthesis was lost, e.g. "Introduction) Louise Willmot".
    private static readonly Regex _leadingOrphanRole = new(@"^\p{L}+\)\s+", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static (string DisplayName, string? RoleHint) ParseDisplayNameAndRoleHint(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return (string.Empty, null);

        var trimmed = raw.Trim();
        var match = _roleSuffixRegex.Match(trimmed);
        // A role suffix must follow a name — a match at index 0 means the whole value is wrapped in brackets/parens
        // (e.g. a serialized list), which is not a role hint and must not yield an empty name.
        if (!match.Success || match.Index == 0)
            return (DeriveDisplayName(trimmed), null);

        var rawName = trimmed[..match.Index].Trim();
        var roleHint = match.Groups["role"].Value.Trim();
        return (DeriveDisplayName(rawName), string.IsNullOrWhiteSpace(roleHint) ? null : roleHint);
    }

    public static string DeriveDisplayName(string raw)
    {
        if (raw is null) return string.Empty;
        var s = raw.Trim();
        if (_placeholder.IsMatch(s)) return string.Empty;

        // --- Leading noise (serialized author fields often prefix the name) ---
        // A stray opening "[" / "(" left by a wrapped list.
        if (s.StartsWith('[') || s.StartsWith('('))
            s = s[1..].TrimStart();
        // "by "/"av "/"writing as "/"et al." — a by-line, not part of the name.
        s = _leadingNoise.Replace(s, string.Empty);
        // A "role:" label or an orphaned "role)" prefix.
        s = _leadingRoleLabel.Replace(s, string.Empty);
        s = _leadingOrphanRole.Replace(s, string.Empty);

        // --- Trailing noise ---
        s = s.TrimEnd('.');
        // Strip a single trailing balanced "(...)"/"[...]" suffix (role, dates, "(pseud.)", …) …
        var paren = s.LastIndexOf('(');
        var bracket = s.LastIndexOf('[');
        var suffixStripped = false;
        if (paren > 0 && s.EndsWith(')')) { s = s[..paren].TrimEnd(); suffixStripped = true; }
        else if (bracket > 0 && s.EndsWith(']')) { s = s[..bracket].TrimEnd(); suffixStripped = true; }
        // … or an unbalanced trailing "(" / "[".
        if (!suffixStripped)
        {
            if (paren > 0 && !s.EndsWith(')')) s = s[..paren].TrimEnd();
            else if (bracket > 0 && !s.EndsWith(']')) s = s[..bracket].TrimEnd();
        }
        // A stray closing "]"/")" with no opener (e.g. "Autotech teknikinformation]"), plus any trailing period.
        s = s.TrimEnd(']', ')', '.');

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

    /// <summary>
    /// Splits a display string that may hold several authors. Splits first on the unambiguous list separators
    /// ("/", ";", "|", "&", and the conjunctions and/och/et/und/y/with), then within each fragment on commas —
    /// but only when the fragment is an explicit bracketed list or carries 2+ commas (3+ names), so a normal
    /// "Last, First" (one comma) is never split. Per-name noise (wrapping brackets, "av"/"by", "role:" labels)
    /// is stripped later by <see cref="DeriveDisplayName"/>. Returns a single element when nothing splits.
    /// </summary>
    public static IReadOnlyList<string> SplitSquished(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName)) return [];

        var result = new List<string>();
        foreach (var part in _listSeparators.Split(displayName.Trim()))
        {
            var p = part.Trim();
            if (p.Length == 0) continue;

            if (p.StartsWith('[') || p.StartsWith('(') || p.Count(c => c == ',') >= 2)
                result.AddRange(p.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            else
                result.Add(p);
        }

        return result.Count > 0 ? result : [displayName.Trim()];
    }
}
