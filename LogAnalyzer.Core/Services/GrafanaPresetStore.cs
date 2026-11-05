using System.Text.Json;
using Microsoft.JSInterop;

namespace LogAnalyzer.Services;

/// <summary>A named set of Grafana selectors and time window, saved so it can be reloaded later.</summary>
public sealed record GrafanaPreset(string Name, string Team, string ServiceName, string Environment, int WindowMinutes);

/// <summary>The Grafana address, datasource and sign-in last used, remembered when the user asks for it.</summary>
public sealed record GrafanaSignIn(string BaseUrl, string DatasourceUid, string? Token, string? SessionCookie);

/// <summary>
/// Grafana import presets and the remembered sign-in, kept in the browser's localStorage like
/// <see cref="PathHistory"/> so they survive a restart. The sign-in is stored in plain text in this
/// user's profile, so the dialog only saves it when the user ticks "Remember sign-in".
/// </summary>
public sealed class GrafanaPresetStore
{
    // Stored through the generic localStorage helpers in download.js (pathHistoryLoad/Save take any key).
    private const string StorageKey = "alyce.grafanaPresets";
    private const string SignInKey = "alyce.grafanaSignIn";
    private const string LastLinkKey = "alyce.grafanaLastLink";

    private readonly IJSRuntime _js;

    public GrafanaPresetStore(IJSRuntime js) => _js = js;

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
            return stored.Length == 0 ? null : JsonSerializer.Deserialize<GrafanaSignIn>(stored[0]);
        }
        catch
        {
            // Pre-render, blocked storage or a damaged entry: the dialog simply starts empty.
            return null;
        }
    }

    public Task SaveSignInAsync(GrafanaSignIn signIn) =>
        WriteRawAsync(SignInKey, new[] { JsonSerializer.Serialize(signIn) });

    public Task ForgetSignInAsync() => WriteRawAsync(SignInKey, Array.Empty<string>());

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

    public Task SaveLastLinkAsync(string link) => WriteRawAsync(LastLinkKey, new[] { link.Trim() });

    private async Task WriteRawAsync(string key, string[] values)
    {
        try
        {
            await _js.InvokeVoidAsync("pathHistorySave", key, values);
        }
        catch
        {
            // Failing to remember must not block the import the user asked for.
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
