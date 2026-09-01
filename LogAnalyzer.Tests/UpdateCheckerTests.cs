using System.Net;
using LogAnalyzer.Services.Updates;
using Xunit;

namespace LogAnalyzer.Tests;

public class UpdateCheckerTests
{
    private const string Repository = "gianpaoloi/AlyCE_LogAnalyzer";

    private static string Release(string tag) => $$"""
        {
          "tag_name": "{{tag}}",
          "html_url": "https://github.com/{{Repository}}/releases/tag/{{tag}}",
          "assets": []
        }
        """;

    private static UpdateChecker Checker(StubTransport transport, string current = "1.2.3",
                                         UpdateOptions? options = null) =>
        new(options ?? new UpdateOptions { Repository = Repository }, transport, current);

    [Fact]
    public async Task Reports_an_update_when_the_latest_release_is_newer()
    {
        using var transport = new StubTransport(HttpStatusCode.OK, Release("v1.3.0"));
        using var checker = Checker(transport, current: "1.2.3");

        var result = await checker.CheckAsync();

        Assert.Equal(UpdateStatus.UpdateAvailable, result.Status);
        Assert.True(result.IsUpdateAvailable);
        Assert.Equal("1.3.0", result.Release!.Version.ToString());
    }

    [Theory]
    [InlineData("1.3.0")]   // exactly the released version
    [InlineData("1.4.0")]   // a local build ahead of the last release
    public async Task Reports_up_to_date_when_the_release_is_not_newer(string current)
    {
        using var transport = new StubTransport(HttpStatusCode.OK, Release("v1.3.0"));
        using var checker = Checker(transport, current);

        var result = await checker.CheckAsync();

        Assert.Equal(UpdateStatus.UpToDate, result.Status);
        Assert.False(result.IsUpdateAvailable);
        // The release is still reported, so the dialog can say what it compared against.
        Assert.NotNull(result.Release);
    }

    /// <summary>
    /// A dev build stamped 1.2.3 and the released 1.2.3 differ only by commit, and that must not
    /// read as an update — otherwise every developer sees a permanent update prompt.
    /// </summary>
    [Fact]
    public async Task Ignores_the_commit_suffix_when_comparing()
    {
        using var transport = new StubTransport(HttpStatusCode.OK, Release("v1.2.3"));
        using var checker = Checker(transport, current: "1.2.3+fe12a13c9ce22ebbdb92e08c706e42107ed7ff27");

        Assert.Equal(UpdateStatus.UpToDate, (await checker.CheckAsync()).Status);
    }

    [Fact]
    public async Task Asks_the_configured_repository_with_the_headers_github_requires()
    {
        using var transport = new StubTransport(HttpStatusCode.OK, Release("v1.3.0"));
        using var checker = Checker(transport);

        await checker.CheckAsync();

        var request = transport.LastRequest!;
        Assert.Equal($"https://api.github.com/repos/{Repository}/releases/latest",
                     request.RequestUri!.ToString());
        // GitHub rejects anonymous requests that do not identify themselves.
        Assert.Contains("AlyCE-LogAnalyzer", request.Headers.UserAgent.ToString());
        Assert.Contains("application/vnd.github+json", request.Headers.Accept.ToString());
        Assert.True(request.Headers.Contains("X-GitHub-Api-Version"));
    }

    /// <summary>
    /// A version the User-Agent header cannot carry must not throw while the client is being built —
    /// "unknown" is exactly what an unstamped build reports.
    /// </summary>
    [Theory]
    [InlineData("unknown")]
    [InlineData("1.2.3 (local build)")]
    public void Survives_a_current_version_that_is_not_a_valid_header_token(string current)
    {
        using var transport = new StubTransport(HttpStatusCode.OK, Release("v1.3.0"));

        using var checker = Checker(transport, current);

        Assert.Equal(current, checker.CurrentVersion);
    }

    [Fact]
    public async Task Fails_without_asking_when_the_running_version_cannot_be_compared()
    {
        using var transport = new StubTransport(HttpStatusCode.OK, Release("v1.3.0"));
        using var checker = Checker(transport, current: "unknown");

        var result = await checker.CheckAsync();

        Assert.Equal(UpdateStatus.Failed, result.Status);
        Assert.Contains("unknown", result.Message);
        // No point spending a rate-limited request on an answer that could not be used.
        Assert.Equal(0, transport.Calls);
    }

    [Fact]
    public async Task Makes_no_request_when_checking_is_switched_off()
    {
        using var transport = new StubTransport(HttpStatusCode.OK, Release("v1.3.0"));
        using var checker = Checker(transport, options: new UpdateOptions { Enabled = false });

        var result = await checker.CheckAsync();

        Assert.Equal(UpdateStatus.Disabled, result.Status);
        Assert.Equal(0, transport.Calls);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, "no published release")]
    [InlineData(HttpStatusCode.Forbidden, "rate limit")]
    [InlineData(HttpStatusCode.TooManyRequests, "rate limit")]
    [InlineData(HttpStatusCode.InternalServerError, "500")]
    public async Task Turns_an_unhelpful_answer_into_a_failed_check(HttpStatusCode status, string expected)
    {
        using var transport = new StubTransport(status, "");
        using var checker = Checker(transport);

        var result = await checker.CheckAsync();

        Assert.Equal(UpdateStatus.Failed, result.Status);
        Assert.Contains(expected, result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Null(result.Release);
    }

    [Fact]
    public async Task Turns_an_unreachable_github_into_a_failed_check()
    {
        // What a machine with no route, or a blocking proxy, produces.
        using var transport = new StubTransport(_ => throw new HttpRequestException("No such host is known."));
        using var checker = Checker(transport);

        var result = await checker.CheckAsync();

        Assert.Equal(UpdateStatus.Failed, result.Status);
        Assert.Contains("No such host", result.Message);
    }

    [Fact]
    public async Task Turns_an_unreadable_answer_into_a_failed_check()
    {
        // A captive-portal login page is the classic version of this.
        using var transport = new StubTransport(HttpStatusCode.OK, "<html>Sign in to continue</html>");
        using var checker = Checker(transport);

        var result = await checker.CheckAsync();

        Assert.Equal(UpdateStatus.Failed, result.Status);
        Assert.Null(result.Release);
    }

    [Fact]
    public async Task Reuses_the_answer_rather_than_asking_again()
    {
        using var transport = new StubTransport(HttpStatusCode.OK, Release("v1.3.0"));
        using var checker = Checker(transport);

        await checker.CheckAsync();
        await checker.CheckAsync();
        await checker.CheckAsync();

        // Every page render asks; anonymous GitHub requests are capped at 60 per hour per address.
        Assert.Equal(1, transport.Calls);
    }

    [Fact]
    public async Task Asks_again_when_the_check_is_forced()
    {
        using var transport = new StubTransport(HttpStatusCode.OK, Release("v1.3.0"));
        using var checker = Checker(transport);

        await checker.CheckAsync();
        await checker.CheckAsync(force: true);

        Assert.Equal(2, transport.Calls);
    }

    /// <summary>
    /// A failure is held for a much shorter time than a success, so a laptop that was offline when
    /// the app started finds out about an update later in the same session.
    /// </summary>
    [Fact]
    public async Task Retries_sooner_after_a_failure_than_after_a_success()
    {
        var options = new UpdateOptions
        {
            Repository = Repository,
            CheckInterval = TimeSpan.FromHours(6),
            RetryInterval = TimeSpan.Zero,
        };

        using var transport = new StubTransport(HttpStatusCode.InternalServerError, "");
        using var checker = Checker(transport, options: options);

        await checker.CheckAsync();
        await checker.CheckAsync();

        Assert.Equal(2, transport.Calls);
    }

    [Fact]
    public async Task Remembers_the_last_result_without_asking_again()
    {
        using var transport = new StubTransport(HttpStatusCode.OK, Release("v1.3.0"));
        using var checker = Checker(transport);

        Assert.Null(checker.Last);

        var result = await checker.CheckAsync();

        // What lets a component render before the check has finished and fill in the answer after.
        Assert.Same(result, checker.Last);
        Assert.Equal(1, transport.Calls);
    }

    [Fact]
    public async Task Only_one_request_is_made_when_several_callers_ask_at_once()
    {
        using var transport = new StubTransport(HttpStatusCode.OK, Release("v1.3.0"));
        using var checker = Checker(transport);

        // Both hosts render the sidebar and the pages concurrently on first load.
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => checker.CheckAsync()));

        Assert.Equal(1, transport.Calls);
        Assert.All(results, r => Assert.Equal(UpdateStatus.UpdateAvailable, r.Status));
    }

    /// <summary>
    /// Stands in for github.com. Counts requests, because "how often does this app call out to the
    /// internet" is a property worth asserting rather than assuming.
    /// </summary>
    private sealed class StubTransport : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

        public StubTransport(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;

        public StubTransport(HttpStatusCode status, string body)
            : this(_ => new HttpResponseMessage(status) { Content = new StringContent(body) }) { }

        public int Calls { get; private set; }

        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
                                                               CancellationToken cancellationToken)
        {
            Calls++;
            LastRequest = request;
            return Task.FromResult(_respond(request));
        }
    }
}
