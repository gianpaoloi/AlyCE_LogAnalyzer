using LogAnalyzer.Services;
using Xunit;

namespace LogAnalyzer.Tests;

public class AppVersionTests
{
    [Theory]
    // What a release build actually reports: SourceLink appends the commit after '+'.
    [InlineData("1.2.3+fe12a13c9ce22ebbdb92e08c706e42107ed7ff27", "1.2.3", "fe12a13c9ce22ebbdb92e08c706e42107ed7ff27")]
    [InlineData("1.0+fe12a13c", "1.0", "fe12a13c")]
    // A build without SourceLink, or with the metadata stripped.
    [InlineData("1.2.3", "1.2.3", null)]
    [InlineData("1.0.0.0", "1.0.0.0", null)]
    // Prereleases keep their suffix on the version side of the '+'.
    [InlineData("1.2.3-beta.1+abcdef12", "1.2.3-beta.1", "abcdef12")]
    [InlineData("2.0.0-rc1", "2.0.0-rc1", null)]
    // Degenerate shapes must not throw.
    [InlineData("1.2.3+", "1.2.3", null)]
    [InlineData("+abcdef12", "unknown", "abcdef12")]
    [InlineData("1.2.3+aa+bb", "1.2.3", "aa+bb")]
    [InlineData("  1.2.3+aa  ", "1.2.3", "aa")]
    [InlineData("", "unknown", null)]
    [InlineData("   ", "unknown", null)]
    [InlineData(null, "unknown", null)]
    public void Split_separates_the_version_from_the_commit(string? informational, string display, string? commit)
    {
        var (actualDisplay, actualCommit) = AppVersion.Split(informational);

        Assert.Equal(display, actualDisplay);
        Assert.Equal(commit, actualCommit);
    }

    /// <summary>
    /// Reads whatever assembly is hosting the tests, so it cannot assert a particular number — only
    /// that the values are coherent, which is what would break if the attribute lookup regressed.
    /// </summary>
    [Fact]
    public void The_running_build_reports_a_usable_version()
    {
        Assert.False(string.IsNullOrWhiteSpace(AppVersion.Full));
        Assert.False(string.IsNullOrWhiteSpace(AppVersion.Display));
        Assert.DoesNotContain("+", AppVersion.Display);
        Assert.StartsWith(AppVersion.Display, AppVersion.Full);
    }

    [Fact]
    public void The_short_commit_is_a_prefix_of_the_commit()
    {
        if (AppVersion.Commit is null)
        {
            Assert.Null(AppVersion.ShortCommit);
            return;
        }

        Assert.NotNull(AppVersion.ShortCommit);
        Assert.StartsWith(AppVersion.ShortCommit!, AppVersion.Commit);
        Assert.True(AppVersion.ShortCommit!.Length <= AppVersion.Commit.Length);
    }
}
