using System.Text.Json;
using Microsoft.JSInterop;

namespace LogAnalyzer.Services;

/// <summary>
/// Saves the user's own languages and which language is active, in the browser's localStorage like
/// <see cref="SkinStore"/>. The built-in languages are not stored.
/// </summary>
public sealed class LanguageStore
{
    // Stored through the generic localStorage helpers in download.js (pathHistoryLoad/Save take any key).
    private const string LanguagesKey = "alyce.languages";
    private const string ActiveKey = "alyce.activeLanguage";

    private readonly IJSRuntime _js;

    /// <summary>Raised after the saved languages or the active language change, so menus can refresh.</summary>
    public event Action? Changed;

    public LanguageStore(IJSRuntime js)
    {
        _js = js;
    }

    /// <summary>The user's own languages, sorted by name. Empty when none are saved or storage is unavailable.</summary>
    public async Task<List<AppLanguage>> GetCustomAsync()
    {
        var languages = new List<AppLanguage>();
        try
        {
            var stored = await _js.InvokeAsync<string[]>("pathHistoryLoad", LanguagesKey);
            foreach (var json in stored)
            {
                try
                {
                    var language = JsonSerializer.Deserialize<AppLanguage>(json);
                    if (language is not null && LanguageCatalog.Validate(language) is null)
                        languages.Add(language with { Strings = LanguageCatalog.Trimmed(language.Strings) });
                }
                catch (JsonException)
                {
                    // A damaged entry is dropped rather than failing the whole list.
                }
            }
        }
        catch
        {
            // No JS yet (pre-render), circuit gone, or storage blocked: languages are optional.
        }

        return languages.OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Adds or replaces the language with the same code. Returns false when it is invalid or cannot be stored.</summary>
    public async Task<bool> SaveAsync(AppLanguage language)
    {
        if (LanguageCatalog.Validate(language) is not null) return false;

        var clean = language with { Code = language.Code.Trim().ToLowerInvariant(), Name = language.Name.Trim(), Strings = LanguageCatalog.Trimmed(language.Strings) };
        var list = await GetCustomAsync();
        list.RemoveAll(l => string.Equals(l.Code, clean.Code, StringComparison.OrdinalIgnoreCase));
        list.Add(clean);
        return await WriteAsync(list);
    }

    /// <summary>Removes the language with that code. Built-in languages cannot be removed.</summary>
    public async Task<bool> DeleteAsync(string code)
    {
        var list = await GetCustomAsync();
        if (list.RemoveAll(l => string.Equals(l.Code, code, StringComparison.OrdinalIgnoreCase)) == 0) return false;
        return await WriteAsync(list);
    }

    /// <summary>The code of the language last chosen, or null when none was chosen.</summary>
    public async Task<string?> GetActiveCodeAsync()
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

    public async Task SetActiveCodeAsync(string code)
    {
        try
        {
            await _js.InvokeVoidAsync("pathHistorySave", ActiveKey, new[] { code });
            Changed?.Invoke();
        }
        catch
        {
            // Failing to remember the choice must not stop the language being shown.
        }
    }

    /// <summary>Finds a language by code among the built-in and the user's own. Null when there is none.</summary>
    public async Task<AppLanguage?> FindAsync(string code)
    {
        var builtIn = LanguageCatalog.BuiltIn.FirstOrDefault(l => string.Equals(l.Code, code, StringComparison.OrdinalIgnoreCase));
        if (builtIn is not null) return builtIn;
        return (await GetCustomAsync()).FirstOrDefault(l => string.Equals(l.Code, code, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<bool> WriteAsync(List<AppLanguage> list)
    {
        try
        {
            var values = list.Select(l => JsonSerializer.Serialize(l)).ToArray();
            await _js.InvokeVoidAsync("pathHistorySave", LanguagesKey, values);
            Changed?.Invoke();
            return true;
        }
        catch
        {
            return false;
        }
    }
}
