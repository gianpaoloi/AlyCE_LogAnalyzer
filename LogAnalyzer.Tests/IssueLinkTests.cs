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
    public void Footer_is_appended_after_the_text_with_a_separator()
    {
        var footer = IssueLink.Footer("AlyCE Log Analyzer 1.2.3+abc");

        var query = Query(IssueLink.Build(Repo, IssueKind.Bug, "T", "Steps", footer));

        Assert.Equal("Steps\n\n---\nAlyCE Log Analyzer 1.2.3+abc", query["body"]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Footer_is_empty_when_there_is_nothing_to_add(string? content)
    {
        Assert.Equal(string.Empty, IssueLink.Footer(content));
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
    public void A_very_long_text_is_shortened_to_fit_and_the_footer_is_kept_whole()
    {
        // Every character encodes to three characters, so this would overflow the limit many times over.
        var text = string.Concat(Enumerable.Repeat("é€", 5000));
        var footer = IssueLink.Footer("### Environment\n- App version: 1.2.3");

        var url = IssueLink.Build(Repo, IssueKind.Bug, "T", text, footer);

        Assert.True(url.Length <= IssueLink.MaxUrlLength, $"URL is {url.Length} characters");
        var body = Query(url)["body"];
        Assert.Contains("description shortened to fit", body);
        Assert.EndsWith("- App version: 1.2.3", body);
    }

    [Fact]
    public void A_short_text_is_not_marked_as_shortened()
    {
        var body = Query(IssueLink.Build(Repo, IssueKind.Bug, "T", "Short", IssueLink.Footer("v")))["body"];

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

    [Fact]
    public void Search_url_queries_issues_for_the_title()
    {
        var url = IssueLink.SearchUrl(Repo, "Export ignores & time");

        const string prefix = $"https://github.com/{Repo}/issues?q=";
        Assert.StartsWith(prefix, url);
        Assert.Equal("is:issue Export ignores & time", Uri.UnescapeDataString(url[prefix.Length..]));
    }

    [Fact]
    public void Bug_body_lists_only_the_answered_sections_in_order()
    {
        var body = IssueLink.BugBody(new BugSteps(
            Page: "Explorer",
            Steps: "1. Load the folder\n2. Click Download",
            Expected: "  ",
            Actual: "The file is empty",
            Frequency: "Every time",
            Input: null));

        Assert.Equal(
            "### Where in the app\nExplorer\n\n" +
            "### Steps to reproduce\n1. Load the folder\n2. Click Download\n\n" +
            "### Actual behaviour\nThe file is empty\n\n" +
            "### How often it happens\nEvery time",
            body);
    }

    [Fact]
    public void Bug_body_is_empty_when_nothing_is_answered()
    {
        Assert.Equal(string.Empty, IssueLink.BugBody(new BugSteps(null, null, null, null, null, null)));
    }
}
