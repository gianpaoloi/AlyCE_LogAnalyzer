using System.Text.Json;
using Microsoft.JSInterop;

namespace LogAnalyzer.Services;

/// <summary>A named set of Grafana selectors and time window, saved so it can be reloaded later.</summary>
public sealed record GrafanaPreset(string Name, string Team, string ServiceName, string Environment, int WindowMinutes);

/// <summary>
/// The Grafana address, datasource and sign-in last used, remembered when the user asks for it.
/// <see cref="UseToken"/> says which of the two secrets is the current sign-in mode; older saves lack it and read as the cookie.
/// </summary>
public sealed record GrafanaSignIn(string BaseUrl, string DatasourceUid, string? Token, string? SessionCookie, bool UseToken = false);

/// <summary>
/// Grafana import presets and the remembered sign-in, kept in the browser's localStorage like
/// <see cref="PathHistory"/> so they survive a restart. The token and cookie are encrypted with an
/// <see cref="ISecretProtector"/> before they are stored, and the dialog only saves them when the user
/// ticks "Remember sign-in".
/// </summary>
public sealed class GrafanaPresetStore
{
    // Stored through the generic localStorage helpers in download.js (pathHistoryLoad/Save take any key).
    private const string StorageKey = "alyce.grafanaPresets";
    private const string SignInKey = "alyce.grafanaSignIn";
    private const string LastLinkKey = "alyce.grafanaLastLink";
    private const string SectionsKey = "alyce.grafanaSections";

    private readonly IJSRuntime _js;
    private readonly ISecretProtector _protector;

    public GrafanaPresetStore(IJSRuntime js, ISecretProtector protector)
    {
        _js = js;
        _protector = protector;
    }

    /// <summary>Presets sorted by name. Empty when nothing is stored yet or storage is unavailable.</summary>
    public async Task<List<GrafanaPreset>> GetAsync()
    {
        try
        {
            var stored = await _js.InvokeAsync<string[]>("pathHistoryLoad", StorageKey);
            var presets = new List<GrafanaPreset>(stored.Length);
            foreach (var json in stored)
            {
                try
                {
                    var preset = JsonSerializer.Deserialize<GrafanaPreset>(json);
                    if (preset is not null && !string.IsNullOrWhiteSpace(preset.Name)) presets.Add(preset);
                }
                catch (JsonException)
                {
                    // A damaged entry is dropped rather than failing the whole list.
                }
            }

            return presets.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }
        catch
        {
            // No JS yet (pre-render), circuit gone, or storage blocked: presets are optional.
            return new List<GrafanaPreset>();
        }
    }

    /// <summary>Adds or replaces the preset with the same name (case-insensitive) and returns the list.</summary>
    public async Task<List<GrafanaPreset>> SaveAsync(GrafanaPreset preset)
    {
        var list = await GetAsync();
        list.RemoveAll(p => string.Equals(p.Name, preset.Name, StringComparison.OrdinalIgnoreCase));
        list.Add(preset);
        return await WriteAsync(list);
    }

    /// <summary>Removes the preset with that name and returns the list.</summary>
    public async Task<List<GrafanaPreset>> DeleteAsync(string name)
    {
        var list = await GetAsync();
        if (list.RemoveAll(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) == 0) return list;
        return await WriteAsync(list);
    }

    /// <summary>The remembered sign-in, or null when none was saved.</summary>
    public async Task<GrafanaSignIn?> GetSignInAsync()
    {
        try
        {
            var stored = await _js.InvokeAsync<string[]>("pathHistoryLoad", SignInKey);
            if (stored.Length == 0) return null;

            var read = GrafanaSignInCodec.Open(stored[0], _protector);
            if (read is null) return null;

            if (read.Legacy)
            {
                // Saved in plain text before encryption: store it again encrypted, or forget it if that fails.
                if (!await SaveSignInAsync(read.SignIn)) await ForgetSignInAsync();
            }

            return read.SignIn;
        }
        catch
        {
            // Pre-render, blocked storage or a damaged entry: the dialog simply starts empty.
            return null;
        }
    }

    /// <summary>
    /// Stores the sign-in with its secrets encrypted. Returns false, and stores nothing, when they cannot
    /// be encrypted or the browser refuses the write.
    /// </summary>
    public async Task<bool> SaveSignInAsync(GrafanaSignIn signIn)
    {
        var sealedJson = GrafanaSignInCodec.Seal(signIn, _protector);
        return sealedJson is not null && await WriteRawAsync(SignInKey, new[] { sealedJson });
    }

    public async Task ForgetSignInAsync() => await WriteRawAsync(SignInKey, Array.Empty<string>());

    /// <summary>The Grafana link pasted last, or null. A link holds no secret, so it is kept without the sign-in.</summary>
    public async Task<string?> GetLastLinkAsync()
    {
        try
        {
            var stored = await _js.InvokeAsync<string[]>("pathHistoryLoad", LastLinkKey);
            return stored.Length == 0 ? null : stored[0];
        }
        catch
        {
            return null;
        }
    }

    public async Task SaveLastLinkAsync(string link) => await WriteRawAsync(LastLinkKey, new[] { link.Trim() });

    /// <summary>
    /// Which collapsible sections of the import dialog were open, by section name. Sections missing from the
    /// result were never saved and keep their defaults. Not secret, so it is kept as plain "name=0/1" entries.
    /// </summary>
    public async Task<Dictionary<string, bool>> GetSectionsAsync()
    {
        var sections = new Dictionary<string, bool>();
        try
        {
            var stored = await _js.InvokeAsync<string[]>("pathHistoryLoad", SectionsKey);
            foreach (var entry in stored)
            {
                var parts = entry.Split('=', 2);
                if (parts.Length == 2) sections[parts[0]] = parts[1] == "1";
            }
        }
        catch
        {
            // Blocked storage: the sections keep their defaults.
        }

        return sections;
    }

    public async Task SaveSectionsAsync(IReadOnlyDictionary<string, bool> sections) =>
        await WriteRawAsync(SectionsKey, sections.Select(s => $"{s.Key}={(s.Value ? "1" : "0")}").ToArray());

    private async Task<bool> WriteRawAsync(string key, string[] values)
    {
        try
        {
            await _js.InvokeVoidAsync("pathHistorySave", key, values);
            return true;
        }
        catch
        {
            // Failing to remember must not block the import the user asked for.
            return false;
        }
    }

    private async Task<List<GrafanaPreset>> WriteAsync(List<GrafanaPreset> list)
    {
        try
        {
            var values = list.Select(p => JsonSerializer.Serialize(p)).ToArray();
            await _js.InvokeVoidAsync("pathHistorySave", StorageKey, values);
        }
        catch
        {
            // Failing to persist must not block the import the user asked for.
        }

        return list.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }
}
