using System.Globalization;

namespace COMrade.Core.Updates;

/// <summary>
/// A COMrade version such as 0.3.0, or a pre-release build such as 0.3.1-alpha.0.3, which comes
/// before 0.3.1. Pre-releases of the same version are not ordered among themselves: releases on
/// GitHub are never pre-releases.
/// </summary>
public sealed record ReleaseVersion(int Major, int Minor, int Patch, string? PreRelease = null) : IComparable<ReleaseVersion>
{
    public bool IsPreRelease => PreRelease is not null;

    /// <summary>Reads "0.3.0", "v0.3.0", "0.3.1-alpha.0.3" or "0.3.0+commit"; null otherwise.</summary>
    public static ReleaseVersion? TryParse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var version = text.Trim();
        if (version.StartsWith('v') || version.StartsWith('V'))
            version = version[1..];
        version = version.Split('+')[0];

        var dash = version.IndexOf('-');
        var preRelease = dash >= 0 ? version[(dash + 1)..] : null;
        if (preRelease is "")
            return null;

        var numbers = (dash >= 0 ? version[..dash] : version).Split('.');
        if (numbers.Length != 3)
            return null;

        var parsed = new int[3];
        for (var i = 0; i < 3; i++)
        {
            if (!int.TryParse(numbers[i], NumberStyles.None, CultureInfo.InvariantCulture, out parsed[i]))
                return null;
        }

        return new ReleaseVersion(parsed[0], parsed[1], parsed[2], preRelease);
    }

    public int CompareTo(ReleaseVersion? other)
    {
        if (other is null)
            return 1;

        var core = (Major, Minor, Patch).CompareTo((other.Major, other.Minor, other.Patch));
        if (core != 0)
            return core;
        return IsPreRelease == other.IsPreRelease ? 0 : IsPreRelease ? -1 : 1;
    }

    public static bool operator <(ReleaseVersion left, ReleaseVersion right) => left.CompareTo(right) < 0;

    public static bool operator >(ReleaseVersion left, ReleaseVersion right) => left.CompareTo(right) > 0;

    public static bool operator <=(ReleaseVersion left, ReleaseVersion right) => left.CompareTo(right) <= 0;

    public static bool operator >=(ReleaseVersion left, ReleaseVersion right) => left.CompareTo(right) >= 0;

    public override string ToString() =>
        PreRelease is null ? $"{Major}.{Minor}.{Patch}" : $"{Major}.{Minor}.{Patch}-{PreRelease}";
}
