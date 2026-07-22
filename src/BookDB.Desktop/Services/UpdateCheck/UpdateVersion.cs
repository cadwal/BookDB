using System;

namespace BookDB.Desktop.Services.UpdateCheck;

/// <summary>A three-part release version (Major.Minor.Patch) for update comparison. Pre-release and
/// build-metadata tags are deliberately rejected by <see cref="TryParse"/> so a pre-release can never
/// count as a newer stable release.</summary>
public readonly record struct UpdateVersion(int Major, int Minor, int Patch) : IComparable<UpdateVersion>
{
    public static bool TryParse(string? text, out UpdateVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var s = text.Trim();
        if (s.Length > 0 && (s[0] == 'v' || s[0] == 'V')) s = s[1..];

        // A pre-release ("3.2.0-beta") or build-metadata ("3.2.0+build") tag is not a stable release.
        if (s.IndexOf('-') >= 0 || s.IndexOf('+') >= 0) return false;

        var parts = s.Split('.');
        if (parts.Length is < 1 or > 4) return false;

        if (!TryPart(parts, 0, out var major)) return false;
        if (!TryPart(parts, 1, out var minor)) return false;
        if (!TryPart(parts, 2, out var patch)) return false;

        version = new UpdateVersion(major, minor, patch);
        return true;
    }

    private static bool TryPart(string[] parts, int index, out int value)
    {
        if (index >= parts.Length) { value = 0; return true; } // missing minor/patch → 0
        return int.TryParse(parts[index], out value) && value >= 0;
    }

    public int CompareTo(UpdateVersion other)
    {
        var c = Major.CompareTo(other.Major);
        if (c != 0) return c;
        c = Minor.CompareTo(other.Minor);
        return c != 0 ? c : Patch.CompareTo(other.Patch);
    }

    public static bool operator <(UpdateVersion a, UpdateVersion b) => a.CompareTo(b) < 0;
    public static bool operator >(UpdateVersion a, UpdateVersion b) => a.CompareTo(b) > 0;
    public static bool operator <=(UpdateVersion a, UpdateVersion b) => a.CompareTo(b) <= 0;
    public static bool operator >=(UpdateVersion a, UpdateVersion b) => a.CompareTo(b) >= 0;

    public override string ToString() => $"{Major}.{Minor}.{Patch}";
}
