using System.Security.Authentication;
using System.Net.Sockets;

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

/// <summary>
/// The messages for a failed connection test or request, shared by every provider. Each one says what
/// happened, what to check, and where the address and model were, so the user can fix it without the log.
/// </summary>
public static class AiFailure
{
    private const int MaxDetailChars = 400;

    /// <summary>The provider answered with an HTTP error. <paramref name="providerDetail"/> is its own message, when it sent one.</summary>
    public static AiException FromStatus(int status, AiSettings? settings = null, string? providerDetail = null)
    {
        var (what, fix) = status switch
        {
            400 => ("The provider rejected the request (HTTP 400).",
                    "Check that the model name is exactly as the provider lists it. The provider's message below says what it did not accept."),
            401 => ("The provider did not accept the API key (HTTP 401).",
                    "Check the API key: copy it again from the provider's console, with no spaces, and make sure it belongs to this provider."),
            403 => ("The provider refused the request (HTTP 403).",
                    "The key may be valid but this account cannot use this model or feature. Check the model access and the key's permissions in the provider's console."),
            404 => ("The provider has no such address or model (HTTP 404).",
                    "Check the address (for example https://api.openai.com/v1 for OpenAI, or https://api.anthropic.com for Claude) and that the model name is spelled as the provider lists it."),
            408 => ("The provider did not answer in time (HTTP 408).",
                    "Try again in a moment."),
            429 => ("The provider is limiting requests, or the account has run out of quota (HTTP 429).",
                    "Wait a minute and try again. If it keeps failing, check the account's usage and billing limits."),
            >= 500 => ($"The provider had an internal error (HTTP {status}).",
                       "The problem is on the provider's side. Try again later, and check the provider's status page."),
            _ => ($"The provider returned HTTP {status}.",
                  "The provider's message below may explain it."),
        };

        return new AiException(Compose(what, fix, settings, providerDetail, technical: null));
    }

    /// <summary>
    /// A request that got no answer: a name that does not resolve, a refused connection, a timeout, or a secure
    /// connection that could not be set up. The technical chain is kept so a support request has something to go on.
    /// </summary>
    public static AiException FromNetwork(Exception e, AiSettings settings)
    {
        var chain = Chain(e).ToList();
        string what, fix;

        var socket = chain.OfType<SocketException>().FirstOrDefault();
        if (socket is not null)
        {
            (what, fix) = socket.SocketErrorCode switch
            {
                SocketError.HostNotFound or SocketError.TryAgain or SocketError.NoData =>
                    ("The host name in the address could not be found.",
                     "Check the spelling of the address. If it is correct, this machine cannot resolve it: check the DNS and the proxy settings."),
                SocketError.ConnectionRefused =>
                    ("Nothing is listening at the address (the connection was refused).",
                     "Check the port number, and that the server or local model is running."),
                SocketError.TimedOut =>
                    ("The connection timed out.",
                     "Check the network, any VPN or firewall, and that the address can be reached from this machine."),
                SocketError.NetworkUnreachable or SocketError.HostUnreachable =>
                    ("The provider cannot be reached from this machine.",
                     "Check the internet connection, the VPN and the proxy settings."),
                _ => ($"The network reported an error ({socket.SocketErrorCode}).",
                      "Check the address and the network connection."),
            };
        }
        else if (chain.OfType<AuthenticationException>().Any())
        {
            (what, fix) = ("The secure connection (TLS) to the provider could not be set up.",
                           "A company proxy or firewall that inspects traffic needs its certificate trusted by Windows. Check that the address is the provider's real address.");
        }
        else if (chain.Any(c => c is TaskCanceledException or TimeoutException))
        {
            (what, fix) = ("The provider did not answer in time.",
                           "Try again. If it keeps happening, check the network and the provider's status page.");
        }
        else
        {
            (what, fix) = ("Could not reach the provider.",
                           "Check the address and the network connection.");
        }

        var technical = string.Join(" <- ", chain.Select(c => $"{c.GetType().Name}: {c.Message}"));
        return new AiException(Compose(what, fix, settings, providerDetail: null, technical));
    }

    /// <summary>
    /// The message in full: the cause, the fix, the address and model that were used, the provider's own words and
    /// the technical chain. The API key is removed from anything quoted, and long text is cut short.
    /// </summary>
    private static string Compose(string what, string fix, AiSettings? settings, string? providerDetail, string? technical)
    {
        var lines = new List<string> { $"{what} {fix}" };

        if (settings is not null)
        {
            var host = Uri.TryCreate(settings.BaseUrl?.Trim(), UriKind.Absolute, out var uri)
                ? uri.GetLeftPart(UriPartial.Authority)
                : settings.BaseUrl ?? "(no address)";
            lines.Add($"Address: {host}. Model: {settings.Model}.");
        }

        if (!string.IsNullOrWhiteSpace(providerDetail))
            lines.Add("Provider said: " + Clean(providerDetail, settings));

        if (!string.IsNullOrWhiteSpace(technical))
            lines.Add("Technical detail: " + Clean(technical, settings));

        return string.Join("\n", lines);
    }

    private static string Clean(string text, AiSettings? settings)
    {
        var value = text.Trim();
        if (settings?.ApiKey is { Length: >= 4 } key)
            value = value.Replace(key, "[key hidden]", StringComparison.Ordinal);
        return value.Length > MaxDetailChars ? value[..MaxDetailChars] + "…" : value;
    }

    private static IEnumerable<Exception> Chain(Exception e)
    {
        for (Exception? cause = e; cause is not null; cause = cause.InnerException)
            yield return cause;
    }
}
