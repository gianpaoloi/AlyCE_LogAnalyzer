namespace LogAnalyzer.Services;

/// <summary>What the user is reporting, which decides the GitHub label the issue is opened with.</summary>
public enum IssueKind
{
    /// <summary>Something the app should do that it does not — labelled <c>enhancement</c>.</summary>
    Feature,

    /// <summary>Something the app does wrong — labelled <c>bug</c>.</summary>
    Bug,
}

/// <summary>
/// The answers a user gives to the bug form's structured questions. Every field is optional: a
/// report with only a title is still a report, and empty sections are left out of the issue.
/// </summary>
public sealed record BugSteps(
    string? Page,
    string? Steps,
    string? Expected,
    string? Actual,
    string? Frequency,
    string? Input);

/// <summary>
/// Builds the address of a pre-filled "new issue" page on GitHub.
/// <para>
/// The app does not post anything itself. It opens the user's browser on GitHub with the title,
/// body and label already filled in, and the user presses Submit there — so the app needs no
/// token, holds no credentials, and sends nothing the user has not looked at. The same route the
/// About link and the update page already take out of the app.
/// </para>
/// <para>
/// GitHub will not accept a URL of arbitrary length, so the text is cut to a size that survives
/// percent-encoding. The footer — the environment block a maintainer needs most — is never cut. A
/// long report is better edited on GitHub than sent truncated silently, and the cut is marked.
/// </para>
/// </summary>
public static class IssueLink
{
    /// <summary>Upper bound on the encoded URL, comfortably under what GitHub and browsers accept.</summary>
    public const int MaxUrlLength = 7000;

    /// <summary>Upper bound on the title, which GitHub shows as a single line.</summary>
    public const int MaxTitleLength = 200;

    private const string Truncated = "\n\n_(description shortened to fit — please add the rest here)_";

    /// <summary>
    /// The URL for a new issue in <paramref name="repository"/> (<c>owner/name</c>).
    /// </summary>
    /// <param name="repository">The repository the issue is filed against.</param>
    /// <param name="kind">Feature or bug; sets the label.</param>
    /// <param name="title">The issue title. Blank titles are allowed — GitHub then asks for one.</param>
    /// <param name="text">The user's own words, which may be shortened to fit.</param>
    /// <param name="footer">Text appended after the user's words, never shortened. See <see cref="Footer"/>.</param>
    public static string Build(string repository, IssueKind kind, string? title, string? text, string? footer)
    {
        if (string.IsNullOrWhiteSpace(repository))
            throw new ArgumentException("A repository is required.", nameof(repository));

        var cleanTitle = Clip((title ?? string.Empty).Trim(), MaxTitleLength);
        var label = kind == IssueKind.Bug ? "bug" : "enhancement";
        var tail = footer?.TrimEnd() ?? string.Empty;

        var words = (text ?? string.Empty).Trim();
        var body = words + tail;

        // Shrink the user's words until the whole encoded URL fits.
        var url = Compose(repository, cleanTitle, body, label);
        while (url.Length > MaxUrlLength && words.Length > 0)
        {
            words = words[..Math.Max(0, words.Length - 100)].TrimEnd();
            body = (words.Length == 0 ? string.Empty : words + Truncated) + tail;
            url = Compose(repository, cleanTitle, body, label);
        }

        return url;
    }

    /// <summary>
    /// A separator and a block of closing text (version, environment) to put after the user's words.
    /// Empty when there is nothing to add.
    /// </summary>
    public static string Footer(string? content) =>
        string.IsNullOrWhiteSpace(content) ? string.Empty : "\n\n---\n" + content.Trim();

    /// <summary>
    /// The URL of GitHub's issue search, pre-filled with <paramref name="text"/>, so a user can see
    /// whether the problem or request is already reported before filing it again.
    /// </summary>
    public static string SearchUrl(string repository, string? text) =>
        $"https://github.com/{Normalise(repository)}/issues?q=" +
        Uri.EscapeDataString("is:issue " + (text ?? string.Empty).Trim());

    /// <summary>
    /// The structured bug questions as Markdown. A section is written only when its answer is not blank,
    /// so the issue never shows empty headings.
    /// </summary>
    public static string BugBody(BugSteps answers)
    {
        var sections = new List<string>();

        void Add(string heading, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
                sections.Add($"### {heading}\n{value.Trim()}");
        }

        Add("Where in the app", answers.Page);
        Add("Input used", answers.Input);
        Add("Steps to reproduce", answers.Steps);
        Add("Expected behaviour", answers.Expected);
        Add("Actual behaviour", answers.Actual);
        Add("How often it happens", answers.Frequency);

        return string.Join("\n\n", sections);
    }

    private static string Normalise(string repository)
    {
        if (string.IsNullOrWhiteSpace(repository))
            throw new ArgumentException("A repository is required.", nameof(repository));
        return repository.Trim().Trim('/');
    }

    private static string Compose(string repository, string title, string body, string label) =>
        $"https://github.com/{Normalise(repository)}/issues/new" +
        $"?title={Uri.EscapeDataString(title)}" +
        $"&body={Uri.EscapeDataString(body)}" +
        $"&labels={Uri.EscapeDataString(label)}";

    private static string Clip(string value, int max) =>
        value.Length <= max ? value : value[..max];
}
