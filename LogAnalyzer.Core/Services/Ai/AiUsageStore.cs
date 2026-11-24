using System.Text.Json;
using Microsoft.JSInterop;

namespace LogAnalyzer.Services.Ai;

/// <summary>
/// The tokens and requests used through one saved provider. A request whose provider reported no token counts
/// is counted in <see cref="Requests"/> but not in <see cref="ReportedRequests"/>, so the totals are never guessed.
/// </summary>
public sealed record AiUsage(int Requests, int ReportedRequests, long InputTokens, long OutputTokens, DateTime? LastUsedUtc)
{
    public static readonly AiUsage None = new(0, 0, 0, 0, null);

    /// <summary>The totals with one more answer added.</summary>
    public AiUsage Add(AiResponse response, DateTime nowUtc)
    {
        var reported = response.InputTokens is not null || response.OutputTokens is not null;
        return new AiUsage(
            Requests + 1,
            ReportedRequests + (reported ? 1 : 0),
            InputTokens + (response.InputTokens ?? 0),
            OutputTokens + (response.OutputTokens ?? 0),
            nowUtc);
    }
}

/// <summary>One provider's usage, as it is kept in browser storage.</summary>
public sealed record AiUsageEntry(string Id, AiUsage Usage);

/// <summary>
/// Running token totals for each saved provider, kept in the browser's localStorage next to the providers. This is
/// a per-computer record for the user to read; it is never sent anywhere. A storage failure is ignored, so usage
/// tracking can never stop an answer from being shown.
/// </summary>
public sealed class AiUsageStore
{
    public const string Key = "alyce.aiUsage";

    private readonly IJSRuntime _js;

    public AiUsageStore(IJSRuntime js)
    {
        _js = js;
    }

    /// <summary>The totals by provider id. Providers that were never used are absent.</summary>
    public async Task<IReadOnlyDictionary<string, AiUsage>> GetAllAsync()
    {
        var all = await LoadAsync();
        return all.ToDictionary(e => e.Id, e => e.Usage, StringComparer.Ordinal);
    }

    /// <summary>Adds one answer to the totals of <paramref name="providerId"/>. Does nothing when the id is unknown.</summary>
    public async Task RecordAsync(string? providerId, AiResponse response)
    {
        if (string.IsNullOrEmpty(providerId)) return;

        var entries = (await LoadAsync()).ToList();
        var index = entries.FindIndex(e => e.Id == providerId);
        var current = index >= 0 ? entries[index].Usage : AiUsage.None;
        var updated = new AiUsageEntry(providerId, current.Add(response, DateTime.UtcNow));

        if (index >= 0) entries[index] = updated;
        else entries.Add(updated);
        await SaveAsync(entries);
    }

    /// <summary>Clears the totals of one provider.</summary>
    public async Task ResetAsync(string providerId)
    {
        var entries = (await LoadAsync()).Where(e => e.Id != providerId).ToList();
        await SaveAsync(entries);
    }

    private async Task<List<AiUsageEntry>> LoadAsync()
    {
        var result = new List<AiUsageEntry>();
        try
        {
            foreach (var json in await _js.InvokeAsync<string[]>("pathHistoryLoad", Key))
            {
                var entry = JsonSerializer.Deserialize<AiUsageEntry>(json);
                if (entry is not null && !string.IsNullOrEmpty(entry.Id)) result.Add(entry);
            }
        }
        catch (Exception)
        {
            // Unreadable or unreachable storage: start from nothing rather than fail the caller.
        }
        return result;
    }

    private async Task SaveAsync(IEnumerable<AiUsageEntry> entries)
    {
        try
        {
            await _js.InvokeVoidAsync("pathHistorySave", Key, entries.Select(e => JsonSerializer.Serialize(e)).ToArray());
        }
        catch (Exception)
        {
            // Not saved this time; the next answer adds to what is stored then.
        }
    }
}
