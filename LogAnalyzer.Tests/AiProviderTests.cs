using System.Net;
using System.Text;
using LogAnalyzer.Models;
using LogAnalyzer.Services;
using LogAnalyzer.Services.Ai;
using Xunit;

namespace LogAnalyzer.Tests;

public class AiProviderTests
{
    /// <summary>Answers every request with one canned status and body, and remembers what it was asked.</summary>
    private sealed class FakeHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Request = request;
            RequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }

    /// <summary>Fails before any answer, as a dropped connection does.</summary>
    private sealed class UnreachableHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            throw new HttpRequestException("no route to host");
    }

    private const string AnthropicReply =
        """{"id":"msg_1","type":"message","role":"assistant","model":"claude-haiku-5-5","content":[{"type":"text","text":"It is a timeout."}],"stop_reason":"end_turn","usage":{"input_tokens":120,"output_tokens":18}}""";

    private const string OpenAiReply =
        """{"id":"chatcmpl-1","object":"chat.completion","created":1,"model":"gpt-4o","choices":[{"index":0,"message":{"role":"assistant","content":"It is a timeout."},"finish_reason":"stop"}],"usage":{"prompt_tokens":120,"completion_tokens":18,"total_tokens":138}}""";

    private static AiPreview Preview(string user = "Level: ERROR") => new("system text", user, 0);

    // ---------------------------------------------------------------- redaction

    [Fact]
    public void Redact_masks_e_mail_addresses_and_ip_addresses()
    {
        var (text, count) = AiRedactor.Redact("user jane.doe@example.com from 10.20.30.40 failed");

        Assert.Equal("user [email] from [ip] failed", text);
        Assert.Equal(2, count);
    }

    [Fact]
    public void Redact_masks_bearer_tokens_and_key_value_secrets_but_keeps_their_names()
    {
        var (text, count) = AiRedactor.Redact("Authorization: Bearer abc.def-123 password=hunter2 api_key=XYZ");

        Assert.DoesNotContain("abc.def-123", text);
        Assert.DoesNotContain("hunter2", text);
        Assert.DoesNotContain("XYZ", text);
        Assert.Contains("password=[redacted]", text);
        // The bearer rule masks the token; the "authorization:" rule then masks the word "Bearer" once more.
        // Redundant but harmless, and it keeps the token itself out of the text.
        Assert.Equal(4, count);
    }

    [Fact]
    public void Redact_masks_json_style_secrets()
    {
        var (text, _) = AiRedactor.Redact("""{"token": "secret-value", "user": "x"}""");

        Assert.DoesNotContain("secret-value", text);
        Assert.Contains("\"user\": \"x\"", text);
    }

    [Fact]
    public void Redact_leaves_ordinary_text_alone()
    {
        const string text = "Timeout after 30 seconds on orders-service";

        var (result, count) = AiRedactor.Redact(text);

        Assert.Equal(text, result);
        Assert.Equal(0, count);
    }

    // ---------------------------------------------------------------- addresses

    [Theory]
    [InlineData("https://api.anthropic.com", "https://api.anthropic.com")]
    [InlineData("https://api.anthropic.com/", "https://api.anthropic.com")]
    [InlineData("https://api.anthropic.com/v1", "https://api.anthropic.com")]
    public void Anthropic_root_drops_a_typed_v1_so_the_sdk_does_not_double_it(string typed, string expected) =>
        Assert.Equal(expected, AiEndpoints.Root(typed));

    [Theory]
    [InlineData("https://api.openai.com/v1", "https://api.openai.com/v1")]
    [InlineData("https://gateway.example.com", "https://gateway.example.com/v1")]
    [InlineData("https://gateway.example.com/v1/", "https://gateway.example.com/v1")]
    [InlineData("http://localhost:11434", "http://localhost:11434/v1")]
    public void OpenAi_format_address_always_ends_in_v1(string typed, string expected) =>
        Assert.Equal(expected, AiEndpoints.V1(typed));

    // ---------------------------------------------------------------- providers, through the agent

    [Fact]
    public async Task Anthropic_sends_the_key_the_version_and_the_prompt_then_reads_the_text()
    {
        var handler = new FakeHandler(HttpStatusCode.OK, AnthropicReply);
        var settings = new AiSettings(AiProviderKind.Anthropic, "https://api.anthropic.com", "claude-haiku-5-5", "sk-ant-test");

        var reply = await AiAssistant.SendAsync(settings, Preview(), http: new HttpClient(handler));

        Assert.Equal(HttpMethod.Post, handler.Request!.Method);
        Assert.Equal("https://api.anthropic.com/v1/messages", handler.Request.RequestUri!.ToString());
        // The Anthropic SDK sends ApiKey as "Authorization: Bearer" (its documented behaviour), not x-api-key.
        Assert.Equal("Bearer", handler.Request.Headers.Authorization!.Scheme);
        Assert.Equal("sk-ant-test", handler.Request.Headers.Authorization.Parameter);
        Assert.True(handler.Request.Headers.Contains("anthropic-version"));
        Assert.Contains("claude-haiku-5-5", handler.RequestBody);
        Assert.Contains("system text", handler.RequestBody);
        Assert.Equal("It is a timeout.", reply.Text);
    }

    [Fact]
    public async Task OpenAi_format_sends_a_bearer_key_and_reads_the_answer()
    {
        var handler = new FakeHandler(HttpStatusCode.OK, OpenAiReply);
        var settings = new AiSettings(AiProviderKind.OpenAi, AiSettings.DefaultBaseUrl(AiProviderKind.OpenAi), "gpt-4o", "sk-openai-test");

        var reply = await AiAssistant.SendAsync(settings, Preview(), http: new HttpClient(handler));

        Assert.Equal("https://api.openai.com/v1/chat/completions", handler.Request!.RequestUri!.ToString());
        Assert.Equal("Bearer", handler.Request.Headers.Authorization!.Scheme);
        Assert.Equal("sk-openai-test", handler.Request.Headers.Authorization.Parameter);
        Assert.Contains("\"role\":\"system\"", handler.RequestBody);
        Assert.Equal("It is a timeout.", reply.Text);
    }

    [Fact]
    public async Task A_local_server_without_a_key_is_called_at_its_v1_address()
    {
        var handler = new FakeHandler(HttpStatusCode.OK, OpenAiReply);
        var settings = new AiSettings(AiProviderKind.Gateway, "http://localhost:11434", "llama3");

        var reply = await AiAssistant.SendAsync(settings, Preview(), http: new HttpClient(handler));

        Assert.Equal("http://localhost:11434/v1/chat/completions", handler.Request!.RequestUri!.ToString());
        Assert.Equal("It is a timeout.", reply.Text);
    }

    [Fact]
    public async Task A_refused_key_is_reported_in_plain_words()
    {
        var handler = new FakeHandler(HttpStatusCode.Unauthorized, """{"error":{"message":"invalid key"}}""");
        var settings = new AiSettings(AiProviderKind.OpenAi, "https://api.openai.com/v1", "gpt-4o", "wrong");

        var error = await Assert.ThrowsAsync<AiException>(() =>
            AiAssistant.SendAsync(settings, Preview(), http: new HttpClient(handler)));

        Assert.Contains("did not accept", error.Message);
        Assert.Contains("401", error.Message);
    }

    [Fact]
    public async Task A_reply_without_text_is_an_error_not_an_empty_explanation()
    {
        var handler = new FakeHandler(HttpStatusCode.OK, """{"id":"c","object":"chat.completion","created":1,"model":"m","choices":[{"index":0,"message":{"role":"assistant","content":null},"finish_reason":"stop"}]}""");
        var settings = new AiSettings(AiProviderKind.Gateway, "http://localhost:11434", "llama3");

        await Assert.ThrowsAsync<AiException>(() =>
            AiAssistant.SendAsync(settings, Preview(), http: new HttpClient(handler)));
    }

    [Fact]
    public async Task An_unreachable_address_is_reported_as_a_connection_problem()
    {
        var settings = new AiSettings(AiProviderKind.Anthropic, "https://api.anthropic.com", "claude-haiku-5-5", "k");

        var error = await Assert.ThrowsAsync<AiException>(() =>
            AiAssistant.SendAsync(settings, Preview(), http: new HttpClient(new UnreachableHandler())));

        Assert.Contains("Could not reach", error.Message);
    }

    [Fact]
    public async Task The_model_abstraction_can_be_swapped_without_touching_the_callers()
    {
        // A test factory standing in for a new provider: the explain flow only needs an IChatClient.
        var handler = new FakeHandler(HttpStatusCode.OK, OpenAiReply);
        var factory = new RecordingFactory();
        var settings = new AiSettings(AiProviderKind.Gateway, "http://localhost:11434", "llama3");

        var reply = await AiAssistant.SendAsync(settings, Preview(), factory, new HttpClient(handler));

        Assert.True(factory.Called);
        Assert.Equal("It is a timeout.", reply.Text);
    }

    /// <summary>Builds the usual client, but records that the factory was the one asked.</summary>
    private sealed class RecordingFactory : IAiChatClientFactory
    {
        public bool Called { get; private set; }

        public Microsoft.Extensions.AI.IChatClient Create(AiSettings settings, HttpClient http)
        {
            Called = true;
            return AiChatClientFactory.Default.Create(settings, http);
        }
    }

    // ---------------------------------------------------------------- settings

    [Fact]
    public void ChatGpt_starts_at_the_official_address_and_needs_a_key()
    {
        Assert.Equal("https://api.openai.com/v1", AiSettings.DefaultBaseUrl(AiProviderKind.OpenAi));

        var settings = new AiSettings(AiProviderKind.OpenAi, AiSettings.DefaultBaseUrl(AiProviderKind.OpenAi), "gpt-4o");

        Assert.Equal("Enter the API key.", settings.Validate());
    }

    [Fact]
    public void A_key_is_refused_over_plain_http_to_a_remote_host()
    {
        var settings = new AiSettings(AiProviderKind.Gateway, "http://gateway.example.com", "model", "key");

        Assert.Contains("only sent over https", settings.Validate());
    }

    [Fact]
    public void A_key_may_go_over_plain_http_to_localhost()
    {
        var settings = new AiSettings(AiProviderKind.Gateway, "http://localhost:1234/v1", "model", "key");

        Assert.Null(settings.Validate());
    }

    [Fact]
    public void A_local_gateway_needs_no_key()
    {
        Assert.Null(new AiSettings(AiProviderKind.Gateway, "http://localhost:11434", "llama3").Validate());
    }

    [Fact]
    public void Validate_asks_for_the_model_and_a_real_address()
    {
        Assert.Equal("Enter the model name.", new AiSettings(AiProviderKind.Gateway, "http://localhost:11434", " ").Validate());
        Assert.NotNull(new AiSettings(AiProviderKind.Gateway, "not a url", "model").Validate());
    }

    // ---------------------------------------------------------------- stored providers

    /// <summary>Reversible stand-in: round-trips, but is not encryption. The tests check what is stored, not the cipher.</summary>
    private sealed class ReversingProtector : ISecretProtector
    {
        public string Protect(string plainText) => Convert.ToBase64String(Encoding.UTF8.GetBytes(new string(plainText.Reverse().ToArray())));
        public string? Unprotect(string protectedText) => new string(Encoding.UTF8.GetString(Convert.FromBase64String(protectedText)).Reverse().ToArray());
    }

    /// <summary>A host with no key: it cannot protect anything and cannot open anything.</summary>
    private sealed class NoKeyProtector : ISecretProtector
    {
        public string Protect(string plainText) => throw new InvalidOperationException("no key");
        public string? Unprotect(string protectedText) => null;
    }

    private static AiProviderConfig Config(AiSettings settings, string id = "p1", string name = "Test") => new(id, name, settings);

    [Fact]
    public void Sealed_provider_does_not_contain_the_key_and_opens_back_to_the_same_provider()
    {
        var config = Config(new AiSettings(AiProviderKind.Anthropic, "https://api.anthropic.com", "claude-haiku-5-5", "sk-ant-secret-123"));

        var json = AiSettingsCodec.Seal(config, new ReversingProtector());

        Assert.NotNull(json);
        Assert.DoesNotContain("sk-ant-secret-123", json);
        // An opened provider carries its id, which is where its token usage is recorded.
        Assert.Equal(config with { Settings = config.Settings with { ProviderId = config.Id, ProviderName = config.Name } },
                     AiSettingsCodec.Open(json, new ReversingProtector()));
    }

    [Fact]
    public void The_verified_mark_is_saved_with_the_provider_and_read_back()
    {
        var config = Config(new AiSettings(AiProviderKind.Gateway, "http://localhost:11434", "llama3", Verified: true));

        var read = AiSettingsCodec.Open(AiSettingsCodec.Seal(config, new ReversingProtector())!, new ReversingProtector());

        Assert.NotNull(read);
        Assert.True(read.Settings.Verified);
    }

    [Fact]
    public void A_save_from_before_named_providers_opens_with_the_default_id_and_the_type_as_name()
    {
        const string old = """{"Kind":"Gateway","BaseUrl":"http://localhost:11434","Model":"llama3","Secret":""}""";

        var read = AiSettingsCodec.Open(old, new ReversingProtector());

        Assert.NotNull(read);
        Assert.Equal(AiSettingsStore.LegacyId, read.Id);
        Assert.Equal("Gateway", read.Name);
        Assert.False(read.Settings.Verified);
    }

    [Fact]
    public void A_save_from_the_removed_LiteLLM_provider_opens_as_a_gateway()
    {
        const string old = """{"Id":"p1","Name":"Old","Kind":"LiteLlm","BaseUrl":"https://ai-litellm-gw-test.agyo.io","Model":"gpt-4o","Secret":""}""";

        var read = AiSettingsCodec.Open(old, new ReversingProtector());

        Assert.NotNull(read);
        Assert.Equal(AiProviderKind.Gateway, read.Settings.Kind);
        Assert.Equal("https://ai-litellm-gw-test.agyo.io", read.Settings.BaseUrl);
    }

    [Fact]
    public void Seal_refuses_to_store_a_key_it_cannot_encrypt()
    {
        var config = Config(new AiSettings(AiProviderKind.OpenAi, "https://api.openai.com/v1", "gpt-4o", "sk-secret"));

        Assert.Null(AiSettingsCodec.Seal(config, new NoKeyProtector()));
    }

    [Fact]
    public void A_local_model_saves_without_any_protector()
    {
        var config = Config(new AiSettings(AiProviderKind.Gateway, "http://localhost:11434", "llama3"));

        Assert.NotNull(AiSettingsCodec.Seal(config, new NoKeyProtector()));
    }

    [Fact]
    public void A_key_that_cannot_be_opened_comes_back_empty_so_the_page_asks_again()
    {
        var json = AiSettingsCodec.Seal(
            Config(new AiSettings(AiProviderKind.OpenAi, "https://api.openai.com/v1", "gpt-4o", "sk-secret")), new ReversingProtector())!;

        var read = AiSettingsCodec.Open(json, new NoKeyProtector());

        Assert.NotNull(read);
        Assert.Equal("https://api.openai.com/v1", read.Settings.BaseUrl);
        Assert.False(read.Settings.HasKey);
    }

    // ---------------------------------------------------------------- preview

    [Fact]
    public void Cluster_preview_masks_values_and_counts_them()
    {
        var group = new MessageGroup
        {
            Level = "ERROR",
            Signature = "ERROR|x",
            SampleMessage = "Login failed for jane.doe@example.com, password=hunter2",
            Count = 42,
        };
        group.Environments.Add("prod");

        var preview = AiAssistant.BuildClusterPreview(group, "it");

        Assert.DoesNotContain("jane.doe@example.com", preview.User);
        Assert.DoesNotContain("hunter2", preview.User);
        Assert.Equal(2, preview.Redactions);
        Assert.Contains("Occurrences: 42", preview.User);
        Assert.Contains("code \"it\"", preview.User);
        Assert.Equal(AiAssistant.SystemPrompt, preview.System);
    }

    [Fact]
    public void Cluster_preview_caps_a_very_long_sample()
    {
        var group = new MessageGroup { Level = "ERROR", Signature = "ERROR|x", SampleMessage = new string('a', AiAssistant.MaxSampleChars + 500) };

        var preview = AiAssistant.BuildClusterPreview(group, "en");

        Assert.Contains("[truncated]", preview.User);
        Assert.True(preview.User.Length < AiAssistant.MaxSampleChars + 1_000);
    }
}
