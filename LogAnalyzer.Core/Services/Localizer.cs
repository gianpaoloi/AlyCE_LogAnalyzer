namespace LogAnalyzer.Services;

/// <summary>
/// The interface text for the language in use. Pages write <c>@L["area.key"]</c>; a key with no text in the
/// current language shows the English text, and a key with no English text shows the key itself.
/// <c>@L["area.key", count]</c> fills {0}, {1}… in the text.
/// </summary>
public sealed class Localizer
{
    private readonly LanguageStore _store;
    private readonly Dictionary<string, string> _english = LanguageCatalog.English.Strings;
    private Dictionary<string, string> _current = LanguageCatalog.English.Strings;

    /// <summary>Raised when the language in use changes, so the layout can redraw the page in the new language.</summary>
    public event Action? Changed;

    public Localizer(LanguageStore store)
    {
        _store = store;
    }

    /// <summary>The code of the language in use.</summary>
    public string CurrentCode { get; private set; } = LanguageCatalog.EnglishCode;

    public string this[string key, params object?[] args]
    {
        get
        {
            var text = _current.TryGetValue(key, out var value) && value.Length > 0
                ? value
                : _english.TryGetValue(key, out var english) ? english : key;
            return args.Length == 0 ? text : string.Format(text, args);
        }
    }

    /// <summary>Reads the saved language choice. Raises <see cref="Changed"/> only when it is not English.</summary>
    public async Task InitializeAsync()
    {
        var code = await _store.GetActiveCodeAsync();
        if (code is null || string.Equals(code, CurrentCode, StringComparison.OrdinalIgnoreCase)) return;
        await SetCurrentAsync(code, remember: false);
    }

    /// <summary>Switches to the language with that code and remembers the choice.</summary>
    public Task SetCurrentAsync(string code) => SetCurrentAsync(code, remember: true);

    private async Task SetCurrentAsync(string code, bool remember)
    {
        var language = await _store.FindAsync(code) ?? LanguageCatalog.English;
        CurrentCode = language.Code;
        _current = language.Strings.Count == 0 ? _english : language.Strings;
        if (remember) await _store.SetActiveCodeAsync(language.Code);
        Changed?.Invoke();
    }
}
