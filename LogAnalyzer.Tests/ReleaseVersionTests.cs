using LogAnalyzer.Services.Updates;
using Xunit;

namespace LogAnalyzer.Tests;

public class ReleaseVersionTests
{
    [Theory]
    // The two shapes that actually meet here: a tag, and an assembly informational version.
    [InlineData("v1.2.3", "1.2.3")]
    [InlineData("1.2.3", "1.2.3")]
    [InlineData("V1.2.3", "1.2.3")]
    // SourceLink appends the commit; it is metadata and does not survive parsing.
    [InlineData("1.2.3+fe12a13c9ce22ebbdb92e08c706e42107ed7ff27", "1.2.3")]
    [InlineData("1.3.0-rc1+abcdef12", "1.3.0-rc1")]
    // Fewer or more numeric fields than three.
    [InlineData("1.2", "1.2")]
    [InlineData("1", "1")]
    [InlineData("1.0.0.0", "1.0.0.0")]
    [InlineData("1.3.0-rc1", "1.3.0-rc1")]
    [InlineData("1.3.0-beta.2", "1.3.0-beta.2")]
    [InlineData("  v1.2.3  ", "1.2.3")]
    public void Parses_the_versions_this_app_produces(string input, string expected)
    {
        Assert.True(ReleaseVersion.TryParse(input, out var version));
        Assert.Equal(expected, version.ToString());
    }

    [Theory]
    // "unknown" is what AppVersion reports when the assembly carries no version — it must not be
    // mistaken for a number, or every check would look like an update.
    [InlineData("unknown")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("v")]
    [InlineData("1.2.3.4.5")]
    [InlineData("1.x.3")]
    [InlineData("-1.2.3")]
    [InlineData("1..3")]
    [InlineData("1.2.3-")]
    [InlineData("1. 2.3")]
    [InlineData("release-1.2.3")]
    public void Refuses_anything_that_is_not_a_version(string? input)
    {
        Assert.False(ReleaseVersion.TryParse(input, out var version));
        Assert.Null(version);
    }

    [Theory]
    // Plain numeric ordering.
    [InlineData("1.2.4", "1.2.3")]
    [InlineData("1.3.0", "1.2.9")]
    [InlineData("2.0.0", "1.99.99")]
    // The case a string comparison gets backwards, which is the reason this type exists.
    [InlineData("1.10.0", "1.9.0")]
    [InlineData("1.0.10", "1.0.9")]
    // A release outranks its own prereleases; a prerelease of a higher version still wins.
    [InlineData("1.3.0", "1.3.0-rc1")]
    [InlineData("1.3.0-rc1", "1.2.9")]
    // Prerelease identifiers compare piecewise, numerically where they are numbers.
    [InlineData("1.3.0-rc2", "1.3.0-rc1")]
    [InlineData("1.3.0-rc.10", "1.3.0-rc.9")]
    [InlineData("1.3.0-rc.1.1", "1.3.0-rc.1")]
    [InlineData("1.3.0-beta", "1.3.0-1")]
    // A fourth field participates like any other.
    [InlineData("1.0.0.1", "1.0.0.0")]
    public void Orders_the_newer_version_first(string newer, string older)
    {
        Assert.True(ReleaseVersion.TryParse(newer, out var a));
        Assert.True(ReleaseVersion.TryParse(older, out var b));

        Assert.True(a.IsNewerThan(b));
        Assert.False(b.IsNewerThan(a));
        Assert.True(a.CompareTo(b) > 0);
        Assert.True(b.CompareTo(a) < 0);
    }

    [Theory]
    // Missing numeric fields are zero, so these name the same version.
    [InlineData("1.2", "1.2.0")]
    [InlineData("1.2.0", "1.2.0.0")]
    [InlineData("1", "1.0.0.0")]
    // Build metadata is not part of precedence: same version, different commit.
    [InlineData("1.2.3+aaaaaaa", "1.2.3+bbbbbbb")]
    [InlineData("v1.2.3", "1.2.3")]
    public void Treats_equivalent_spellings_as_the_same_version(string left, string right)
    {
        Assert.True(ReleaseVersion.TryParse(left, out var a));
        Assert.True(ReleaseVersion.TryParse(right, out var b));

        Assert.Equal(0, a.CompareTo(b));
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.False(a.IsNewerThan(b));
        Assert.False(b.IsNewerThan(a));
    }

    [Fact]
    public void Reports_whether_a_version_is_a_prerelease()
    {
        Assert.True(ReleaseVersion.TryParse("1.3.0-rc1", out var pre));
        Assert.True(pre.IsPrerelease);
        Assert.Equal("rc1", pre.Prerelease);

        Assert.True(ReleaseVersion.TryParse("1.3.0", out var release));
        Assert.False(release.IsPrerelease);
        Assert.Null(release.Prerelease);
    }

    [Fact]
    public void Exposes_the_numeric_fields()
    {
        Assert.True(ReleaseVersion.TryParse("v2.11.7-rc1+abc", out var version));

        Assert.Equal(2, version.Major);
        Assert.Equal(11, version.Minor);
        Assert.Equal(7, version.Patch);
    }

    /// <summary>
    /// Sorting is what a release list would be ordered by, so the whole ordering is exercised at
    /// once rather than only in pairs.
    /// </summary>
    [Fact]
    public void Sorts_a_realistic_release_history()
    {
        var versions = new[] { "1.10.0", "1.0.0", "2.0.0-rc1", "1.9.0", "2.0.0", "1.10.0-rc2", "1.10.0-rc1" }
            .Select(v => { ReleaseVersion.TryParse(v, out var parsed); return parsed!; })
            .OrderBy(v => v)
            .Select(v => v.ToString())
            .ToArray();

        Assert.Equal(
            new[] { "1.0.0", "1.9.0", "1.10.0-rc1", "1.10.0-rc2", "1.10.0", "2.0.0-rc1", "2.0.0" },
            versions);
    }

    [Fact]
    public void Is_newer_than_nothing_at_all()
    {
        Assert.True(ReleaseVersion.TryParse("1.0.0", out var version));

        Assert.True(version.CompareTo(null) > 0);
        Assert.False(version.Equals(null));
    }
}
