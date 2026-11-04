using LogAnalyzer.Services;
using Xunit;

namespace LogAnalyzer.Tests;

public class IssueLinkTests
{
    private const string Repo = "gianpaoloi/AlyCE_LogAnalyzer";

    /// <summary>Splits a built URL's query string into decoded name/value pairs.</summary>
    private static Dictionary<string, string> Query(string url)
    {
        var query = url[(url.IndexOf('?') + 1)..];
        return query.Split('&').ToDictionary(
            p => Uri.UnescapeDataString(p[..p.IndexOf('=')]),
            p => Uri.UnescapeDataString(p[(p.IndexOf('=') + 1)..]));
    }

    [Fact]
    public void Points_at_the_new_issue_page_of_the_repository()
    {
        var url = IssueLink.Build(Repo, IssueKind.Feature, "Title", "Body", null);

        Assert.StartsWith($"https://github.com/{Repo}/issues/new?", url);
    }

    [Theory]
    [InlineData(IssueKind.Feature, "enhancement")]
    [InlineData(IssueKind.Bug, "bug")]
    public void Kind_chooses_the_label(IssueKind kind, string label)
    {
        var query = Query(IssueLink.Build(Repo, kind, "T", "B", null));

        Assert.Equal(label, query["labels"]);
    }

    [Fact]
    public void Title_and_body_round_trip_through_encoding()
    {
        const string title = "Filter & \"time\" ? #1 — ünïcode";
        const string details = "Line one\nLine two with 100% & more=less+plus";

        var query = Query(IssueLink.Build(Repo, IssueKind.Bug, title, details, null));

        Assert.Equal(title, query["title"]);
        Assert.Equal(details, query["body"]);
    }

    [Fact]
    public void Version_is_appended_to_the_body_when_given()
    {
        var query = Query(IssueLink.Build(Repo, IssueKind.Bug, "T", "Steps", "1.2.3+abc"));

        Assert.Equal("Steps\n\n---\nAlyCE Log Analyzer 1.2.3+abc", query["body"]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Version_is_left_out_when_absent(string? version)
    {
        var query = Query(IssueLink.Build(Repo, IssueKind.Feature, "T", "Only this", version));

        Assert.Equal("Only this", query["body"]);
    }

    [Fact]
    public void Title_is_trimmed_and_clipped_to_the_maximum()
    {
        var longTitle = "  " + new string('x', IssueLink.MaxTitleLength + 50);

        var query = Query(IssueLink.Build(Repo, IssueKind.Feature, longTitle, "B", null));

        Assert.Equal(IssueLink.MaxTitleLength, query["title"].Length);
        Assert.Equal('x', query["title"][0]);
    }

    [Fact]
    public void A_very_long_description_is_shortened_to_fit_and_keeps_the_version()
    {
        // Every character encodes to three characters, so this would overflow the limit many times over.
        var details = string.Concat(Enumerable.Repeat("é€", 5000));

        var url = IssueLink.Build(Repo, IssueKind.Bug, "T", details, "1.2.3");

        Assert.True(url.Length <= IssueLink.MaxUrlLength, $"URL is {url.Length} characters");
        var body = Query(url)["body"];
        Assert.Contains("description shortened to fit", body);
        Assert.EndsWith("AlyCE Log Analyzer 1.2.3", body);
    }

    [Fact]
    public void A_short_description_is_not_marked_as_shortened()
    {
        var body = Query(IssueLink.Build(Repo, IssueKind.Bug, "T", "Short", "1.2.3"))["body"];

        Assert.DoesNotContain("shortened", body);
    }

    [Fact]
    public void Repository_slashes_are_tidied()
    {
        var url = IssueLink.Build($"/{Repo}/", IssueKind.Feature, "T", "B", null);

        Assert.StartsWith($"https://github.com/{Repo}/issues/new?", url);
    }

    [Fact]
    public void Blank_repository_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => IssueLink.Build(" ", IssueKind.Feature, "T", "B", null));
    }
}
