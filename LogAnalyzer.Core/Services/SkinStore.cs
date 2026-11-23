using System.Text.Json;
using Microsoft.JSInterop;

namespace LogAnalyzer.Services;

/// <summary>
/// Saves the user's skins and which one is active, in the browser's localStorage like
/// <see cref="GrafanaPresetStore"/>, and applies a skin to the page. The built-in skins are not stored.
/// </summary>
public sealed class SkinStore
{
    // Stored through the generic localStorage helpers in download.js (pathHistoryLoad/Save take any key).
    private const string SkinsKey = "alyce.skins";
    private const string ActiveKey = "alyce.activeSkin";

    private readonly IJSRuntime _js;

    /// <summary>Raised after the saved skins or the active skin change, so other components can refresh.</summary>
    public event Action? Changed;

    public SkinStore(IJSRuntime js)
    {
        _js = js;
    }

    /// <summary>The user's own skins, sorted by name. Empty when none are saved or storage is unavailable.</summary>
    public async Task<List<AppSkin>> GetCustomAsync()
    {
        var skins = new List<AppSkin>();
        try
        {
            var stored = await _js.InvokeAsync<string[]>("pathHistoryLoad", SkinsKey);
            foreach (var json in stored)
            {
                try
                {
                    var skin = JsonSerializer.Deserialize<AppSkin>(json);
                    if (skin is not null && SkinCatalog.Validate(skin) is null) skins.Add(SkinCatalog.Resolve(skin));
                }
                catch (JsonException)
                {
                    // A damaged entry is dropped rather than failing the whole list.
                }
            }
        }
        catch
        {
            // No JS yet (pre-render), circuit gone, or storage blocked: skins are optional.
        }

        return skins.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Adds or replaces the skin with the same name (case-insensitive). Returns false when it is invalid or cannot be stored.</summary>
    public async Task<bool> SaveAsync(AppSkin skin)
    {
        if (SkinCatalog.Validate(skin) is not null) return false;

        var list = await GetCustomAsync();
        list.RemoveAll(s => string.Equals(s.Name, skin.Name.Trim(), StringComparison.OrdinalIgnoreCase));
        list.Add(SkinCatalog.Resolve(skin with { Name = skin.Name.Trim() }));
        return await WriteAsync(list);
    }

    /// <summary>Removes the skin with that name. Built-in skins cannot be removed.</summary>
    public async Task<bool> DeleteAsync(string name)
    {
        var list = await GetCustomAsync();
        if (list.RemoveAll(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)) == 0) return false;
        return await WriteAsync(list);
    }

    /// <summary>The name of the skin last applied, or null when none was chosen.</summary>
    public async Task<string?> GetActiveNameAsync()
    {
        try
        {
            var stored = await _js.InvokeAsync<string[]>("pathHistoryLoad", ActiveKey);
            return stored.Length == 0 ? null : stored[0];
        }
        catch
        {
            return null;
        }
    }

    public async Task SetActiveNameAsync(string name)
    {
        try
        {
            await _js.InvokeVoidAsync("pathHistorySave", ActiveKey, new[] { name });
            Changed?.Invoke();
        }
        catch
        {
            // Failing to remember the choice must not stop the skin being applied.
        }
    }

    /// <summary>Finds a skin by name among the built-in and the user's own. Null when there is none with that name.</summary>
    public async Task<AppSkin?> FindAsync(string name)
    {
        // The built-in skin called "Slate" was renamed "Blue"; keep a saved choice of the old name working.
        if (string.Equals(name, "Slate", StringComparison.OrdinalIgnoreCase)) name = "Blue";
        // The built-in skin called "Launcher" was renamed "Default".
        if (string.Equals(name, "Launcher", StringComparison.OrdinalIgnoreCase)) name = SkinCatalog.DefaultName;

        var builtIn = SkinCatalog.BuiltIn.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
        if (builtIn is not null) return SkinCatalog.Resolve(builtIn);
        return (await GetCustomAsync()).FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The skin to show now: the active one if it still exists, otherwise the default.</summary>
    public async Task<AppSkin> GetActiveAsync()
    {
        var name = await GetActiveNameAsync();
        return (name is not null ? await FindAsync(name) : null) ?? SkinCatalog.Resolve(SkinCatalog.Default);
    }

    /// <summary>Sets the page's colours to the skin. Fails quietly when the page is not ready yet.</summary>
    public async Task ApplyAsync(AppSkin skin)
    {
        try
        {
            await _js.InvokeVoidAsync("applySkinVariables", SkinCatalog.Resolve(skin).Variables);
        }
        catch
        {
            // Not rendered yet, or the circuit has gone: the next apply will set the colours.
        }
    }

    private async Task<bool> WriteAsync(List<AppSkin> list)
    {
        try
        {
            var values = list.Select(s => JsonSerializer.Serialize(s)).ToArray();
            await _js.InvokeVoidAsync("pathHistorySave", SkinsKey, values);
            Changed?.Invoke();
            return true;
        }
        catch
        {
            return false;
        }
    }
}
