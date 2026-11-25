using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using LogAnalyzer.Services.Ai;
using Xunit;

namespace LogAnalyzer.Tests;

public class AiFailureTests
{
    private static readonly AiSettings OpenAi =
        new(AiProviderKind.OpenAi, "https://api.openai.com/v1", "gpt-test", "sk-SECRET123");

    [Fact]
    public void An_unauthorised_status_says_the_key_is_wrong_and_shows_where_it_was_sent()
    {
        var error = AiFailure.FromStatus(401, OpenAi);

        Assert.Contains("did not accept the API key (HTTP 401)", error.Message);
        Assert.Contains("Address: https://api.openai.com. Model: gpt-test.", error.Message);
    }

    [Fact]
    public void The_providers_own_words_are_shown_but_the_key_never_is()
    {
        var error = AiFailure.FromStatus(401, OpenAi, providerDetail: "Incorrect API key provided: sk-SECRET123");

        Assert.Contains("Provider said: Incorrect API key provided: [key hidden]", error.Message);
        Assert.DoesNotContain("SECRET123", error.Message);
    }

    [Fact]
    public void A_refused_connection_says_nothing_is_listening()
    {
        var cause = new HttpRequestException("No connection could be made", new SocketException((int)SocketError.ConnectionRefused));

        var error = AiFailure.FromNetwork(cause, new AiSettings(AiProviderKind.Gateway, "http://localhost:4000", "m"));

        Assert.Contains("Nothing is listening at the address", error.Message);
        Assert.Contains("Check the port number", error.Message);
        Assert.Contains("Technical detail: HttpRequestException", error.Message);
    }

    [Fact]
    public void An_unknown_host_says_the_name_does_not_resolve()
    {
        var cause = new HttpRequestException("name", new SocketException((int)SocketError.HostNotFound));

        var error = AiFailure.FromNetwork(cause, OpenAi);

        Assert.Contains("host name in the address could not be found", error.Message);
    }

    [Fact]
    public void A_timeout_says_the_provider_was_too_slow()
    {
        var error = AiFailure.FromNetwork(new TaskCanceledException("timed out"), OpenAi);

        Assert.Contains("did not answer in time", error.Message);
    }

    [Fact]
    public async Task A_401_from_the_provider_reaches_the_user_with_the_fix()
    {
        using var http = new HttpClient(new StatusHandler(HttpStatusCode.Unauthorized,
            "{\"error\":{\"message\":\"Incorrect API key provided\"}}"));
        var settings = new AiSettings(AiProviderKind.OpenAi, "https://api.openai.com/v1", "gpt-test", "sk-SECRET123");

        var error = await Assert.ThrowsAsync<AiException>(() => AiAssistant.TestAsync(settings, http: http));

        Assert.Contains("did not accept the API key", error.Message);
        Assert.Contains("Incorrect API key provided", error.Message);
        Assert.DoesNotContain("sk-SECRET123", error.Message);
    }

    /// <summary>Answers every request with one status and body.</summary>
    private sealed class StatusHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
    }
}
