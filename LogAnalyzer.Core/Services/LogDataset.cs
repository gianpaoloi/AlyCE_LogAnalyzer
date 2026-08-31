using LogAnalyzer.Models;

namespace LogAnalyzer.Services;

/// <summary>
/// One immutable snapshot of loaded log data, published as a single reference by
/// <see cref="LogStore"/>.
/// <para>
/// Loading used to append to, and re-sort, the very list that the Explorer was walking — from a
/// different thread, on the MAUI drag-and-drop path — which threw "collection was modified" mid
/// query. Publishing a new instance instead makes that impossible: a reader takes the reference
/// once and the data behind it never changes.
/// </para>
/// </summary>
public sealed class LogDataset
{
    public static readonly LogDataset Empty = new(Array.Empty<LogEntry>(), null, null);

    /// <summary>
    /// Below this, a linear scan is already fast enough that the facet indexes would be pure
    /// overhead — three int arrays the size of the dataset, built for nothing.
    /// </summary>
    private const int MinEntriesForIndex = 50_000;

    private readonly Dictionary<string, int[]>? _byLevel;
    private readonly Dictionary<string, int[]>? _byEnvironment;
    private readonly Dictionary<string, int[]>? _byCompany;

    public IReadOnlyList<LogEntry> Entries { get; }
    public LogStats? Stats { get; private set; }
    public string? LoadedPath { get; }

    public LogDataset(IReadOnlyList<LogEntry> entries, LogStats? stats, string? loadedPath)
    {
        Entries = entries;
        Stats = stats;
        LoadedPath = loadedPath;

        if (entries.Count < MinEntriesForIndex) return;

        // Three independent passes over the same list, so they run side by side: on a large load
        // this is a visible part of the total time, and it is work the old code never did.
        Dictionary<string, int[]>? level = null, environment = null, company = null;
        Parallel.Invoke(
            () => level = BuildIndex(entries, e => e.Level),
            () => environment = BuildIndex(entries, e => e.Environment),
            () => company = BuildIndex(entries, e => e.Company));

        _byLevel = level;
        _byEnvironment = environment;
        _byCompany = company;
    }

    /// <summary>
    /// Attaches statistics computed alongside the indexes. Only legal before the dataset is
    /// published — <see cref="LogStore"/> does this while still holding the only reference to it.
    /// </summary>
    internal void AttachStats(LogStats stats) => Stats = stats;

    /// <summary>
    /// The entries worth testing against <paramref name="filter"/>, in chronological order.
    /// <para>
    /// Picks whichever of the level / environment / company selections narrows the set most and
    /// walks only those entries; everything else still goes through <see cref="LogFilter.Matches"/>.
    /// Filtering a million entries down to the few thousand ERROR rows used to scan all million.
    /// </para>
    /// </summary>
    public IEnumerable<LogEntry> Candidates(LogFilter filter)
    {
        var best = Narrowest(filter);
        if (best is null || best.Length >= Entries.Count) return Entries;

        return Project(best);
    }

    private IEnumerable<LogEntry> Project(int[] positions)
    {
        foreach (var i in positions) yield return Entries[i];
    }

    /// <summary>The smallest posting list among the facets the filter actually constrains.</summary>
    private int[]? Narrowest(LogFilter filter)
    {
        var best = Postings(_byLevel, filter.Levels);

        var environments = Postings(_byEnvironment, filter.Environments);
        if (Smaller(environments, best)) best = environments;

        // The single-environment field is an older, separate filter than the multi-select one.
        if (!string.IsNullOrWhiteSpace(filter.Environment))
        {
            var single = Postings(_byEnvironment, new[] { filter.Environment! });
            if (Smaller(single, best)) best = single;
        }

        var companies = Postings(_byCompany, filter.Companies);
        if (Smaller(companies, best)) best = companies;

        return best;
    }

    private static bool Smaller(int[]? candidate, int[]? current) =>
        candidate is not null && (current is null || candidate.Length < current.Length);

    /// <summary>
    /// Merges the posting lists of the selected values into one chronologically ordered array.
    /// Null when there is no index, no selection, or a selected value that isn't in the index —
    /// the last case would otherwise silently under-report entries the index doesn't cover.
    /// </summary>
    private static int[]? Postings(Dictionary<string, int[]>? index, IReadOnlyCollection<string>? values)
    {
        if (index is null || values is null || values.Count == 0) return null;

        if (values.Count == 1)
        {
            var only = values.First();
            return only is not null && index.TryGetValue(only, out var single) ? single : Array.Empty<int>();
        }

        var lists = new List<int[]>(values.Count);
        var total = 0;
        foreach (var value in values)
        {
            if (value is null) continue;
            if (!index.TryGetValue(value, out var list)) continue;   // nothing has this value
            lists.Add(list);
            total += list.Length;
        }

        if (lists.Count == 0) return Array.Empty<int>();
        if (lists.Count == 1) return lists[0];

        var merged = new int[total];
        var at = 0;
        foreach (var list in lists)
        {
            Array.Copy(list, 0, merged, at, list.Length);
            at += list.Length;
        }

        // Entries are stored in time order, so sorting the positions restores it.
        Array.Sort(merged);
        return merged;
    }

    private static Dictionary<string, int[]> BuildIndex(
        IReadOnlyList<LogEntry> entries, Func<LogEntry, string?> key)
    {
        // Comparer matches LogFilter's sets, so a lookup can't miss on casing alone.
        var buckets = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < entries.Count; i++)
        {
            var value = key(entries[i]);
            if (string.IsNullOrEmpty(value)) continue;
            if (!buckets.TryGetValue(value, out var list))
            {
                list = new List<int>();
                buckets[value] = list;
            }
            list.Add(i);
        }

        var index = new Dictionary<string, int[]>(buckets.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var (value, list) in buckets) index[value] = list.ToArray();
        return index;
    }
}
