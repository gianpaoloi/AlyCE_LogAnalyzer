using Microsoft.JSInterop;

namespace LogAnalyzer.Services;

/// <summary>
/// Recently used paths per input box, kept in the browser's localStorage.
/// <para>
/// Deliberately not in <see cref="SessionState"/>: that only lives for the circuit / WebView, and
/// these need to survive an app restart. localStorage also keeps them per user and works the same
/// in the Blazor Server host and in the MAUI WebView.
/// </para>
/// </summary>
public sealed class PathHistory
{
    /// <summary>The folder box on the load panel.</summary>
    public const string LogFolderKey = "logFolder";

    /// <summary>The file box on the live-watch page.</summary>
    public const string LiveFileKey = "liveFile";

    private const string StoragePrefix = "alyce.pathHistory.";
    private const int MaxEntries = 12;

    private readonly IJSRuntime _js;

    public PathHistory(IJSRuntime js) => _js = js;

    /// <summary>Most recently used first. Empty when nothing is stored yet.</summary>
    public async Task<List<string>> GetAsync(string key)
    {
        try
        {
            var stored = await _js.InvokeAsync<string[]>("pathHistoryLoad", StoragePrefix + key);
            return stored.ToList();
        }
        catch
        {
            // No JS yet (pre-render), circuit gone, or storage blocked by policy — history is optional.
            return new List<string>();
        }
    }

    /// <summary>
    /// Moves <paramref name="value"/> to the front, de-duplicated case-insensitively (Windows paths),
    /// and returns the updated list so the caller can rebind without a second round trip.
    /// </summary>
    public async Task<List<string>> AddAsync(string key, string? value)
    {
        var path = value?.Trim() ?? "";
        var list = await GetAsync(key);
        if (path.Length == 0) return list;

        list.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        list.Insert(0, path);
        if (list.Count > MaxEntries) list.RemoveRange(MaxEntries, list.Count - MaxEntries);

        try
        {
            await _js.InvokeVoidAsync("pathHistorySave", StoragePrefix + key, list);
        }
        catch
        {
            // Same as above: failing to persist must not break the load the user just asked for.
        }

        return list;
    }
}
