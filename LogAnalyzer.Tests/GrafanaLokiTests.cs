using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using LogAnalyzer.Services;
using Xunit;

namespace LogAnalyzer.Tests;

public class GrafanaLokiTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);

    private static LokiQuery Query(string team = "tse", string service = "webapi-fin", string env = "dev") =>
        new("https://grafana.example", "uid1", team, service, env, TimeSpan.FromHours(1));

    /// <summary>A Loki query_range response holding one stream with the given (nanoseconds, line) entries.</summary>
    private static HttpResponseMessage LokiPage(params (long Ns, string Line)[] entries)
    {
        var body = JsonSerializer.Serialize(new
        {
            status = "success",
            data = new
            {
                resultType = "streams",
                result = new[]
                {
                    new
                    {
                        stream = new Dictionary<string, string> { ["team"] = "tse" },
                        values = entries.Select(e => new[] { e.Ns.ToString(), e.Line }).ToArray(),
                    },
                },
            },
        });
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
    }

    /// <summary>Answers each request with the next queued response; an exhausted queue returns an empty page.</summary>
    private sealed class ScriptedHandler : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses;
        public List<Uri> Requests { get; } = new();
        public List<string?> Authorization { get; } = new();
        public List<string> Cookies { get; } = new();

        public ScriptedHandler(params HttpResponseMessage[] responses) =>
            _responses = new Queue<HttpResponseMessage>(responses);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request.RequestUri!);
            Authorization.Add(request.Headers.Authorization?.ToString());
            Cookies.Add(request.Headers.TryGetValues("Cookie", out var cookies) ? string.Join(";", cookies) : "");
            var response = _responses.Count > 0
                ? _responses.Dequeue()
                : LokiPage();
            return Task.FromResult(response);
        }
    }

    private static async Task<(GrafanaLokiClient.Result Result, string[] Lines)> RunAsync(
        ScriptedHandler handler, LokiQuery? query = null)
    {
        using var client = new GrafanaLokiClient(handler);
        using var output = new MemoryStream();
        var result = await client.DownloadAsync(query ?? Query(), new GrafanaCredential(Token: "tok"), output, Now, CancellationToken.None);
        var text = Encoding.UTF8.GetString(output.ToArray());
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        return (result, lines);
    }

    [Fact]
    public void BuildSelector_lists_the_set_labels_in_order()
    {
        Assert.Equal("{team=\"tse\", service_name=\"webapi-fin\", environment=\"dev\"}",
                     GrafanaLokiClient.BuildSelector(Query()));
    }

    [Fact]
    public void BuildSelector_leaves_out_empty_labels()
    {
        Assert.Equal("{environment=\"dev\"}", GrafanaLokiClient.BuildSelector(Query(team: " ", service: "", env: "dev")));
    }

    [Fact]
    public void BuildSelector_escapes_quotes_and_backslashes()
    {
        Assert.Equal("{service_name=\"a\\\"b\\\\c\"}",
                     GrafanaLokiClient.BuildSelector(Query(team: "", service: "a\"b\\c", env: "")));
    }

    [Fact]
    public void BuildSelector_refuses_a_query_with_no_labels()
    {
        Assert.Throws<ArgumentException>(() => GrafanaLokiClient.BuildSelector(Query(team: "", service: "", env: "")));
    }

    [Fact]
    public async Task Writes_one_page_in_order_and_sends_the_bearer_token()
    {
        var handler = new ScriptedHandler(LokiPage((1L, "first"), (2L, "second")));

        var (result, lines) = await RunAsync(handler);

        Assert.Equal(2, result.Written);
        Assert.Equal(new[] { "first", "second" }, lines);
        Assert.Equal("Bearer tok", handler.Authorization[0]);
        Assert.StartsWith("https://grafana.example/api/datasources/proxy/uid/uid1/loki/api/v1/query_range?",
                          handler.Requests[0].ToString());
        Assert.Contains("direction=forward", handler.Requests[0].ToString());
    }

    [Fact]
    public async Task Pages_forward_and_does_not_repeat_the_boundary_timestamp()
    {
        // A full first page: the last timestamp (1000 + 4999) is shared with the start of page two.
        var firstPage = Enumerable.Range(0, GrafanaLokiClient.PageSize)
            .Select(i => (1000L + i, $"line{i}"))
            .ToArray();
        var secondPage = new[]
        {
            (1000L + 4999, "line4999"),   // repeated boundary line: must be skipped
            (6000L, "line5000"),
            (6001L, "line5001"),
        };
        var handler = new ScriptedHandler(LokiPage(firstPage), LokiPage(secondPage));

        var (result, lines) = await RunAsync(handler);

        Assert.Equal(GrafanaLokiClient.PageSize + 2, result.Written);
        Assert.Equal(GrafanaLokiClient.PageSize + 2, lines.Length);
        Assert.Equal("line5001", lines[^1]);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Contains("start=5999", handler.Requests[1].ToString());
    }

    [Theory]
    [InlineData("abc123")]
    [InlineData("grafana_session=abc123")]
    public async Task A_session_cookie_is_sent_as_the_grafana_session_cookie(string pasted)
    {
        var handler = new ScriptedHandler(LokiPage((1L, "only")));
        using var client = new GrafanaLokiClient(handler);
        using var output = new MemoryStream();

        await client.DownloadAsync(Query(), new GrafanaCredential(SessionCookie: pasted), output, Now, CancellationToken.None);

        Assert.Equal("grafana_session=abc123", handler.Cookies[0]);
        Assert.Null(handler.Authorization[0]);
    }

    [Fact]
    public void An_empty_credential_is_detected()
    {
        Assert.True(new GrafanaCredential(Token: " ", SessionCookie: null).IsEmpty);
        Assert.False(new GrafanaCredential(SessionCookie: "x").IsEmpty);
    }

    [Fact]
    public async Task A_cancelled_download_stops_with_a_cancellation()
    {
        using var client = new GrafanaLokiClient(new ScriptedHandler(LokiPage((1L, "never"))));
        using var output = new MemoryStream();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.DownloadAsync(Query(), new GrafanaCredential(Token: "tok"), output, Now, cts.Token));
    }

    [Fact]
    public async Task Progress_is_reported_after_each_page()
    {
        var firstPage = Enumerable.Range(0, GrafanaLokiClient.PageSize).Select(i => (1000L + i, $"line{i}")).ToArray();
        var reported = new List<int>();
        using var client = new GrafanaLokiClient(new ScriptedHandler(LokiPage(firstPage), LokiPage((9000L, "last"))));
        using var output = new MemoryStream();

        await client.DownloadAsync(Query(), new GrafanaCredential(Token: "tok"), output, Now, CancellationToken.None,
                                   onPage: reported.Add);

        Assert.Equal(new[] { GrafanaLokiClient.PageSize, GrafanaLokiClient.PageSize + 1 }, reported);
    }

    [Fact]
    public async Task An_empty_result_writes_nothing()
    {
        var (result, lines) = await RunAsync(new ScriptedHandler(LokiPage()));

        Assert.Equal(0, result.Written);
        Assert.Empty(lines);
    }

    [Fact]
    public async Task A_rejected_token_is_reported_in_plain_words()
    {
        var denied = new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent("{\"message\":\"invalid token\"}"),
        };

        var ex = await Assert.ThrowsAsync<InvalidDataException>(() => RunAsync(new ScriptedHandler(denied)));

        Assert.Contains("rejected the service account token", ex.Message);
    }

    [Fact]
    public async Task A_refused_session_cookie_is_reported_as_a_cookie_not_a_token()
    {
        var denied = new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent("{\"message\":\"unauthorized\"}"),
        };
        using var client = new GrafanaLokiClient(new ScriptedHandler(denied));
        using var output = new MemoryStream();

        var ex = await Assert.ThrowsAsync<InvalidDataException>(() =>
            client.DownloadAsync(Query(), new GrafanaCredential(SessionCookie: "abc"), output, Now, CancellationToken.None));

        Assert.Contains("grafana_session cookie", ex.Message);
        Assert.DoesNotContain("token", ex.Message);
    }

    [Fact]
    public async Task Lists_only_the_loki_datasources_with_their_uid_and_name()
    {
        const string json = """
            [
              {"uid":"prom1","name":"Prometheus","type":"prometheus"},
              {"uid":"bf9o2vkid4w00c","name":"grafanacloud-logs","type":"loki"},
              {"uid":"loki2","name":"","type":"loki"}
            ]
            """;
        var handler = new ScriptedHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        });
        using var client = new GrafanaLokiClient(handler);

        var found = await client.ListLokiDatasourcesAsync("https://grafana.example", new GrafanaCredential(Token: "tok"), CancellationToken.None);

        Assert.Equal(new[] { "bf9o2vkid4w00c", "loki2" }, found.Select(d => d.Uid));
        Assert.Equal("grafanacloud-logs", found[0].Name);
        Assert.Equal("loki2", found[1].Name);   // an unnamed datasource falls back to its uid
        Assert.Equal("Bearer tok", handler.Authorization[0]);
        Assert.Equal("https://grafana.example/api/datasources", handler.Requests[0].ToString());
    }

    [Fact]
    public async Task A_response_that_is_not_a_loki_result_is_refused()
    {
        var html = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"status\":\"error\"}", Encoding.UTF8, "application/json"),
        };

        var ex = await Assert.ThrowsAsync<InvalidDataException>(() => RunAsync(new ScriptedHandler(html)));

        Assert.Contains("not with a Loki log result", ex.Message);
    }
}
