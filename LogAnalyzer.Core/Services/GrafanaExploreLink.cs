using System.Text.RegularExpressions;

namespace LogAnalyzer.Services;

/// <summary>
/// What a Grafana Explore link (the address bar URL of a Loki logs view) says: the Grafana address,
/// the Loki datasource uid, the team / service_name / environment label filters, the time window, and
/// the level / field / line-text filters. Empty strings or a null window mean the link did not say.
/// </summary>
public sealed record GrafanaExploreLink(
    string BaseUrl,
    string DatasourceUid,
    string Team,
    string ServiceName,
    string Environment,
    int? WindowMinutes,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    IReadOnlyList<LokiFieldMatch>? Fields = null,
    IReadOnlyList<LokiLineFilter>? LineFilters = null);

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
            To: AbsoluteTime(to),
            Fields: FieldMatches(pairs, "var-levels").Concat(FieldMatches(pairs, "var-fields")).ToList(),
            LineFilters: LineFilters(pairs));
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

    /// <summary>
    /// Single-value filters (<c>name|=|value</c>) from the level and field pickers, e.g.
    /// <c>detected_level|=|error</c> or <c>cid|=|…</c>. Only plain equality is read: a "not equal" or
    /// regex filter is skipped rather than guessed.
    /// </summary>
    private static List<LokiFieldMatch> FieldMatches(IEnumerable<(string Key, string Value)> pairs, string key)
    {
        var matches = new List<LokiFieldMatch>();
        foreach (var (_, filter) in pairs.Where(p => p.Key == key))
        {
            var parts = filter.Split('|', 3);
            if (parts.Length != 3 || parts[1] != "=" || !GrafanaLokiClient.IsLabelName(parts[0])) continue;

            var value = FieldValue(parts[2]);
            if (value.Length > 0) matches.Add(new LokiFieldMatch(parts[0], value));
        }

        return matches;
    }

    /// <summary>
    /// The value of a field filter. A field parsed from the JSON line is written as
    /// <c>{"parser":"json"__gfc__"value":"axoteam"},axoteam</c>; the value is taken from inside the braces.
    /// Otherwise it is the first value, as for the label filters.
    /// </summary>
    private static string FieldValue(string raw)
    {
        // The multi-value prefix can come before the wrapper, so it is removed first.
        var text = raw.StartsWith(MultiValuePrefix, StringComparison.Ordinal) ? raw[MultiValuePrefix.Length..] : raw;
        if (text.StartsWith('{'))
        {
            var inner = FieldJsonValue().Match(text);
            if (inner.Success) return inner.Groups[1].Value;
        }

        return text.Split(',').Select(v => v.Trim()).FirstOrDefault(v => v.Length > 0) ?? "";
    }

    /// <summary>Line text filters (<c>flags|=|text</c>). A "caseInsensitive" flag makes the match ignore case.</summary>
    private static List<LokiLineFilter> LineFilters(IEnumerable<(string Key, string Value)> pairs)
    {
        var filters = new List<LokiLineFilter>();
        foreach (var (_, filter) in pairs.Where(p => p.Key == "var-lineFilters"))
        {
            var parts = filter.Split('|', 3);
            if (parts.Length != 3 || parts[1].Replace("__gfp__", "") != "=" || parts[2].Length == 0) continue;

            var caseInsensitive = parts[0].Split(',').Contains("caseInsensitive");
            filters.Add(new LokiLineFilter(parts[2], caseInsensitive));
        }

        return filters;
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

    [GeneratedRegex("\"value\"\\s*:\\s*\"([^\"]*)\"")]
    private static partial Regex FieldJsonValue();

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
