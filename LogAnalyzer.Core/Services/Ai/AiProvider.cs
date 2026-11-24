namespace LogAnalyzer.Services.Ai;

/// <summary>
/// Which provider the user picked. The kind decides which model client is built and the default address.
/// The two OpenAI-style kinds share one client, because ChatGPT and local or other servers speak the OpenAI format.
/// </summary>
public enum AiProviderKind
{
    /// <summary>Claude, through Anthropic's Messages API.</summary>
    Anthropic,

    /// <summary>ChatGPT, through OpenAI's chat-completions API.</summary>
    OpenAi,

    /// <summary>Any OpenAI-format server: a local model (Ollama, LM Studio, llama.cpp) or a gateway such as LiteLLM.</summary>
    Gateway,

    /// <summary>Gemini's own generateContent API: Google's endpoint, or a gateway that serves Gemini natively such as Databricks AI Gateway.</summary>
    GeminiNative,

    /// <summary>The OpenAI Responses API, served by OpenAI and by gateways such as Databricks AI Gateway.</summary>
    ResponsesApi,
}

/// <summary>What the user configured. <see cref="ApiKey"/> lives in memory only; storage encrypts it.</summary>
public sealed record AiSettings(AiProviderKind Kind, string BaseUrl, string Model, string? ApiKey = null, bool Verified = false, string? ProviderId = null, string? ProviderName = null)
{
    // Verified: a connection test passed for exactly these values. The page clears it when any value changes.

    public static string DefaultBaseUrl(AiProviderKind kind) => kind switch
    {
        AiProviderKind.Anthropic => "https://api.anthropic.com",
        AiProviderKind.OpenAi => "https://api.openai.com/v1",
        AiProviderKind.GeminiNative => "https://generativelanguage.googleapis.com",
        AiProviderKind.ResponsesApi => "https://api.openai.com/v1",
        _ => "",
    };

    public bool HasKey => !string.IsNullOrWhiteSpace(ApiKey);

    /// <summary>A local server or an open gateway may need no key; the hosted providers always do.</summary>
    public bool KeyRequired => Kind != AiProviderKind.Gateway;

    /// <summary>The first problem with these settings (in English), or null when they can be used.</summary>
    public string? Validate()
    {
        if (!Uri.TryCreate(BaseUrl?.Trim(), UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            return "Enter the address of the provider, starting with https:// (or http:// for a local server).";
        if (string.IsNullOrWhiteSpace(Model)) return "Enter the model name.";
        if (KeyRequired && !HasKey) return "Enter the API key.";
        // The key is a credential: it must not travel in clear text to a remote host.
        if (HasKey && uri.Scheme == "http" && !uri.IsLoopback)
            return "The API key is only sent over https (http is allowed for localhost).";
        return null;
    }
}

/// <summary>One saved provider: the user's name for it, and its settings. <see cref="Id"/> identifies it across edits.</summary>
public sealed record AiProviderConfig(string Id, string Name, AiSettings Settings);

/// <summary>The answer text of one request.</summary>
public sealed record AiResponse(string Text, long? InputTokens = null, long? OutputTokens = null);

/// <summary>A failure whose message is fit to show the user. Every problem a provider can cause ends up as this.</summary>
public sealed class AiException(string message) : Exception(message);

/// <summary>Turns the address the user typed into the form each SDK expects.</summary>
public static class AiEndpoints
{
    /// <summary>
    /// The address without a trailing "/" or "/v1". The Anthropic SDK adds "/v1/messages" itself, so a
    /// typed "/v1" would be doubled.
    /// </summary>
    public static string Root(string baseUrl)
    {
        var root = baseUrl.Trim().TrimEnd('/');
        return root.EndsWith("/v1", StringComparison.OrdinalIgnoreCase) ? root[..^3] : root;
    }

    /// <summary>
    /// The address ending in "/v1", which the OpenAI SDK extends with "/chat/completions". "https://host" and
    /// "https://host/v1" both give "https://host/v1".
    /// </summary>
    public static string V1(string baseUrl) => Root(baseUrl) + "/v1";
}

/// <summary>The message for a failed request, by HTTP status. Shared by every provider.</summary>
public static class AiFailure
{
    public static AiException FromStatus(int status) => status switch
    {
        401 or 403 => new AiException(
            $"The provider did not accept the request (HTTP {status}). Check the API key and that its account can use this model."),
        404 => new AiException(
            "The provider could not find this address or model (HTTP 404). Check the address and the model name."),
        429 => new AiException(
            "The provider is limiting requests (HTTP 429). Wait a moment and try again."),
        _ => new AiException($"The provider returned HTTP {status}."),
    };
}
