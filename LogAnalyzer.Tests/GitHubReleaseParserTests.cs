using System.Text.Json;
using LogAnalyzer.Services.Updates;
using Xunit;

namespace LogAnalyzer.Tests;

public class GitHubReleaseParserTests
{
    /// <summary>
    /// The hash block the release workflow writes into the notes. Kept here in the shape the
    /// workflow produces, so a change to that format shows up as a failing test rather than as a
    /// silently unverified download.
    /// </summary>
    private const string Notes = """
        ## AlyCE Log Analyzer 1.3.0

        ### SHA256
        ```
        AAAA1111BBBB2222CCCC3333DDDD4444EEEE5555FFFF6666AAAA7777BBBB8888  AlyCE-LogAnalyzer-Setup-1.3.0.exe
        1111AAAA2222BBBB3333CCCC4444DDDD5555EEEE6666FFFF7777AAAA8888BBBB  AlyCE-LogAnalyzer-1.3.0-win-x64.zip
        ```
        """;

    /// <summary>
    /// The two assets every release attaches, portable ZIP first — the order the workflow uploads
    /// them in, and the order that would trip up a parser taking the first asset it sees.
    /// </summary>
    private static string DefaultAssets(string? digest) =>
        "[" +
        """
          {
            "name": "AlyCE-LogAnalyzer-1.3.0-win-x64.zip",
            "browser_download_url": "https://github.com/o/r/releases/download/v1.3.0/AlyCE-LogAnalyzer-1.3.0-win-x64.zip",
            "size": 1
          },
          {
            "name": "AlyCE-LogAnalyzer-Setup-1.3.0.exe",
            "browser_download_url": "https://github.com/o/r/releases/download/v1.3.0/AlyCE-LogAnalyzer-Setup-1.3.0.exe",
            "size": 88160256
        """ +
        (digest is null ? "" : $""" , "digest": "{digest}" """) +
        "} ]";

    /// <summary>A <c>releases/latest</c> payload, with the parts each test cares about swappable.</summary>
    private static string Payload(
        string tag = "v1.3.0",
        bool draft = false,
        bool prerelease = false,
        string? notes = Notes,
        string? digest = null,
        string? assets = null)
    {
        var body = notes is null ? "null" : JsonSerializer.Serialize(notes);

        return $$"""
        {
          "tag_name": "{{tag}}",
          "name": "AlyCE Log Analyzer 1.3.0",
          "draft": {{(draft ? "true" : "false")}},
          "prerelease": {{(prerelease ? "true" : "false")}},
          "html_url": "https://github.com/o/r/releases/tag/{{tag}}",
          "published_at": "2026-08-14T09:31:07Z",
          "body": {{body}},
          "assets": {{assets ?? DefaultAssets(digest)}}
        }
        """;
    }

    [Fact]
    public void Reads_the_release_a_published_tag_produces()
    {
        var release = GitHubReleaseParser.Parse(Payload());

        Assert.NotNull(release);
        Assert.Equal("1.3.0", release.Version.ToString());
        Assert.Equal("v1.3.0", release.TagName);
        Assert.Equal("https://github.com/o/r/releases/tag/v1.3.0", release.ReleaseUrl);
        Assert.Equal("AlyCE Log Analyzer 1.3.0", release.Title);
        Assert.False(release.IsPrerelease);
        Assert.Equal(new DateTimeOffset(2026, 8, 14, 9, 31, 7, TimeSpan.Zero), release.PublishedAt);
    }

    /// <summary>
    /// The portable ZIP is listed first and cannot update an installed copy; the setup executable
    /// has to be the one picked.
    /// </summary>
    [Fact]
    public void Picks_the_setup_executable_out_of_the_assets()
    {
        var release = GitHubReleaseParser.Parse(Payload());

        Assert.NotNull(release);
        Assert.True(release.HasInstaller);
        Assert.Equal("AlyCE-LogAnalyzer-Setup-1.3.0.exe", release.InstallerName);
        Assert.EndsWith("AlyCE-LogAnalyzer-Setup-1.3.0.exe", release.InstallerUrl);
        Assert.Equal(88160256, release.InstallerSize);
    }

    [Fact]
    public void Takes_the_installer_hash_from_the_release_notes()
    {
        var release = GitHubReleaseParser.Parse(Payload());

        // The setup line, not the ZIP line that sits next to it in the same block.
        Assert.Equal("AAAA1111BBBB2222CCCC3333DDDD4444EEEE5555FFFF6666AAAA7777BBBB8888",
                     release!.InstallerSha256);
    }

    /// <summary>
    /// GitHub now returns a digest per asset. It is authoritative, so it must win over the notes —
    /// the notes are prose that anyone with write access can edit after the fact.
    /// </summary>
    [Fact]
    public void Prefers_the_assets_own_digest_over_the_notes()
    {
        const string digest = "9999888877776666555544443333222211110000aaaabbbbccccddddeeeeffff";

        var release = GitHubReleaseParser.Parse(Payload(digest: "sha256:" + digest));

        Assert.Equal(digest, release!.InstallerSha256);
    }

    [Theory]
    [InlineData("md5:abc")]                     // wrong algorithm
    [InlineData("sha256:tooshort")]             // not 64 characters
    [InlineData("")]
    public void Ignores_a_digest_it_cannot_use(string digest)
    {
        var release = GitHubReleaseParser.Parse(Payload(digest: digest));

        // Falls back to the hash block rather than carrying the unusable value forward.
        Assert.Equal("AAAA1111BBBB2222CCCC3333DDDD4444EEEE5555FFFF6666AAAA7777BBBB8888",
                     release!.InstallerSha256);
    }

    [Fact]
    public void Leaves_the_hash_unset_when_the_release_publishes_none()
    {
        var release = GitHubReleaseParser.Parse(Payload(notes: "No hashes in here."));

        Assert.NotNull(release);
        Assert.True(release.HasInstaller);
        Assert.Null(release.InstallerSha256);
    }

    /// <summary>
    /// A hash whose file name does not match the asset must not be used: a release that attaches
    /// several files publishes several hashes, and pairing them wrongly would fail every install.
    /// </summary>
    [Fact]
    public void Does_not_use_a_hash_published_for_a_different_file()
    {
        var notes = """
            ### SHA256
            ```
            1111AAAA2222BBBB3333CCCC4444DDDD5555EEEE6666FFFF7777AAAA8888BBBB  AlyCE-LogAnalyzer-1.3.0-win-x64.zip
            ```
            """;

        var release = GitHubReleaseParser.Parse(Payload(notes: notes));

        Assert.Null(release!.InstallerSha256);
    }

    [Fact]
    public void Reports_a_prerelease_as_one()
    {
        var release = GitHubReleaseParser.Parse(Payload(tag: "v1.3.0-rc1", prerelease: true));

        Assert.NotNull(release);
        Assert.True(release.IsPrerelease);
        Assert.True(release.Version.IsPrerelease);
        Assert.Equal("1.3.0-rc1", release.Version.ToString());
    }

    [Fact]
    public void Reads_a_release_with_no_assets_but_offers_no_installer()
    {
        var release = GitHubReleaseParser.Parse(Payload(assets: "[]"));

        Assert.NotNull(release);
        Assert.False(release.HasInstaller);
        Assert.Null(release.InstallerUrl);
        Assert.Null(release.InstallerSha256);
    }

    /// <summary>Only .exe assets qualify, so a release of just the portable ZIP has no installer.</summary>
    [Fact]
    public void Does_not_treat_the_portable_zip_as_an_installer()
    {
        var assets = """
            [{ "name": "AlyCE-LogAnalyzer-1.3.0-win-x64.zip",
               "browser_download_url": "https://github.com/o/r/x.zip", "size": 5 }]
            """;

        var release = GitHubReleaseParser.Parse(Payload(assets: assets));

        Assert.False(release!.HasInstaller);
    }

    [Fact]
    public void Ignores_a_draft_release()
    {
        // A draft has no public download, so it is not an update anyone could take.
        Assert.Null(GitHubReleaseParser.Parse(Payload(draft: true)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("not json at all")]
    [InlineData("[]")]                                  // an array, e.g. /releases instead of /latest
    [InlineData("\"a string\"")]
    [InlineData("{}")]                                  // no tag
    [InlineData("""{ "tag_name": "nightly" }""")]       // a tag that is not a version
    [InlineData("""{ "tag_name": "" }""")]
    public void Returns_nothing_for_a_payload_it_cannot_use(string? json)
    {
        Assert.Null(GitHubReleaseParser.Parse(json));
    }

    /// <summary>
    /// Fields this app does not need must not be required either — GitHub adds and removes them,
    /// and a missing "name" or "published_at" is not a reason to refuse an update.
    /// </summary>
    [Fact]
    public void Copes_with_a_minimal_release()
    {
        var release = GitHubReleaseParser.Parse("""
            { "tag_name": "v2.0.0", "html_url": "https://github.com/o/r/releases/tag/v2.0.0" }
            """);

        Assert.NotNull(release);
        Assert.Equal("2.0.0", release.Version.ToString());
        Assert.Null(release.Title);
        Assert.Null(release.Notes);
        Assert.Null(release.PublishedAt);
        Assert.False(release.HasInstaller);
    }
}
