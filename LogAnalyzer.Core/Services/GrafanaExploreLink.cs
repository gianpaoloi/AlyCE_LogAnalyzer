using System.Text.RegularExpressions;

namespace LogAnalyzer.Services;

/// <summary>
/// What a Grafana Explore link (the address bar URL of a Loki logs view) says: the Grafana address,
/// the Loki datasource uid, the team / service_name / environment label filters and the time window.
/// Empty strings or a null window mean the link did not say.
/// </summary>
public sealed record GrafanaExploreLink(
    string BaseUrl,
    string DatasourceUid,
    string Team,
    string ServiceName,
    string Environment,
    int? WindowMinutes,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null);

/// <summary>
/// Reads a Grafana Explore link pasted from the browser. Nothing is fetched: the link only tells the
/// app which query the team was looking at, so the same selectors can be loaded without retyping them.
/// </summary>
public static partial class GrafanaExploreLinkParser
{
    // Grafana writes a multi-value label filter as "__CVΩ__value1,value2" (Ω is U+03A9).
    private const string MultiValuePrefix = "__CVΩ__";

    /// <summary>Returns null when the text is not an http(s) link with Explore filters or a datasource.</summary>
    public static GrafanaExploreLink? TryParse(string text)
    {
        if (!Uri.TryCreate(text.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            return null;
        }

        var pairs = ParseQuery(uri.Query);
        var datasource = pairs.LastOrDefault(p => p.Key == "var-ds").Value ?? "";
        var filters = pairs.Where(p => p.Key == "var-filters").Select(p => p.Value).ToList();

        var team = FilterValue(filters, "team");
        var service = FilterValue(filters, "service_name");
        var environment = FilterValue(filters, "environment");

        if (string.IsNullOrEmpty(datasource) && team is null && service is null && environment is null)
            return null;

        var from = pairs.LastOrDefault(p => p.Key == "from").Value ?? "";
        var to = pairs.LastOrDefault(p => p.Key == "to").Value ?? "";

        return new GrafanaExploreLink(
            BaseUrl: uri.GetLeftPart(UriPartial.Authority),
            DatasourceUid: datasource,
            Team: team ?? "",
            ServiceName: service ?? "",
            Environment: environment ?? "",
            WindowMinutes: RelativeWindowMinutes(from),
            From: AbsoluteTime(from),
            To: AbsoluteTime(to));
    }

    /// <summary>An absolute time in the link is epoch milliseconds. "now" and relative values give null.</summary>
    private static DateTimeOffset? AbsoluteTime(string value) =>
        long.TryParse(value, out var ms) ? DateTimeOffset.FromUnixTimeMilliseconds(ms) : null;

    /// <summary>The first value of a label filter, e.g. "tse" from <c>team|=|__CVΩ__tse,tse</c>.</summary>
    private static string? FilterValue(IEnumerable<string> filters, string label)
    {
        foreach (var filter in filters)
        {
            var parts = filter.Split('|', 3);
            if (parts.Length != 3 || parts[0] != label) continue;

            var value = parts[2];
            if (value.StartsWith(MultiValuePrefix, StringComparison.Ordinal))
                value = value[MultiValuePrefix.Length..];

            var first = value.Split(',').Select(v => v.Trim()).FirstOrDefault(v => v.Length > 0);
            if (first is not null) return first;
        }

        return null;
    }

    /// <summary>Turns Grafana's relative <c>now-7d</c> style start into minutes. Absolute times give null.</summary>
    private static int? RelativeWindowMinutes(string from)
    {
        var match = RelativeRange().Match(from);
        if (!match.Success || !int.TryParse(match.Groups[1].Value, out var amount)) return null;

        var minutesPerUnit = match.Groups[2].Value switch
        {
            "m" => 1,
            "h" => 60,
            "d" => 1440,
            "w" => 10080,
            _ => 0,
        };
        return minutesPerUnit == 0 ? null : amount * minutesPerUnit;
    }

    [GeneratedRegex(@"^now-(\d+)([mhdw])$")]
    private static partial Regex RelativeRange();

    private static List<(string Key, string Value)> ParseQuery(string query)
    {
        var pairs = new List<(string Key, string Value)>();
        foreach (var part in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = part.IndexOf('=');
            var key = Unescape(eq < 0 ? part : part[..eq]);
            var value = eq < 0 ? "" : Unescape(part[(eq + 1)..]);
            pairs.Add((key, value));
        }

        return pairs;
    }

    private static string Unescape(string text) => Uri.UnescapeDataString(text.Replace('+', ' '));
}
