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
/// Builds the address of a pre-filled "new issue" page on GitHub.
/// <para>
/// The app does not post anything itself. It opens the user's browser on GitHub with the title,
/// body and label already filled in, and the user presses Submit there — so the app needs no
/// token, holds no credentials, and sends nothing the user has not looked at. The same route the
/// About link and the update page already take out of the app.
/// </para>
/// <para>
/// GitHub will not accept a URL of arbitrary length, so the body is cut to a size that survives
/// percent-encoding. A report that long is better edited on GitHub than sent truncated silently,
/// and the cut is marked so it is visible.
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
    /// <param name="description">The user's own words.</param>
    /// <param name="version">
    /// The running app version, appended to the body so a report can be tied to a build. Null or
    /// blank leaves it out.
    /// </param>
    public static string Build(string repository, IssueKind kind, string? title, string? description, string? version)
    {
        if (string.IsNullOrWhiteSpace(repository))
            throw new ArgumentException("A repository is required.", nameof(repository));

        var cleanTitle = Clip((title ?? string.Empty).Trim(), MaxTitleLength);
        var label = kind == IssueKind.Bug ? "bug" : "enhancement";
        var footer = string.IsNullOrWhiteSpace(version)
            ? string.Empty
            : $"\n\n---\nAlyCE Log Analyzer {version.Trim()}";

        var text = (description ?? string.Empty).Trim();
        var body = text + footer;

        // Shrink the description until the whole encoded URL fits. The footer is kept whole: the
        // version is the part a maintainer needs most and the part that is never the long one.
        var url = Compose(repository, cleanTitle, body, label);
        while (url.Length > MaxUrlLength && text.Length > 0)
        {
            text = text[..Math.Max(0, text.Length - 100)].TrimEnd();
            body = text + Truncated + footer;
            url = Compose(repository, cleanTitle, body, label);
        }

        return url;
    }

    private static string Compose(string repository, string title, string body, string label) =>
        $"https://github.com/{repository.Trim().Trim('/')}/issues/new" +
        $"?title={Uri.EscapeDataString(title)}" +
        $"&body={Uri.EscapeDataString(body)}" +
        $"&labels={Uri.EscapeDataString(label)}";

    private static string Clip(string value, int max) =>
        value.Length <= max ? value : value[..max];
}
