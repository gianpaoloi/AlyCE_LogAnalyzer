using LogAnalyzer.Services.Updates;
using Xunit;

namespace LogAnalyzer.Tests;

/// <summary>
/// These two guards decide what the updater is willing to download and where it writes it, just
/// before the file is executed — so they are worth pinning down precisely.
/// </summary>
public class UpdateDownloadTests
{
    [Theory]
    // What GitHub actually serves release assets from.
    [InlineData("https://github.com/gianpaoloi/AlyCE_LogAnalyzer/releases/download/v1.3.0/Setup.exe")]
    [InlineData("https://objects.githubusercontent.com/github-production-release-asset/1/2")]
    [InlineData("https://api.github.com/repos/o/r/releases/latest")]
    [InlineData("https://GITHUB.COM/o/r/x.exe")]
    public void Accepts_an_https_github_url(string url)
    {
        Assert.True(UpdateDownload.IsTrustedUrl(url));
    }

    [Theory]
    // Plain HTTP would let anything on the path swap the executable.
    [InlineData("http://github.com/o/r/releases/download/v1/Setup.exe")]
    // A host that merely ends in the right letters, which a naive Contains or EndsWith would pass.
    [InlineData("https://evil-github.com/x.exe")]
    [InlineData("https://githubcom/x.exe")]
    [InlineData("https://github.com.attacker.net/x.exe")]
    // Other schemes, including one that would run a local file.
    [InlineData("file://C:/Windows/System32/cmd.exe")]
    [InlineData("ftp://github.com/x.exe")]
    // Not a URL at all.
    [InlineData("github.com/o/r/x.exe")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Refuses_anything_else(string? url)
    {
        Assert.False(UpdateDownload.IsTrustedUrl(url));
    }

    [Theory]
    [InlineData("AlyCE-LogAnalyzer-Setup-1.3.0.exe", "AlyCE-LogAnalyzer-Setup-1.3.0.exe")]
    // The asset name is chosen by whoever published the release, so path components are stripped
    // rather than trusted — this is the shape of a directory-traversal attempt.
    [InlineData(@"..\..\Windows\System32\evil.exe", "evil.exe")]
    [InlineData("/etc/passwd", "passwd")]
    [InlineData(@"C:\Windows\notepad.exe", "notepad.exe")]
    // Characters Windows will not accept in a file name.
    [InlineData("bad:name*?.exe", "badname.exe")]
    public void Reduces_an_asset_name_to_a_safe_file_name(string asset, string expected)
    {
        Assert.Equal(expected, UpdateDownload.SafeFileName(asset));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData(@"..\..\")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData(":*?")]
    public void Falls_back_to_a_name_of_its_own_when_nothing_usable_is_left(string? asset)
    {
        // Never an empty path, and never a directory — either would throw at the FileStream instead.
        Assert.Equal("AlyCE-LogAnalyzer-Setup.exe", UpdateDownload.SafeFileName(asset));
    }
}
