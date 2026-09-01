using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace LogAnalyzer.Services.Updates;

/// <summary>
/// A release version that can be ordered against another one, so "is the tag on GitHub newer than
/// the build I am running?" is a comparison rather than a string test.
/// <para>
/// String comparison is exactly what gets this wrong: <c>"1.10.0" &lt; "1.9.0"</c> ordinally, and
/// <c>1.3.0-rc1</c> would look newer than <c>1.3.0</c>. Both shapes occur here — the release
/// workflow publishes prereleases for any tag carrying a <c>-</c> suffix.
/// </para>
/// <para>
/// Ordering follows SemVer precedence: numeric fields first, then a build *without* a prerelease
/// suffix outranks one with it, then the suffix identifiers are compared piecewise. Build metadata
/// (everything after <c>+</c>, which is where SourceLink puts the commit) is ignored, per SemVer §10
/// — two builds of the same version from different commits are the same version.
/// </para>
/// </summary>
public sealed class ReleaseVersion : IComparable<ReleaseVersion>, IEquatable<ReleaseVersion>
{
    /// <summary>
    /// Assembly versions have four fields (<c>1.0.0.0</c>) and tags have three, and both turn up
    /// here — the running build's version comes from an assembly attribute.
    /// </summary>
    private const int NumericFields = 4;

    private readonly int[] _numbers;
    private readonly string _text;

    private ReleaseVersion(int[] numbers, string? prerelease, string text)
    {
        _numbers = numbers;
        Prerelease = prerelease;
        _text = text;
    }

    /// <summary>The prerelease suffix without its leading dash (<c>rc1</c>), or null for a release.</summary>
    public string? Prerelease { get; }

    /// <summary>True for a version like <c>1.3.0-rc1</c>, which ranks below <c>1.3.0</c>.</summary>
    public bool IsPrerelease => Prerelease is not null;

    public int Major => _numbers[0];
    public int Minor => _numbers[1];
    public int Patch => _numbers[2];

    /// <summary>
    /// Parses <c>1.2.3</c>, <c>v1.2.3</c>, <c>1.2</c>, <c>1.0.0.0</c>, <c>1.3.0-rc1</c> and
    /// <c>1.2.3+commitsha</c>. Returns false for anything else rather than throwing: the input is
    /// either a tag name from the network or an assembly attribute, and neither is guaranteed.
    /// </summary>
    public static bool TryParse(string? value, [NotNullWhen(true)] out ReleaseVersion? version)
    {
        version = null;
        if (string.IsNullOrWhiteSpace(value)) return false;

        var text = value.Trim();

        // Tags are written "v1.2.3"; the version itself is not.
        if (text.Length > 0 && (text[0] == 'v' || text[0] == 'V')) text = text[1..];

        // Build metadata never affects precedence, so it is dropped instead of stored.
        var plus = text.IndexOf('+');
        if (plus >= 0) text = text[..plus];

        string? prerelease = null;
        var dash = text.IndexOf('-');
        if (dash >= 0)
        {
            prerelease = text[(dash + 1)..];
            text = text[..dash];
            if (prerelease.Length == 0) return false;
        }

        var parts = text.Split('.');
        if (parts.Length == 0 || parts.Length > NumericFields) return false;

        var numbers = new int[NumericFields];
        for (var i = 0; i < parts.Length; i++)
        {
            // NumberStyles.None rejects a sign, whitespace and thousands separators, so "1.-2.3"
            // and "1. 2.3" fail here rather than parsing into something surprising.
            if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out var n))
                return false;

            numbers[i] = n;
        }

        var normalised = string.Join('.', parts);
        version = new ReleaseVersion(numbers, prerelease,
            prerelease is null ? normalised : $"{normalised}-{prerelease}");

        return true;
    }

    /// <summary>Convenience for the common question, so callers read as prose.</summary>
    public bool IsNewerThan(ReleaseVersion other) => CompareTo(other) > 0;

    public int CompareTo(ReleaseVersion? other)
    {
        if (other is null) return 1;

        for (var i = 0; i < NumericFields; i++)
        {
            // Missing fields are zero, which is what makes 1.2 and 1.2.0 equal.
            var c = _numbers[i].CompareTo(other._numbers[i]);
            if (c != 0) return c;
        }

        return ComparePrerelease(Prerelease, other.Prerelease);
    }

    /// <summary>
    /// SemVer §11: a version with a prerelease suffix has *lower* precedence than the same version
    /// without one, and suffixes are compared identifier by identifier.
    /// </summary>
    private static int ComparePrerelease(string? left, string? right)
    {
        if (left is null && right is null) return 0;
        if (left is null) return 1;      // 1.3.0 is newer than 1.3.0-rc1
        if (right is null) return -1;

        var a = left.Split('.');
        var b = right.Split('.');

        for (var i = 0; i < Math.Max(a.Length, b.Length); i++)
        {
            // Ran out of identifiers: the shorter one is lower (rc1 < rc1.2).
            if (i >= a.Length) return -1;
            if (i >= b.Length) return 1;

            var c = CompareIdentifier(a[i], b[i]);
            if (c != 0) return c;
        }

        return 0;
    }

    private static int CompareIdentifier(string a, string b)
    {
        var aNumeric = int.TryParse(a, NumberStyles.None, CultureInfo.InvariantCulture, out var aValue);
        var bNumeric = int.TryParse(b, NumberStyles.None, CultureInfo.InvariantCulture, out var bValue);

        if (aNumeric && bNumeric) return aValue.CompareTo(bValue);

        // Numeric identifiers always rank below alphanumeric ones, so rc.2 < rc.beta.
        if (aNumeric) return -1;
        if (bNumeric) return 1;

        return string.CompareOrdinal(a, b);
    }

    public bool Equals(ReleaseVersion? other) => CompareTo(other) == 0;

    public override bool Equals(object? obj) => obj is ReleaseVersion other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var n in _numbers) hash.Add(n);
        hash.Add(Prerelease, StringComparer.Ordinal);
        return hash.ToHashCode();
    }

    /// <summary>The version as parsed, without the <c>v</c> prefix or the build metadata.</summary>
    public override string ToString() => _text;
}
