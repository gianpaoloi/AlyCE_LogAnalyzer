using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;

namespace LogAnalyzer.Services.Ai;

/// <summary>
/// A chat client for Gemini's own <c>generateContent</c> API, the format a Gemini endpoint speaks natively, for
/// example Databricks AI Gateway or Google's endpoint. It is not the OpenAI format, so the OpenAI client cannot
/// reach it. The key is sent as a Bearer token, which Databricks requires.
/// </summary>
public sealed class GeminiChatClient : IChatClient
{
    public const int DefaultMaxOutputTokens = 1024;

    private readonly HttpClient _http;
    private readonly string _root;
    private readonly string _model;
    private readonly string _apiKey;

    /// <param name="baseUrl">The address up to, not including, <c>/v1beta</c>, e.g. <c>https://generativelanguage.googleapis.com</c>.</param>
    public GeminiChatClient(HttpClient http, string baseUrl, string model, string apiKey)
    {
        _http = http;
        _root = baseUrl.Trim().TrimEnd('/');
        _model = model.Trim();
        _apiKey = apiKey.Trim();
    }

    public ChatClientMetadata Metadata => new("gemini-generate-content", new Uri(_root), _model);

    public async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
                                                     CancellationToken cancellationToken = default)
    {
        var url = $"{_root}/v1beta/models/{Uri.EscapeDataString(_model)}:generateContent";
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(BuildRequest(messages, options).ToJsonString(), Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

        using var response = await _http.SendAsync(request, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode) throw AiFailure.FromStatus((int)response.StatusCode);

        return ParseResponse(json);
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // The app asks for whole answers only, so the one answer is returned as a single update.
        var response = await GetResponseAsync(messages, options, cancellationToken);
        yield return new ChatResponseUpdate(ChatRole.Assistant, response.Text);
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;

    public void Dispose()
    {
    }

    /// <summary>System messages become <c>systemInstruction</c>; the user and assistant turns become <c>contents</c>.</summary>
    private JsonObject BuildRequest(IEnumerable<ChatMessage> messages, ChatOptions? options)
    {
        var contents = new JsonArray();
        var system = new StringBuilder();

        foreach (var message in messages)
        {
            var text = message.Text;
            if (string.IsNullOrEmpty(text)) continue;

            if (message.Role == ChatRole.System)
            {
                if (system.Length > 0) system.AppendLine();
                system.Append(text);
                continue;
            }

            contents.Add(new JsonObject
            {
                ["role"] = message.Role == ChatRole.Assistant ? "model" : "user",
                ["parts"] = new JsonArray(new JsonObject { ["text"] = text }),
            });
        }

        var body = new JsonObject
        {
            ["contents"] = contents,
            ["generationConfig"] = new JsonObject
            {
                ["maxOutputTokens"] = options?.MaxOutputTokens ?? DefaultMaxOutputTokens,
            },
        };
        if (system.Length > 0)
        {
            body["systemInstruction"] = new JsonObject
            {
                ["parts"] = new JsonArray(new JsonObject { ["text"] = system.ToString() }),
            };
        }
        return body;
    }

    private ChatResponse ParseResponse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var candidate = default(JsonElement);
        if (root.TryGetProperty("candidates", out var candidates) &&
            candidates.ValueKind == JsonValueKind.Array && candidates.GetArrayLength() > 0)
            candidate = candidates[0];

        var text = new StringBuilder();
        if (candidate.ValueKind == JsonValueKind.Object &&
            candidate.TryGetProperty("content", out var content) &&
            content.TryGetProperty("parts", out var parts) &&
            parts.ValueKind == JsonValueKind.Array)
        {
            foreach (var part in parts.EnumerateArray())
                if (part.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String)
                    text.Append(t.GetString());
        }

        if (text.Length == 0)
            throw new AiException("The provider returned no text. The request may have been blocked by its safety settings.");

        long? input = null;
        long? output = null;
        if (root.TryGetProperty("usageMetadata", out var usage))
        {
            if (usage.TryGetProperty("promptTokenCount", out var p) && p.TryGetInt64(out var pi)) input = pi;
            if (usage.TryGetProperty("candidatesTokenCount", out var o) && o.TryGetInt64(out var oi)) output = oi;
        }

        return new ChatResponse(new ChatMessage(ChatRole.Assistant, text.ToString()))
        {
            ModelId = _model,
            Usage = new UsageDetails { InputTokenCount = input, OutputTokenCount = output },
        };
    }
}
