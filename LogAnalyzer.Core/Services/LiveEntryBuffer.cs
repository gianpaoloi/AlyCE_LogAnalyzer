using LogAnalyzer.Models;

namespace LogAnalyzer.Services;

/// <summary>The display filters a live page applies to its buffer. See <see cref="LiveEntryBuffer.Rebuild"/>.</summary>
public readonly record struct LiveDisplayFilter(
    IEnumerable<string> Levels,
    IEnumerable<string> Environments,
    IEnumerable<string> Companies,
    string? LoggerPrefix,
    string? Text);

/// <summary>
/// The buffer behind a live page: it takes batches of entries from a background producer — the file
/// tail (<see cref="LogWatcher"/>) or the network listener (<see cref="NetworkLogListener"/>) — keeps
/// the newest <see cref="MaxKept"/> that match the text filter, and hands the renderer a filtered
/// snapshot of them.
/// <para>
/// Extracted from the page so both live pages share one implementation of the four limits that keep
/// a long watch flat, and so those limits can be tested without a renderer.
/// </para>
/// <para>
/// Thread model: <see cref="Ingest"/> runs on the producer's thread and everything else on the
/// renderer. <see cref="_gate"/> guards nothing but the ring, and is only ever held for a bounded
/// run of array writes — no LINQ, no allocation and no per-batch loop under it. That is the whole
/// point: touching a header filter runs <see cref="Rebuild"/> on the renderer, and the renderer is
/// the single thread that serializes every click, every redraw and the nav menu for the circuit.
/// When it could block behind an ingest batch (which the watcher hands over in chunks of up to
/// 4 MB, sixteen per poll) the entire app froze until that batch finished.
/// </para>
/// </summary>
public sealed class LiveEntryBuffer
{
    /// <summary>Newest entries kept. Older ones are evicted as new ones arrive.</summary>
    public const int MaxKept = 1000;

    /// <summary>
    /// Levels the filter offers before anything has been received. Anything else the log turns out
    /// to contain — FATAL, TRACE, a custom level — is added as it is seen: the list used to be this
    /// array and nothing else, so a FATAL line was displayed but could not be filtered for.
    /// </summary>
    private static readonly string[] KnownLevels = { "ERROR", "WARN", "INFO", "DEBUG" };

    // Caps on the values collected for the header combos and the logger tree. Without them a long
    // watch on a busy multi-tenant log keeps growing the Environment / Company dropdowns and the
    // tree, and the cost of every single refresh grows with it until the page stops keeping up.
    private const int MaxLoggers = 2000;
    private const int MaxDistinctValues = 500;

    // The buffer, as a fixed ring of the newest MaxKept matching entries.
    private readonly object _gate = new();
    private readonly LogEntry[] _ring = new LogEntry[MaxKept];
    private int _ringEnd;      // next slot to write
    private int _ringCount;

    // Everything seen this session (regardless of filter), to populate the tree and the filters.
    // Written only by Ingest, i.e. only by the producer's single thread, and never read by the
    // renderer — it reads the published copies below instead. So no lock on either side.
    private readonly Dictionary<string, int> _loggerCounts = new(StringComparer.Ordinal);
    private readonly SortedSet<string> _envSeen = new(StringComparer.OrdinalIgnoreCase);
    private readonly SortedSet<string> _companySeen = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _levelsSeen = new(KnownLevels, StringComparer.OrdinalIgnoreCase);

    // Reused by the ingest pre-trim; producer thread only.
    private readonly List<LogEntry> _keep = new(MaxKept);

    // Stable snapshots for the header combos and the logger tree, published as whole immutable
    // objects. Only rebuilt when a new value appears, so a dropdown's selection isn't disturbed on
    // every refresh.
    private volatile List<string> _envList = new();
    private volatile List<string> _companyList = new();
    private volatile List<string> _levelList = OrderBySeverity(KnownLevels);
    private volatile IReadOnlyDictionary<string, int> _loggerSnapshot =
        new Dictionary<string, int>(StringComparer.Ordinal);

    // The text filter as the producer thread sees it. Read there, written on the renderer.
    private volatile string? _text;

    private int _dirty;         // a redraw is due
    private int _bufferDirty;   // the snapshot no longer matches the ring

    // While frozen the grid renders this copy of the ring instead of the ring itself, so the row
    // being read doesn't move. Column filters still apply to it; the producer keeps buffering
    // behind. Renderer only — _paused is what the producer thread looks at.
    private LogEntry[]? _frozen;
    private volatile bool _paused;
    private int _pending;       // matching entries buffered since the view was frozen

    /// <summary>Distinct levels seen, most severe first. Safe to hand straight to a dropdown.</summary>
    public List<string> Levels => _levelList;

    /// <summary>Distinct environments seen, alphabetical.</summary>
    public List<string> Environments => _envList;

    /// <summary>Distinct companies seen, alphabetical.</summary>
    public List<string> Companies => _companyList;

    /// <summary>Logger names seen with their counts, for the logger tree.</summary>
    public IReadOnlyDictionary<string, int> Loggers => _loggerSnapshot;

    /// <summary>
    /// What the grid renders: the buffer (or the frozen copy) with the display filters applied.
    /// Replaced wholesale by <see cref="Rebuild"/>; never mutated in place, so the reference the
    /// grid holds stays valid.
    /// </summary>
    public List<LogEntry> Snapshot { get; private set; } = new();

    /// <summary>
    /// The free-text filter. The only filter applied at ingest as well as at display time, so the
    /// buffer itself holds nothing that does not match it.
    /// </summary>
    public string? TextFilter
    {
        get => _text;
        set => _text = value;
    }

    /// <summary>Entries currently in the ring. Approximate by nature — the producer keeps appending.</summary>
    public int BufferedCount => Volatile.Read(ref _ringCount);

    /// <summary>Matching entries buffered since the view was frozen.</summary>
    public int PendingCount => Volatile.Read(ref _pending);

    /// <summary>True while the grid is showing a frozen copy rather than following the producer.</summary>
    public bool IsFrozen => _frozen is not null;

    /// <summary>
    /// Ingest. Runs on the producer's thread, and the batch it is handed is unbounded — a catch-up
    /// poll on a big file can carry hundreds of thousands of entries. Everything expensive here
    /// happens outside <see cref="_gate"/>; only the ring push takes it.
    /// </summary>
    public void Ingest(IReadOnlyList<LogEntry> entries)
    {
        var envChanged = false;
        var companyChanged = false;
        var levelChanged = false;
        var loggerAdded = false;

        foreach (var e in entries)
        {
            // Known values keep counting; new ones stop being collected once a cap is reached.
            if (!string.IsNullOrEmpty(e.Logger))
            {
                if (_loggerCounts.TryGetValue(e.Logger, out var n)) _loggerCounts[e.Logger] = n + 1;
                else if (_loggerCounts.Count < MaxLoggers)
                {
                    _loggerCounts[e.Logger] = 1;
                    loggerAdded = true;
                }
            }
            if (!string.IsNullOrEmpty(e.Environment) && _envSeen.Count < MaxDistinctValues)
                envChanged |= _envSeen.Add(e.Environment);
            if (!string.IsNullOrEmpty(e.Company) && _companySeen.Count < MaxDistinctValues)
                companyChanged |= _companySeen.Add(e.Company);
            if (!string.IsNullOrEmpty(e.Level) && _levelsSeen.Count < MaxDistinctValues)
                levelChanged |= _levelsSeen.Add(e.Level);
        }

        if (envChanged) _envList = _envSeen.ToList();
        if (companyChanged) _companyList = _companySeen.ToList();
        if (levelChanged) _levelList = OrderBySeverity(_levelsSeen);
        // Only republished when a logger is new to us: the tree is rebuilt off the key count, so
        // copying up to MaxLoggers entries for a batch that merely bumped some counters is work
        // nothing would ever look at.
        if (loggerAdded) _loggerSnapshot = new Dictionary<string, int>(_loggerCounts, StringComparer.Ordinal);

        // Only the free-text filter gates the buffer; Level/Environment/logger are applied at
        // display time (Rebuild) so changing a column filter re-filters buffered rows. Collected
        // newest-first and cut off at MaxKept, because only the newest MaxKept can survive the ring
        // anyway: pushing the rest just to have each one evict its predecessor is work thrown away
        // by definition, and it used to be done holding the lock the renderer needs.
        var text = _text;
        var filtering = !string.IsNullOrWhiteSpace(text);
        var matched = 0;
        _keep.Clear();
        for (var i = entries.Count - 1; i >= 0; i--)
        {
            var e = entries[i];
            if (filtering && e.Message.IndexOf(text!, StringComparison.OrdinalIgnoreCase) < 0) continue;
            matched++;
            if (_keep.Count < MaxKept) _keep.Add(e);
        }

        if (matched > 0)
        {
            lock (_gate)
            {
                for (var i = _keep.Count - 1; i >= 0; i--) Push(_keep[i]);
            }
            _keep.Clear();  // so the scratch list doesn't keep a page of entries alive

            // Counted, not shown: the frozen view stays as it is until it is released.
            if (_paused) Interlocked.Add(ref _pending, matched);
            Interlocked.Exchange(ref _bufferDirty, 1);
        }

        // A batch where the text filter matched nothing still moves the header combos and the
        // logger tree, so it is worth a redraw even though the grid's rows are unchanged.
        if (matched > 0 || envChanged || companyChanged || levelChanged || loggerAdded)
            MarkDirty();
    }

    /// <summary>Notes that something the page shows has changed (a status, an error, a counter).</summary>
    public void MarkDirty() => Interlocked.Exchange(ref _dirty, 1);

    /// <summary>True once per change: whether a redraw is due. Clears the flag.</summary>
    public bool TakeDirty() => Interlocked.Exchange(ref _dirty, 0) == 1;

    /// <summary>
    /// True once per change: whether the ring moved, i.e. whether the redraw also needs a
    /// <see cref="Rebuild"/>. Clears the flag.
    /// </summary>
    public bool TakeBufferDirty() => Interlocked.Exchange(ref _bufferDirty, 0) == 1;

    /// <summary>
    /// Rebuilds <see cref="Snapshot"/>: the buffer (or the frozen copy) narrowed by the text box,
    /// the level, environment and company combos and the logger tree. Always called on the renderer,
    /// and never while holding <see cref="_gate"/> — the copy is taken first, the filtering runs on
    /// it.
    /// </summary>
    public void Rebuild(LiveDisplayFilter filter)
    {
        var levels = AsCollection(filter.Levels);
        var envs = AsCollection(filter.Environments);
        var companies = AsCollection(filter.Companies);
        var text = filter.Text;

        IEnumerable<LogEntry> q = _frozen ?? SnapshotBuffer();
        if (levels.Count > 0) q = q.Where(e => levels.Contains(e.Level));
        if (envs.Count > 0) q = q.Where(e => e.Environment != null && envs.Contains(e.Environment));
        if (companies.Count > 0) q = q.Where(e => e.Company != null && companies.Contains(e.Company));
        if (!string.IsNullOrEmpty(filter.LoggerPrefix))
            q = q.Where(e => LogFilter.MatchesPrefix(e.Logger, filter.LoggerPrefix));
        // Also applied here, not just at ingest, so narrowing the box drops the entries already
        // buffered under the previous one instead of leaving them on screen.
        if (!string.IsNullOrWhiteSpace(text))
            q = q.Where(e => e.Message.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0);
        Snapshot = q.ToList();
    }

    /// <summary>
    /// Freezes the buffer as it is now, or releases it so the grid catches up with the producer.
    /// The caller rebuilds afterwards.
    /// </summary>
    public void Freeze(bool frozen)
    {
        _paused = frozen;
        _frozen = frozen ? SnapshotBuffer() : null;
        Interlocked.Exchange(ref _pending, 0);
    }

    /// <summary>Empties the buffer. The caller rebuilds afterwards.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            Array.Clear(_ring);
            _ringEnd = 0;
            _ringCount = 0;
        }
        // Still frozen if it was frozen — the user asked for a still view, now an empty one.
        if (_frozen is not null) _frozen = [];
        Interlocked.Exchange(ref _pending, 0);
    }

    /// <summary>Appends one entry to the ring, evicting the oldest. Caller holds <see cref="_gate"/>.</summary>
    private void Push(LogEntry e)
    {
        _ring[_ringEnd] = e;
        _ringEnd = _ringEnd + 1 == _ring.Length ? 0 : _ringEnd + 1;
        if (_ringCount < _ring.Length) _ringCount++;
    }

    /// <summary>Copies the ring out newest-first. The lock covers the copy and nothing else.</summary>
    private LogEntry[] SnapshotBuffer()
    {
        lock (_gate)
        {
            if (_ringCount == 0) return [];
            var result = new LogEntry[_ringCount];
            var i = _ringEnd;
            for (var n = 0; n < _ringCount; n++)
            {
                i = i == 0 ? _ring.Length - 1 : i - 1;
                result[n] = _ring[i];
            }
            return result;
        }
    }

    private static ICollection<string> AsCollection(IEnumerable<string>? values) => values switch
    {
        null => Array.Empty<string>(),
        ICollection<string> collection => collection,
        _ => values.ToList(),
    };

    /// <summary>ERROR first, then WARN / INFO / DEBUG, then anything unrecognised.</summary>
    private static List<string> OrderBySeverity(IEnumerable<string> levels) =>
        levels.OrderBy(Rank).ThenBy(l => l, StringComparer.OrdinalIgnoreCase).ToList();

    private static int Rank(string level) => LogLevels.Series(level) switch
    {
        LogLevels.Error => 0,
        LogLevels.Warn => 1,
        LogLevels.Info => 2,
        LogLevels.Debug => 3,
        _ => 4,
    };
}
