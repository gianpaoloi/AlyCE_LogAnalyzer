using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace LogAnalyzer.Services;

/// <summary>
/// What to ask Loki for: the stream labels to match and the relative time window ending now.
/// Empty labels are left out of the query, so at least one must be set.
/// </summary>
public sealed record LokiQuery(
    string BaseUrl,
    string DatasourceUid,
    string Team,
    string ServiceName,
    string Environment,
    TimeSpan Window,
    DateTimeOffset? Start = null,
    DateTimeOffset? End = null)
{
    /// <summary>Short name shown as the source of the loaded entries.</summary>
    public string Label
    {
        get
        {
            var parts = new[] { Team, ServiceName, Environment }
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(p => p.Trim());
            return "Grafana " + string.Join(" · ", parts);
        }
    }
}

/// <summary>A Loki datasource the token can see: its uid (used in the proxy URL) and display name.</summary>
public sealed record LokiDatasource(string Uid, string Name);

/// <summary>
/// How the app proves who it is to Grafana. Either a service account token (sent as a Bearer header),
/// or the <c>grafana_session</c> cookie of a browser session that is already signed in through SSO.
/// </summary>
public sealed record GrafanaCredential(string? Token = null, string? SessionCookie = null)
{
    private const string CookieName = "grafana_session";

    public bool IsEmpty => string.IsNullOrWhiteSpace(Token) && string.IsNullOrWhiteSpace(SessionCookie);

    public void Apply(HttpRequestMessage request)
    {
        if (!string.IsNullOrWhiteSpace(Token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token.Trim());
            return;
        }

        if (!string.IsNullOrWhiteSpace(SessionCookie))
        {
            // Accept the bare value or the whole "grafana_session=..." pair, as copied from the browser.
            var value = SessionCookie.Trim();
            if (value.StartsWith(CookieName + "=", StringComparison.OrdinalIgnoreCase))
                value = value[(CookieName.Length + 1)..];
            request.Headers.Add("Cookie", $"{CookieName}={value}");
        }
    }
}

/// <summary>The dialog's result: what to query and how to authenticate the query.</summary>
public sealed record GrafanaImportRequest(LokiQuery Query, GrafanaCredential Credential);

/// <summary>
/// Downloads log lines from a Grafana Cloud Loki datasource through Grafana's datasource proxy, so the
/// request is authorised by a Grafana service account token (SSO sign-in alone gives no API access).
/// The lines are written in time order as JSON lines, ready for <see cref="LogParser"/>.
/// </summary>
public sealed class GrafanaLokiClient : IDisposable
{
    /// <summary>Loki's default per-request line cap (max_entries_limit_per_query).</summary>
    public const int PageSize = 5000;

    /// <summary>
    /// Safety stop for a runaway query. Lines go to a temp file as they arrive, so this is not a memory
    /// limit; the parser then reads the file like any large <c>.log</c>.
    /// </summary>
    public const int MaxLines = 5_000_000;

    /// <summary>Outcome of a download: how many lines were written.</summary>
    public readonly record struct Result(int Written);

    private readonly HttpClient _http;

    /// <param name="handler">Test seam, like <c>UpdateChecker</c>. Null uses the default network stack.</param>
    public GrafanaLokiClient(HttpMessageHandler? handler = null)
    {
        _http = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        _http.Timeout = TimeSpan.FromMinutes(5);
    }

    /// <summary>
    /// Builds the LogQL stream selector, e.g. <c>{team="tse", service_name="webapi-fin", environment="dev"}</c>.
    /// Values are escaped so a quote or backslash cannot change the query.
    /// </summary>
    /// <exception cref="ArgumentException">No label has a value.</exception>
    public static string BuildSelector(LokiQuery query)
    {
        var matchers = new List<string>(3);
        AddMatcher(matchers, "team", query.Team);
        AddMatcher(matchers, "service_name", query.ServiceName);
        AddMatcher(matchers, "environment", query.Environment);

        if (matchers.Count == 0)
            throw new ArgumentException("Enter at least one of team, service name or environment.");

        return "{" + string.Join(", ", matchers) + "}";
    }

    private static void AddMatcher(List<string> matchers, string label, string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        var escaped = value.Trim().Replace("\\", "\\\\").Replace("\"", "\\\"");
        matchers.Add($"{label}=\"{escaped}\"");
    }

    /// <summary>
    /// Writes every line the query matches in <paramref name="query"/>'s window, oldest first, to
    /// <paramref name="output"/>. Pages forward from the window start; each page after the first
    /// starts at the newest timestamp of the previous one (Loki's start is inclusive), so the lines
    /// that share that timestamp are read again and skipped by content.
    /// </summary>
    /// <exception cref="InvalidDataException">
    /// Grafana refused the request, the response is not a Loki result, or the window is too large.
    /// </exception>
    public async Task<Result> DownloadAsync(LokiQuery query, GrafanaCredential credential, Stream output,
                                            DateTimeOffset now, CancellationToken ct,
                                            Action<int>? onPage = null)
    {
        var selector = BuildSelector(query);
        // An absolute range (from a link) is used as given; otherwise the window ends now.
        var endNs = ToUnixNanoseconds(query.End ?? now);
        var cursorNs = ToUnixNanoseconds(query.Start ?? (now - query.Window));
        var url = $"{query.BaseUrl.TrimEnd('/')}/api/datasources/proxy/uid/" +
                  $"{Uri.EscapeDataString(query.DatasourceUid)}/loki/api/v1/query_range";

        var written = 0;
        var boundaryNs = long.MinValue;
        var boundaryLines = new HashSet<string>(StringComparer.Ordinal);

        await using var writer = new StreamWriter(output, new UTF8Encoding(false), bufferSize: 64 * 1024, leaveOpen: true)
        {
            NewLine = "\n",
        };

        while (true)
        {
            var page = await FetchPageAsync(url, selector, credential, cursorNs, endNs, ct);
            if (page.Count == 0) break;

            page.Sort((a, b) => a.Ns.CompareTo(b.Ns));

            var writtenBefore = written;
            foreach (var (ns, line) in page)
            {
                if (ns == boundaryNs && boundaryLines.Contains(line)) continue;

                // A raw line break would split one record in two, as in GrafanaExport.
                await writer.WriteLineAsync(line.Replace('\r', ' ').Replace('\n', ' ').AsMemory(), ct);
                written++;
                if (written > MaxLines)
                    throw new InvalidDataException(
                        $"More than {MaxLines:N0} lines match. Narrow the time range or add a label.");
            }

            onPage?.Invoke(written);

            // A short page means the window is exhausted.
            if (page.Count < PageSize) break;

            var newestNs = page[^1].Ns;
            if (newestNs >= endNs) break;

            if (written == writtenBefore)
                throw new InvalidDataException(
                    $"Grafana returned more than {PageSize:N0} lines with the same timestamp. Narrow the query.");

            boundaryNs = newestNs;
            boundaryLines = page.Where(e => e.Ns == newestNs).Select(e => e.Line).ToHashSet(StringComparer.Ordinal);
            cursorNs = newestNs;
        }

        await writer.FlushAsync(ct);
        return new Result(written);
    }

    private async Task<List<(long Ns, string Line)>> FetchPageAsync(
        string url, string selector, GrafanaCredential credential, long startNs, long endNs, CancellationToken ct)
    {
        var requestUri = url +
            $"?query={Uri.EscapeDataString(selector)}&start={startNs}&end={endNs}" +
            $"&limit={PageSize}&direction=forward";

        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        credential.Apply(request);

        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new InvalidDataException(DescribeFailure(response.StatusCode, body, credential));
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        return ReadEntries(doc.RootElement);
    }

    /// <summary>Reads <c>data.result[].values[]</c>, where each value is <c>[nanoseconds, line]</c>.</summary>
    private static List<(long Ns, string Line)> ReadEntries(JsonElement root)
    {
        if (!root.TryGetProperty("status", out var status) || status.GetString() != "success" ||
            !root.TryGetProperty("data", out var data) ||
            !data.TryGetProperty("result", out var streams) || streams.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("Grafana answered, but not with a Loki log result.");
        }

        var entries = new List<(long Ns, string Line)>();
        foreach (var stream in streams.EnumerateArray())
        {
            if (!stream.TryGetProperty("values", out var values) || values.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var value in values.EnumerateArray())
            {
                if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() < 2) continue;
                var ts = value[0].GetString();
                var line = value[1].GetString();
                if (ts is null || line is null || !long.TryParse(ts, out var ns)) continue;
                entries.Add((ns, line));
            }
        }

        return entries;
    }

    /// <summary>
    /// Lists the Loki datasources the token can see (<c>GET /api/datasources</c>), so the uid does not
    /// have to be copied by hand from an Explore link.
    /// </summary>
    public async Task<IReadOnlyList<LokiDatasource>> ListLokiDatasourcesAsync(
        string baseUrl, GrafanaCredential credential, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl.TrimEnd('/')}/api/datasources");
        credential.Apply(request);

        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new InvalidDataException(DescribeFailure(response.StatusCode, body, credential));
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Grafana answered, but not with a datasource list.");

        var found = new List<LokiDatasource>();
        foreach (var ds in doc.RootElement.EnumerateArray())
        {
            if (!ds.TryGetProperty("type", out var type) || type.GetString() != "loki") continue;
            if (!ds.TryGetProperty("uid", out var uidValue) || uidValue.GetString() is not { Length: > 0 } uid) continue;

            var name = ds.TryGetProperty("name", out var nameValue) ? nameValue.GetString() : null;
            found.Add(new LokiDatasource(uid, string.IsNullOrWhiteSpace(name) ? uid : name));
        }

        return found;
    }

    private static string DescribeFailure(HttpStatusCode code, string body, GrafanaCredential credential)
    {
        var snippet = body.Length > 300 ? body[..300] + "…" : body;
        var usingToken = !string.IsNullOrWhiteSpace(credential.Token);
        return code switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden when usingToken =>
                $"Grafana rejected the service account token (HTTP {(int)code}). Check that it was copied in full " +
                "and that its service account has at least Viewer access.",
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                $"Grafana did not accept the grafana_session cookie (HTTP {(int)code}). It may have expired or been " +
                "copied incorrectly: sign in to Grafana in the browser again, copy the grafana_session value " +
                "again, and paste it into Sign-in.",
            HttpStatusCode.NotFound =>
                "Grafana could not find that Loki datasource (HTTP 404). Check the datasource uid.",
            _ => $"Grafana returned HTTP {(int)code}: {snippet.Trim()}",
        };
    }

    private static long ToUnixNanoseconds(DateTimeOffset time) => time.ToUnixTimeMilliseconds() * 1_000_000L;

    public void Dispose() => _http.Dispose();
}
