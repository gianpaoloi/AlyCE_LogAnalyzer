using LogAnalyzer.Models;

namespace LogAnalyzer.Services;

/// <summary>Why an entry was matched to the one it was compared with.</summary>
public enum CorrelationReason
{
    /// <summary>The same correlation id (CID / NDC) as the seed entry.</summary>
    Cid,

    /// <summary>The same thread in the same environment, close in time to the seed entry.</summary>
    Thread,
}

/// <summary>An entry found to belong with a seed entry, and the rule that matched it.</summary>
public sealed record CorrelatedEntry(LogEntry Entry, CorrelationReason Reason);

/// <summary>
/// Finds the log lines that belong with one entry: the same correlation id, or the same thread in the
/// same environment within a time window. The seed entry itself is never returned.
/// </summary>
public static class LogCorrelation
{
    public static readonly TimeSpan DefaultWindow = TimeSpan.FromMinutes(2);

    public const int DefaultMax = 50;

    /// <param name="entries">The entries to search, in any order.</param>
    /// <param name="seed">The entry to find relatives of.</param>
    /// <param name="window">How far apart in time a same-thread entry may be.</param>
    /// <param name="max">At most this many results, the ones closest in time to the seed.</param>
    /// <returns>The matches in chronological order.</returns>
    public static IReadOnlyList<CorrelatedEntry> Find(IReadOnlyList<LogEntry> entries, LogEntry seed,
                                                      TimeSpan window, int max = DefaultMax)
    {
        var hasCid = !string.IsNullOrWhiteSpace(seed.Cid);
        var hasThread = !string.IsNullOrEmpty(seed.ThreadId);
        if (!hasCid && !hasThread) return Array.Empty<CorrelatedEntry>();

        var found = new List<(CorrelatedEntry Item, TimeSpan Distance)>();
        foreach (var e in entries)
        {
            if (ReferenceEquals(e, seed)) continue;

            var distance = (e.Time - seed.Time).Duration();
            CorrelationReason? reason = null;
            if (hasCid && string.Equals(e.Cid, seed.Cid, StringComparison.Ordinal))
                reason = CorrelationReason.Cid;
            else if (hasThread && string.Equals(e.ThreadId, seed.ThreadId, StringComparison.Ordinal)
                     && SameEnvironment(e, seed) && distance <= window)
                reason = CorrelationReason.Thread;

            if (reason is { } r) found.Add((new CorrelatedEntry(e, r), distance));
        }

        return found
            .OrderBy(f => f.Distance)
            .Take(max)
            .OrderBy(f => f.Item.Entry.Time)
            .Select(f => f.Item)
            .ToList();
    }

    private static bool SameEnvironment(LogEntry a, LogEntry b) =>
        string.Equals(a.Environment ?? "", b.Environment ?? "", StringComparison.OrdinalIgnoreCase);
}
