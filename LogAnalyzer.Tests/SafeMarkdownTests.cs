using LogAnalyzer.Services;
using Xunit;

namespace LogAnalyzer.Tests;

/// <summary>
/// The Markdown a user uploads for a language is shown as markup, so raw HTML must stay text and
/// links and images must not carry a script-capable address.
/// </summary>
public class SafeMarkdownTests
{
    [Theory]
    [InlineData("<script>alert(1)</script>", "<script")]
    [InlineData("<img src=x onerror=alert(1)>", "<img")]
    [InlineData("<iframe src=\"https://example.com\"></iframe>", "<iframe")]
    [InlineData("<a href=\"javascript:alert(1)\">x</a>", "<a href")]
    public void Raw_html_is_shown_as_text_not_rendered(string markdown, string tagThatMustNotAppear)
    {
        var html = SafeMarkdown.ToHtml(markdown);

        Assert.DoesNotContain(tagThatMustNotAppear, html, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("[click](javascript:alert(1))")]
    [InlineData("[click](JaVaScRiPt:alert(1))")]
    [InlineData("[click](  javascript:alert(1))")]
    [InlineData("[click](java&#x09;script:alert(1))")]
    [InlineData("[click](java&#0000009script:alert(1))")]
    [InlineData("[click](vbscript:msgbox(1))")]
    [InlineData("[click](data:text/html;base64,PHNjcmlwdD4=)")]
    public void A_link_to_a_script_or_data_scheme_is_neutralised(string markdown)
    {
        var html = SafeMarkdown.ToHtml(markdown);

        Assert.Contains("href=\"#\"", html);
        Assert.DoesNotContain("script:", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("data:", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void An_image_with_a_non_web_source_shows_nothing()
    {
        var html = SafeMarkdown.ToHtml("![pic](data:image/svg+xml;base64,PHN2Zy8+)");

        Assert.Contains("src=\"\"", html);
        Assert.DoesNotContain("data:", html);
    }

    [Theory]
    [InlineData("[site](https://example.com/page)", "href=\"https://example.com/page\"")]
    [InlineData("[plain](http://example.com)", "href=\"http://example.com\"")]
    [InlineData("[mail](mailto:someone@example.com)", "href=\"mailto:someone@example.com\"")]
    [InlineData("[file](docs/QUICK-INFO.md)", "href=\"docs/QUICK-INFO.md\"")]
    [InlineData("[section](#install)", "href=\"#install\"")]
    public void Ordinary_links_are_kept(string markdown, string expected)
    {
        Assert.Contains(expected, SafeMarkdown.ToHtml(markdown));
    }

    [Fact]
    public void Ordinary_markdown_still_renders()
    {
        var html = SafeMarkdown.ToHtml("## Title\n\nSome **bold** text and a `code` span.\n\n- one\n- two");

        Assert.Contains("<h2", html);
        Assert.Contains("<strong>bold</strong>", html);
        Assert.Contains("<code>code</code>", html);
        Assert.Contains("<li>one</li>", html);
    }

    [Fact]
    public void Null_renders_as_empty()
    {
        Assert.Equal(string.Empty, SafeMarkdown.ToHtml(null).Trim());
    }
}
