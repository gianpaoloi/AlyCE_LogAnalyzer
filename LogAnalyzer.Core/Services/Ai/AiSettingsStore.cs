using System.Text.Json;
using Microsoft.JSInterop;

namespace LogAnalyzer.Services.Ai;

/// <summary>
/// One saved provider as it is written to browser storage: the key encrypted, everything else readable.
/// Saves from before named providers have no <see cref="Id"/> or <see cref="Name"/>; they read as the default.
/// </summary>
public sealed record StoredAiConfig(string Id, string Name, string Kind, string BaseUrl, string Model, string Secret, bool Verified = false);

/// <summary>Turns a saved provider into its stored form and back. The key is encrypted with the host's <see cref="ISecretProtector"/>.</summary>
public static class AiSettingsCodec
{
    /// <summary>
    /// The JSON to store, with the key encrypted. Returns null when the key cannot be encrypted, so that a
    /// plain-text key is never the fallback. A provider without a key (a local model) needs no protector.
    /// </summary>
    public static string? Seal(AiProviderConfig config, ISecretProtector protector)
    {
        try
        {
            var settings = config.Settings;
            var secret = settings.HasKey ? protector.Protect(settings.ApiKey!.Trim()) : "";
            return JsonSerializer.Serialize(new StoredAiConfig(
                config.Id, config.Name.Trim(), settings.Kind.ToString(), settings.BaseUrl.Trim(),
                settings.Model.Trim(), secret, settings.Verified));
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Reads a saved provider. A key that cannot be decrypted comes back as null, so the page asks for it again.
    /// Returns null for damaged text or an unknown provider type.
    /// </summary>
    public static AiProviderConfig? Open(string json, ISecretProtector protector)
    {
        try
        {
            var stored = JsonSerializer.Deserialize<StoredAiConfig>(json);
            if (stored is null) return null;

            // LiteLLM used to be a provider of its own. Its saved settings keep working as a generic gateway.
            var kindName = stored.Kind == "LiteLlm" ? nameof(AiProviderKind.Gateway) : stored.Kind;
            if (!Enum.TryParse<AiProviderKind>(kindName, out var kind)) return null;

            var key = string.IsNullOrEmpty(stored.Secret) ? null : protector.Unprotect(stored.Secret);
            var id = string.IsNullOrWhiteSpace(stored.Id) ? AiSettingsStore.LegacyId : stored.Id;
            var name = string.IsNullOrWhiteSpace(stored.Name) ? kind.ToString() : stored.Name;
            var settings = new AiSettings(kind, stored.BaseUrl, stored.Model, key, stored.Verified, id, name);
            return new AiProviderConfig(id, name, settings);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>
/// The saved AI providers, kept in the browser's localStorage like <see cref="GrafanaPresetStore"/>. Each
/// provider is stored on its own with its key encrypted, and one of them is the active one used for
/// explanations. A provider whose stored text cannot be read is kept as it is, so saving another one never loses it.
/// </summary>
public sealed class AiSettingsStore
{
    /// <summary>The id given to a provider saved before providers had ids.</summary>
    public const string LegacyId = "default";

    private const string ProvidersKey = "alyce.ai";
    private const string ActiveKey = "alyce.aiActive";

    private readonly IJSRuntime _js;
    private readonly ISecretProtector _protector;

    public AiSettingsStore(IJSRuntime js, ISecretProtector protector)
    {
        _js = js;
        _protector = protector;
    }

    /// <summary>Every saved provider, in the order they were added. Empty when none are saved or storage is unavailable.</summary>
    public async Task<List<AiProviderConfig>> GetAllAsync()
    {
        var list = new List<AiProviderConfig>();
        foreach (var json in await LoadRawAsync(ProvidersKey))
        {
            var config = AiSettingsCodec.Open(json, _protector);
            if (config is not null) list.Add(config);
        }

        return list;
    }

    /// <summary>The id of the provider used for explanations, or null when none is chosen.</summary>
    public async Task<string?> GetActiveIdAsync()
    {
        var stored = await LoadRawAsync(ActiveKey);
        return stored.Count == 0 ? null : stored[0];
    }

    /// <summary>The provider used for explanations: the chosen one, or the first saved one. Null when none is saved.</summary>
    /// <summary>
    /// True when a provider is active and its settings pass validation. Pages use it to enable their AI controls.
    /// False, not an error, when browser storage cannot be read yet.
    /// </summary>
    public async Task<bool> HasUsableProviderAsync()
    {
        try
        {
            var active = await GetActiveAsync();
            return active is not null && active.Settings.Validate() is null;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public async Task<AiProviderConfig?> GetActiveAsync()
    {
        var all = await GetAllAsync();
        if (all.Count == 0) return null;

        var activeId = await GetActiveIdAsync();
        return all.FirstOrDefault(c => c.Id == activeId) ?? all[0];
    }

    /// <summary>
    /// Adds the provider, or replaces the one with the same id. The first provider saved becomes the active one.
    /// Returns false, and stores nothing, when the key cannot be encrypted or the browser refuses the write.
    /// </summary>
    public async Task<bool> SaveAsync(AiProviderConfig config)
    {
        var sealedJson = AiSettingsCodec.Seal(config, _protector);
        if (sealedJson is null) return false;

        var entries = await LoadRawAsync(ProvidersKey);
        var index = entries.FindIndex(e => IdOf(e) == config.Id);
        if (index >= 0) entries[index] = sealedJson;
        else entries.Add(sealedJson);

        var activeId = await GetActiveIdAsync();
        var activeStillExists = activeId is not null && entries.Any(e => IdOf(e) == activeId);

        var wrote = await WriteAsync(ProvidersKey, entries.ToArray());
        if (wrote && !activeStillExists) wrote = await WriteAsync(ActiveKey, new[] { config.Id });
        return wrote;
    }

    /// <summary>Removes the provider. If it was the active one, the first remaining provider becomes active.</summary>
    public async Task RemoveAsync(string id)
    {
        var entries = await LoadRawAsync(ProvidersKey);
        if (entries.RemoveAll(e => IdOf(e) == id) == 0) return;

        await WriteAsync(ProvidersKey, entries.ToArray());
        if (await GetActiveIdAsync() == id)
        {
            var next = entries.Count == 0 ? Array.Empty<string>() : new[] { IdOf(entries[0]) };
            await WriteAsync(ActiveKey, next);
        }
    }

    /// <summary>Chooses the provider used for explanations.</summary>
    public async Task SetActiveAsync(string id) => await WriteAsync(ActiveKey, new[] { id });

    private async Task<List<string>> LoadRawAsync(string key)
    {
        try
        {
            return (await _js.InvokeAsync<string[]>("pathHistoryLoad", key)).ToList();
        }
        catch
        {
            // Pre-render, blocked storage or a damaged entry: the page starts empty.
            return new List<string>();
        }
    }

    private async Task<bool> WriteAsync(string key, string[] values)
    {
        try
        {
            await _js.InvokeVoidAsync("pathHistorySave", key, values);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>The id of a stored entry, read without decrypting it. Entries from before ids existed are the legacy id.</summary>
    private static string IdOf(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty(nameof(StoredAiConfig.Id), out var id) &&
                   id.GetString() is { Length: > 0 } value
                ? value
                : LegacyId;
        }
        catch (JsonException)
        {
            return LegacyId;
        }
    }
}
