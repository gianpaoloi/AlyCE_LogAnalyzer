using System.ClientModel;
using System.ClientModel.Primitives;
using Anthropic;
using Microsoft.Extensions.AI;
using OpenAI;
using OpenAI.Chat;

namespace LogAnalyzer.Services.Ai;

/// <summary>
/// The model abstraction. It turns the user's settings into a Microsoft.Extensions.AI <see cref="IChatClient"/>,
/// and everything above it (the Agent Framework agent, the explain flow) only sees that interface. Adding a
/// model means adding a case here; the callers do not change.
/// </summary>
public interface IAiChatClientFactory
{
    /// <param name="http">The HTTP client every request goes through. Tests pass a fake; the app passes <see cref="AiChatClientFactory.SharedHttp"/>.</param>
    IChatClient Create(AiSettings settings, HttpClient http);
}

/// <summary>
/// The vendor SDKs behind <see cref="IChatClient"/>: the Anthropic SDK for Claude, and the OpenAI SDK for
/// ChatGPT and other OpenAI-format servers (local models and gateways).
/// </summary>
public sealed class AiChatClientFactory : IAiChatClientFactory
{
    /// <summary>The app's one HTTP client. A client per request would exhaust sockets.</summary>
    public static readonly HttpClient SharedHttp = new() { Timeout = TimeSpan.FromMinutes(2) };

    /// <summary>Claude requires an output cap. Explanations are short, so this is ample.</summary>
    public const int AnthropicMaxTokens = 1024;

    public static readonly AiChatClientFactory Default = new();

    public IChatClient Create(AiSettings settings, HttpClient http) => settings.Kind switch
    {
        AiProviderKind.Anthropic => CreateAnthropic(settings, http),
        AiProviderKind.GeminiNative => new GeminiChatClient(http, settings.BaseUrl, settings.Model, settings.ApiKey ?? ""),
        _ => CreateOpenAiFormat(settings, http),
    };

    private static IChatClient CreateAnthropic(AiSettings settings, HttpClient http)
    {
        var client = new AnthropicClient
        {
            ApiKey = settings.ApiKey!.Trim(),
            BaseUrl = AiEndpoints.Root(settings.BaseUrl),
            HttpClient = http,
        };
        return client.AsIChatClient(settings.Model.Trim(), AnthropicMaxTokens);
    }

    private static IChatClient CreateOpenAiFormat(AiSettings settings, HttpClient http)
    {
        // The OpenAI SDK always needs a credential object. A local server without auth ignores the placeholder.
        var credential = new ApiKeyCredential(settings.HasKey ? settings.ApiKey!.Trim() : "no-key");
        var options = new OpenAIClientOptions
        {
            Endpoint = new Uri(AiEndpoints.V1(settings.BaseUrl)),
            Transport = new HttpClientPipelineTransport(http),
        };
        return new ChatClient(settings.Model.Trim(), credential, options).AsIChatClient();
    }
}
