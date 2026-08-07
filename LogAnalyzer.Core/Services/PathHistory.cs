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

    /// <summary>Folders and files the user pinned in the file browser.</summary>
    public const string FavoriteKey = "favorites";

    /// <summary>Favourites are pinned on purpose, so they get more room than a recent-paths list.</summary>
    public const int MaxFavorites = 30;

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
    public async Task<List<string>> AddAsync(string key, string? value, int max = MaxEntries)
    {
        var path = value?.Trim() ?? "";
        var list = await GetAsync(key);
        if (path.Length == 0) return list;

        list.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        list.Insert(0, path);
        if (list.Count > max) list.RemoveRange(max, list.Count - max);

        return await SaveAsync(key, list);
    }

    /// <summary>Drops <paramref name="value"/> and returns the updated list. Used to unpin a favourite.</summary>
    public async Task<List<string>> RemoveAsync(string key, string value)
    {
        var list = await GetAsync(key);
        if (list.RemoveAll(p => string.Equals(p, value, StringComparison.OrdinalIgnoreCase)) == 0) return list;
        return await SaveAsync(key, list);
    }

    private async Task<List<string>> SaveAsync(string key, List<string> list)
    {
        try
        {
            await _js.InvokeVoidAsync("pathHistorySave", StoragePrefix + key, list);
        }
        catch
        {
            // Same as GetAsync: failing to persist must not break what the user just asked for.
        }

        return list;
    }
}
