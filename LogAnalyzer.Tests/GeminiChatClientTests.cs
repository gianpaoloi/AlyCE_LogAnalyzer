using System.Net;
using System.Net.Http.Headers;
using System.Text;
using LogAnalyzer.Services.Ai;
using Xunit;

namespace LogAnalyzer.Tests;

public class GeminiChatClientTests
{
    private const string Answer =
        "{\"candidates\":[{\"content\":{\"role\":\"model\",\"parts\":[{\"text\":\"OK\"}]},\"finishReason\":\"STOP\"}]," +
        "\"usageMetadata\":{\"promptTokenCount\":12,\"candidatesTokenCount\":3,\"totalTokenCount\":15}}";

    private const string Gateway = "https://adb-1.azuredatabricks.net/ai-gateway/gemini";
    private const string Model = "aiplatform_test.ai_llm_endpoints.gemini_2_5_flash";

    [Fact]
    public async Task Request_goes_to_generate_content_with_a_bearer_token_and_gemini_roles()
    {
        var handler = new Recording(HttpStatusCode.OK, Answer);
        using var http = new HttpClient(handler);
        var settings = new AiSettings(AiProviderKind.GeminiNative, Gateway, Model, "dbx-token");

        await AiAssistant.TestAsync(settings, http: http);

        var request = handler.Request!;
        Assert.Equal($"{Gateway}/v1beta/models/{Model}:generateContent", request.RequestUri!.ToString());
        Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
        Assert.Equal("dbx-token", request.Headers.Authorization.Parameter);

        var body = handler.Body!;
        Assert.Contains("\"role\":\"user\"", body);
        Assert.Contains("Connection test.", body);
        Assert.Contains("\"systemInstruction\"", body);
        Assert.Contains("\"maxOutputTokens\":", body);
    }

    [Fact]
    public async Task Assistant_turns_are_sent_as_model_and_system_text_as_system_instruction()
    {
        var handler = new Recording(HttpStatusCode.OK, Answer);
        var client = new GeminiChatClient(new HttpClient(handler), Gateway, Model, "k");

        var messages = new[]
        {
            new Microsoft.Extensions.AI.ChatMessage(Microsoft.Extensions.AI.ChatRole.System, "Be brief."),
            new Microsoft.Extensions.AI.ChatMessage(Microsoft.Extensions.AI.ChatRole.User, "Hi"),
            new Microsoft.Extensions.AI.ChatMessage(Microsoft.Extensions.AI.ChatRole.Assistant, "Hello"),
        };
        await client.GetResponseAsync(messages);

        var body = handler.Body!;
        Assert.Contains("\"role\":\"model\"", body);
        Assert.Contains("\"systemInstruction\":{\"parts\":[{\"text\":\"Be brief.\"}]}", body);
    }

    [Fact]
    public async Task Text_and_token_counts_are_read_from_the_answer()
    {
        using var http = new HttpClient(new Recording(HttpStatusCode.OK, Answer));
        var settings = new AiSettings(AiProviderKind.GeminiNative, Gateway, Model, "k");

        var reply = await AiAssistant.TestAsync(settings, http: http);

        Assert.Equal("OK", reply.Text);
        Assert.Equal(12, reply.InputTokens);
        Assert.Equal(3, reply.OutputTokens);
    }

    [Fact]
    public async Task A_rejected_key_gives_the_same_message_as_the_other_providers()
    {
        using var http = new HttpClient(new Recording(HttpStatusCode.Unauthorized, "{}"));
        var settings = new AiSettings(AiProviderKind.GeminiNative, Gateway, Model, "wrong");

        var e = await Assert.ThrowsAsync<AiException>(() => AiAssistant.TestAsync(settings, http: http));

        Assert.Contains("did not accept the request (HTTP 401)", e.Message);
    }

    [Fact]
    public async Task An_answer_with_no_text_is_reported_as_blocked_or_empty()
    {
        using var http = new HttpClient(new Recording(HttpStatusCode.OK, "{\"promptFeedback\":{\"blockReason\":\"SAFETY\"}}"));
        var settings = new AiSettings(AiProviderKind.GeminiNative, Gateway, Model, "k");

        var e = await Assert.ThrowsAsync<AiException>(() => AiAssistant.TestAsync(settings, http: http));

        Assert.Contains("no text", e.Message);
    }

    [Fact]
    public void Google_endpoint_is_the_default_address_and_a_key_is_required()
    {
        Assert.Equal("https://generativelanguage.googleapis.com", AiSettings.DefaultBaseUrl(AiProviderKind.GeminiNative));
        Assert.True(new AiSettings(AiProviderKind.GeminiNative, Gateway, Model).KeyRequired);
    }

    /// <summary>Answers one canned response and keeps the request and its body.</summary>
    private sealed class Recording(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Request = request;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }
}

public class ResponsesApiTests
{
    private const string Answer =
        "{\"id\":\"resp_1\",\"object\":\"response\",\"created_at\":0,\"model\":\"m\",\"status\":\"completed\"," +
        "\"output\":[{\"type\":\"message\",\"id\":\"msg_1\",\"role\":\"assistant\",\"status\":\"completed\"," +
        "\"content\":[{\"type\":\"output_text\",\"text\":\"OK\",\"annotations\":[]}]}]," +
        "\"usage\":{\"input_tokens\":12,\"output_tokens\":3,\"total_tokens\":15}}";

    [Fact]
    public async Task Request_goes_to_v1_responses_with_a_bearer_token_and_returns_text_and_tokens()
    {
        var handler = new RecordingResponses(Answer);
        using var http = new HttpClient(handler);
        var settings = new AiSettings(AiProviderKind.ResponsesApi, "https://adb-1.azuredatabricks.net/ai-gateway/mlflow",
                                      "aiplatform_test.ai_llm_endpoints.databricks_claude_opus_5", "dbx-token");

        var reply = await AiAssistant.TestAsync(settings, http: http);

        Assert.Equal("https://adb-1.azuredatabricks.net/ai-gateway/mlflow/v1/responses", handler.Url);
        Assert.Equal("Bearer dbx-token", handler.Authorization);
        Assert.Contains("\"model\":\"aiplatform_test.ai_llm_endpoints.databricks_claude_opus_5\"", handler.Body);
        Assert.Equal("OK", reply.Text);
        Assert.Equal(12, reply.InputTokens);
        Assert.Equal(3, reply.OutputTokens);
    }

    private sealed class RecordingResponses(string body) : HttpMessageHandler
    {
        public string? Url { get; private set; }
        public string? Authorization { get; private set; }
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Url = request.RequestUri!.ToString();
            Authorization = request.Headers.Authorization?.ToString();
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
        }
    }
}

public class GeminiGoogleKeyTests
{
    [Fact]
    public async Task Google_AI_Studio_key_is_sent_in_the_x_goog_api_key_header_not_as_a_bearer_token()
    {
        var handler = new CapturingGoogle();
        using var http = new HttpClient(handler);
        var settings = new AiSettings(AiProviderKind.GeminiNative, "https://generativelanguage.googleapis.com", "gemini-2.5-flash", "aistudio-key");

        await AiAssistant.TestAsync(settings, http: http);

        Assert.Equal("aistudio-key", handler.GoogleKeyHeader);
        Assert.Null(handler.Authorization);
        Assert.Contains("/v1beta/models/gemini-2.5-flash:generateContent", handler.Url);
    }

    private sealed class CapturingGoogle : HttpMessageHandler
    {
        public string? Url { get; private set; }
        public string? GoogleKeyHeader { get; private set; }
        public string? Authorization { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Url = request.RequestUri!.ToString();
            GoogleKeyHeader = request.Headers.TryGetValues("x-goog-api-key", out var v) ? v.Single() : null;
            Authorization = request.Headers.Authorization?.ToString();
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("{\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"OK\"}]}}]}", Encoding.UTF8, "application/json"),
            });
        }
    }
}

public class GeminiAddressFormTests
{
    [Theory]
    [InlineData("https://generativelanguage.googleapis.com", "gemini-2.5-flash")]
    [InlineData("https://generativelanguage.googleapis.com/v1beta", "gemini-2.5-flash")]
    [InlineData("https://generativelanguage.googleapis.com/v1beta/", "models/gemini-2.5-flash")]
    public async Task Address_and_model_are_accepted_with_or_without_the_repeated_parts(string address, string model)
    {
        var handler = new UrlOnly();
        using var http = new HttpClient(handler);
        var settings = new AiSettings(AiProviderKind.GeminiNative, address, model, "k");

        await AiAssistant.TestAsync(settings, http: http);

        Assert.Equal("https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent", handler.Url);
    }

    private sealed class UrlOnly : HttpMessageHandler
    {
        public string? Url { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Url = request.RequestUri!.ToString();
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("{\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"OK\"}]}}]}", Encoding.UTF8, "application/json"),
            });
        }
    }
}
