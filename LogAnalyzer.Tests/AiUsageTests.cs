using System.Net;
using System.Text;
using LogAnalyzer.Services.Ai;
using Xunit;

namespace LogAnalyzer.Tests;

public class AiUsageTests
{
    private static readonly DateTime Now = new(2026, 11, 24, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Add_sums_reported_tokens_and_counts_each_request()
    {
        var usage = AiUsage.None
            .Add(new AiResponse("a", 100, 20), Now)
            .Add(new AiResponse("b", 50, 5), Now.AddMinutes(1));

        Assert.Equal(2, usage.Requests);
        Assert.Equal(2, usage.ReportedRequests);
        Assert.Equal(150, usage.InputTokens);
        Assert.Equal(25, usage.OutputTokens);
        Assert.Equal(Now.AddMinutes(1), usage.LastUsedUtc);
    }

    [Fact]
    public void Add_counts_a_request_without_token_counts_as_unreported_and_adds_no_tokens()
    {
        var usage = AiUsage.None
            .Add(new AiResponse("a", 10, 2), Now)
            .Add(new AiResponse("b"), Now);

        Assert.Equal(2, usage.Requests);
        Assert.Equal(1, usage.ReportedRequests);
        Assert.Equal(10, usage.InputTokens);
        Assert.Equal(2, usage.OutputTokens);
    }

    [Fact]
    public void Add_does_not_change_the_original_totals()
    {
        var before = AiUsage.None;
        _ = before.Add(new AiResponse("a", 1, 1), Now);

        Assert.Equal(0, before.Requests);
    }

    [Fact]
    public async Task Token_counts_from_an_openai_format_answer_reach_the_response()
    {
        const string body = "{\"id\":\"x\",\"object\":\"chat.completion\",\"created\":0,\"model\":\"m\"," +
                            "\"choices\":[{\"index\":0,\"message\":{\"role\":\"assistant\",\"content\":\"OK\"}," +
                            "\"finish_reason\":\"stop\"}],\"usage\":{\"prompt_tokens\":12,\"completion_tokens\":3," +
                            "\"total_tokens\":15}}";
        using var http = new HttpClient(new CannedHandler(body));
        var settings = new AiSettings(AiProviderKind.Gateway, "http://localhost:4000", "m");

        var reply = await AiAssistant.TestAsync(settings, http: http);

        Assert.Equal(12, reply.InputTokens);
        Assert.Equal(3, reply.OutputTokens);
    }

    [Fact]
    public async Task An_answer_without_usage_has_no_token_counts()
    {
        const string body = "{\"id\":\"x\",\"object\":\"chat.completion\",\"created\":0,\"model\":\"m\"," +
                            "\"choices\":[{\"index\":0,\"message\":{\"role\":\"assistant\",\"content\":\"OK\"}," +
                            "\"finish_reason\":\"stop\"}]}";
        using var http = new HttpClient(new CannedHandler(body));
        var settings = new AiSettings(AiProviderKind.Gateway, "http://localhost:4000", "m");

        var reply = await AiAssistant.TestAsync(settings, http: http);

        Assert.Null(reply.InputTokens);
        Assert.Null(reply.OutputTokens);
    }

    /// <summary>Answers every request with one canned body.</summary>
    private sealed class CannedHandler(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
    }
}
