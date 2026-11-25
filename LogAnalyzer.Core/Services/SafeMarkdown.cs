using System.Net;
using System.Text.RegularExpressions;
using Markdig;

namespace LogAnalyzer.Services;

/// <summary>
/// Renders Markdown to HTML for the pages to insert as markup, with the HTML in the source neutralised.
/// <para>
/// A Markdown file can come from outside the app (a README uploaded for a language), so nothing in it is
/// trusted to be markup. Raw HTML tags are shown as text rather than rendered, and a link or image may only
/// point at a web address, a mail address, or a place on the page. Anything else, such as
/// <c>javascript:</c> or <c>data:</c>, is replaced with a harmless target.
/// </para>
/// </summary>
public static class SafeMarkdown
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .DisableHtml()
        .Build();

    /// <summary>Matches an <c>href</c> or <c>src</c> attribute as Markdig writes it, with double quotes.</summary>
    private static readonly Regex UrlAttribute = new(@"\b(href|src)=""([^""]*)""", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>Whitespace and control characters, which browsers ignore inside a URL scheme.</summary>
    private static readonly Regex Invisible = new(@"[\s\x00-\x1F\x7F]+", RegexOptions.Compiled);

    /// <summary>
    /// What comes before the first colon, when no <c>/</c> or <c>?</c> comes before it. That is the scheme
    /// position, and it is checked even when it holds odd characters, so an obfuscated scheme is refused too.
    /// </summary>
    private static readonly Regex Scheme = new(@"^([^/?]+):", RegexOptions.Compiled);

    private static readonly HashSet<string> LinkSchemes = new(StringComparer.OrdinalIgnoreCase) { "http", "https", "mailto" };
    private static readonly HashSet<string> ImageSchemes = new(StringComparer.OrdinalIgnoreCase) { "http", "https" };

    /// <summary>Renders the Markdown. The result is safe to insert as markup.</summary>
    public static string ToHtml(string? markdown) =>
        Neutralise(Markdown.ToHtml(markdown ?? string.Empty, Pipeline));

    /// <summary>Replaces each link or image target that uses a scheme outside its allowed set.</summary>
    internal static string Neutralise(string html) =>
        UrlAttribute.Replace(html, match =>
        {
            var isLink = match.Groups[1].Value.Equals("href", StringComparison.OrdinalIgnoreCase);
            var allowed = isLink ? LinkSchemes : ImageSchemes;
            if (IsAllowed(match.Groups[2].Value, allowed)) return match.Value;

            // A link goes nowhere; an image shows nothing.
            return isLink ? "href=\"#\"" : "src=\"\"";
        });

    private static bool IsAllowed(string url, HashSet<string> schemes)
    {
        // The browser decodes character references and skips spaces and control characters before it
        // reads the scheme, so "java&#x09;script:" must be read the same way here.
        var value = Invisible.Replace(WebUtility.HtmlDecode(url), string.Empty);
        var scheme = Scheme.Match(value);
        return !scheme.Success || schemes.Contains(scheme.Groups[1].Value.Trim());
    }
}
