using System.IO.Compression;
using LogAnalyzer.Models;

namespace LogAnalyzer.Services;

/// <summary>
/// In-memory store of parsed log entries. Holds one <see cref="LogDataset"/> at a time and
/// replaces it wholesale when a load finishes, so queries never see half-loaded data.
/// <para>
/// Registered per user session in the web host and as a singleton in the desktop app — see the
/// service registrations for why.
/// </para>
/// </summary>
public sealed class LogStore
{
    /// <summary>
    /// Rough bytes per log line, measured on the sample corpus. Used only to pre-size the entry
    /// lists: the old fixed <c>capacity: 1_000_000</c> allocated an 8 MB array to hold 600 lines.
    /// </summary>
    private const int EstimatedBytesPerEntry = 250;

    private const int MaxEstimatedCapacity = 4_000_000;

    /// <summary>
    /// Floor between progress notifications. A load raised one event per file, and each one made
    /// every open page redo its full query — the Explorer re-filtered the whole previous dataset
    /// once per file, and triage re-clustered it. Only the counters changed.
    /// </summary>
    private const int ProgressThrottleMs = 250;

    private volatile LogDataset _data = LogDataset.Empty;
    private int _loading;                        // 0/1 via Interlocked: only one load at a time
    private CancellationTokenSource? _loadCts;
    private long _lastProgressAt;

    // Not volatile: these are touched with Interlocked / Volatile helpers, and taking a ref to a
    // volatile field drops the volatility (CS0420) anyway.
    private int _filesProcessed;
    private volatile int _filesTotal;
    private long _entriesParsed;

    public IReadOnlyList<LogEntry> Entries => _data.Entries;
    public LogStats? Stats => _data.Stats;
    public string? LoadedPath => _data.LoadedPath;
    public bool IsLoaded => _data.Stats is not null;
    public bool IncludeDebug { get; set; }

    // ---- progress state, observed by the UI while a load runs ----
    public bool IsLoading => Volatile.Read(ref _loading) == 1;
    public int FilesProcessed => Volatile.Read(ref _filesProcessed);
    public int FilesTotal => _filesTotal;
    public long EntriesParsed => Interlocked.Read(ref _entriesParsed);
    public string? LoadError { get; private set; }

    /// <summary>True while a running load can still be cancelled.</summary>
    public bool CanCancel => _loadCts is not null;

    /// <summary>Raised when the loaded dataset is replaced, or a load finishes or fails.</summary>
    public event Action? DatasetChanged;

    /// <summary>Raised while a load runs, throttled. Only the progress counters have changed.</summary>
    public event Action? ProgressChanged;

    private void RaiseDataset() => DatasetChanged?.Invoke();

    private void RaiseProgress(bool force = false)
    {
        if (!force)
        {
            var now = Environment.TickCount64;
            if (now - Interlocked.Read(ref _lastProgressAt) < ProgressThrottleMs) return;
            Interlocked.Exchange(ref _lastProgressAt, now);
        }

        ProgressChanged?.Invoke();
    }

    /// <summary>Asks a running load to stop. The dataset already in place is left alone.</summary>
    public void CancelLoad()
    {
        try { _loadCts?.Cancel(); } catch (ObjectDisposedException) { /* already finished */ }
    }

    /// <summary>Clears the currently loaded dataset and resets progress/error state.</summary>
    public void Clear()
    {
        if (IsLoading) return;

        _data = LogDataset.Empty;
        LoadError = null;
        _filesProcessed = 0;
        _filesTotal = 0;
        Interlocked.Exchange(ref _entriesParsed, 0);
        RaiseDataset();
    }

    // ---------------------------------------------------------------- loading

    /// <summary>
    /// Loads dropped filesystem paths. Supports one .zip file or one/many .log files.
    /// </summary>
    public async Task LoadFromPathsAsync(IEnumerable<string> paths, bool includeDebug, CancellationToken ct)
    {
        var list = paths
            .Where(p => !string.IsNullOrWhiteSpace(p) && File.Exists(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var zipFiles = list.Where(p => p.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)).ToList();
        var logFiles = list.Where(p => p.EndsWith(".log", StringComparison.OrdinalIgnoreCase)).ToList();

        if (zipFiles.Count > 1 || (zipFiles.Count > 0 && logFiles.Count > 0))
        {
            LoadError = "Use one ZIP file or one/more LOG files, not both together.";
            RaiseDataset();
            return;
        }

        if (zipFiles.Count == 1)
        {
            await using var stream = File.OpenRead(zipFiles[0]);
            await LoadFromZipAsync(stream, Path.GetFileName(zipFiles[0]), includeDebug, ct);
            return;
        }

        if (logFiles.Count > 0)
        {
            // One load for the whole set, not one per file: appending each file separately re-sorted
            // everything already loaded, so dropping N files cost N sorts of a growing list.
            var sources = logFiles.Select(FileSource).ToList();
            var label = logFiles.Count == 1
                ? Path.GetFileName(logFiles[0])
                : $"{logFiles.Count} files";
            await RunLoadAsync(sources, label, includeDebug, parallel: true, append: false, ct);
            return;
        }

        LoadError = "Only .zip and .log files are supported.";
        RaiseDataset();
    }

    /// <summary>Loads every *.log file in <paramref name="folder"/>. Optionally skips DEBUG entries.</summary>
    public async Task LoadAsync(string folder, bool includeDebug, CancellationToken ct)
    {
        List<LoadSource> sources;
        try
        {
            if (!Directory.Exists(folder))
                throw new DirectoryNotFoundException($"Folder not found: {folder}");

            sources = Directory.GetFiles(folder, "*.log").OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                               .Select(FileSource).ToList();
        }
        catch (Exception ex)
        {
            LoadError = ex.Message;
            RaiseDataset();
            return;
        }

        await RunLoadAsync(sources, folder, includeDebug, parallel: true, append: false, ct);
    }

    /// <summary>
    /// Loads every *.log entry inside an uploaded ZIP. The stream is copied to a temp file first
    /// (ZipArchive needs a seekable source), then each entry is parsed like a folder load.
    /// </summary>
    public async Task LoadFromZipAsync(Stream zipStream, string displayName, bool includeDebug, CancellationToken ct)
    {
        // Not GetTempFileName: that one creates the file to reserve the name and gives up once
        // 65 535 stale .tmp files exist in the temp folder.
        var temp = Path.Combine(Path.GetTempPath(), $"alyce-{Guid.NewGuid():N}.zip");
        try
        {
            await using (var fs = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                await zipStream.CopyToAsync(fs, ct);

            List<LoadSource> sources;
            using (var archive = ZipFile.OpenRead(temp))
            {
                sources = archive.Entries
                    .Where(e => !string.IsNullOrEmpty(e.Name) &&
                                e.Name.EndsWith(".log", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(e => e.FullName, StringComparer.OrdinalIgnoreCase)
                    .Select(e => ZipSource(temp, e))
                    .ToList();
            }

            if (sources.Count == 0)
            {
                LoadError = "The ZIP contains no .log files.";
                RaiseDataset();
                return;
            }

            await RunLoadAsync(sources, displayName, includeDebug, parallel: true, append: false, ct);
        }
        catch (OperationCanceledException)
        {
            LoadError = "Load cancelled.";
            RaiseDataset();
        }
        catch (Exception ex)
        {
            LoadError = ex.Message;
            RaiseDataset();
        }
        finally
        {
            try { File.Delete(temp); } catch { /* best effort */ }
        }
    }

    /// <summary>
    /// Appends entries from uploaded .log files to the existing dataset. If nothing is loaded, this
    /// acts like a fresh load.
    /// <para>
    /// Takes stream factories rather than open streams: they are opened one at a time, just before
    /// being parsed, because a browser upload cannot serve several at once and an idle
    /// <c>IBrowserFile</c> stream times out while it waits its turn. Each one is disposed as soon
    /// as it has been read.
    /// </para>
    /// </summary>
    public Task EnqueueFromStreamsAsync(
        IReadOnlyList<(string Name, Func<Stream> Open)> files, bool includeDebug, CancellationToken ct)
    {
        var sources = files.Select(f => new LoadSource(f.Name, 0, f.Open)).ToList();
        var label = files.Count == 1 ? files[0].Name : $"{files.Count} files";
        return RunLoadAsync(sources, label, includeDebug, parallel: false, append: true, ct);
    }

    /// <summary>
    /// The one place a load actually happens: parse the sources, merge them into time order and
    /// publish a new dataset. Progress, cancellation and error reporting are shared by every entry
    /// point so they cannot drift apart.
    /// </summary>
    private async Task RunLoadAsync(
        List<LoadSource> sources, string label, bool includeDebug, bool parallel, bool append, CancellationToken ct)
    {
        if (Interlocked.CompareExchange(ref _loading, 1, 0) == 1) return;

        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _loadCts = cts;
        LoadError = null;
        _filesProcessed = 0;
        _filesTotal = sources.Count;
        var previous = _data;
        Interlocked.Exchange(ref _entriesParsed, append ? previous.Entries.Count : 0);
        Interlocked.Exchange(ref _lastProgressAt, 0);
        RaiseProgress(force: true);

        try
        {
            var token = cts.Token;
            var dataset = await Task.Run(() =>
            {
                var parsed = ParseSources(sources, includeDebug, parallel, append ? previous.Entries.Count : 0, token);
                token.ThrowIfCancellationRequested();

                // Both sides are already in time order, so appending is a merge rather than a
                // re-sort of everything loaded so far.
                var all = append && previous.Entries.Count > 0
                    ? MergeInTimeOrder(new[] { previous.Entries, parsed })
                    : parsed;

                var fileCount = append ? (previous.Stats?.FileCount ?? 0) + sources.Count : sources.Count;
                var path = append && !string.IsNullOrEmpty(previous.LoadedPath)
                    ? $"{previous.LoadedPath} + {label}"
                    : label;

                // Statistics and the facet indexes are independent passes over the merged list, so
                // they run side by side rather than one after the other.
                LogStats? stats = null;
                LogDataset? built = null;
                Parallel.Invoke(
                    () => stats = ComputeStats(all, fileCount),
                    () => built = new LogDataset(all, null, path));

                built!.AttachStats(stats!);
                return built;
            }, token);

            _data = dataset;
            Interlocked.Exchange(ref _entriesParsed, dataset.Entries.Count);
        }
        catch (OperationCanceledException)
        {
            LoadError = "Load cancelled.";
        }
        catch (Exception ex)
        {
            LoadError = ex.Message;
        }
        finally
        {
            _loadCts = null;
            cts.Dispose();
            Volatile.Write(ref _loading, 0);
            RaiseDataset();
        }
    }

    /// <summary>
    /// Parses every source and returns one list in time order. Files are independent, so they are
    /// parsed on all cores; each worker keeps its own <see cref="LogParser"/> because the intern
    /// pool inside one is not thread-safe.
    /// </summary>
    private List<LogEntry> ParseSources(
        List<LoadSource> sources, bool includeDebug, bool parallel, long alreadyCounted, CancellationToken ct)
    {
        var perSource = new IReadOnlyList<LogEntry>[sources.Count];
        var parsed = alreadyCounted;

        void ParseOne(int index, LogParser parser)
        {
            perSource[index] = ParseSource(sources[index], parser, includeDebug, ct);
            Interlocked.Increment(ref _filesProcessed);
            Interlocked.Exchange(ref _entriesParsed, Interlocked.Add(ref parsed, perSource[index].Count));
            RaiseProgress();
        }

        if (parallel && sources.Count > 1)
        {
            var options = new ParallelOptions
            {
                CancellationToken = ct,
                MaxDegreeOfParallelism = Math.Max(1, Math.Min(Environment.ProcessorCount, sources.Count)),
            };

            Parallel.For(0, sources.Count, options,
                () => new LogParser(),
                (index, _, parser) => { ParseOne(index, parser); return parser; },
                _ => { });
        }
        else
        {
            var parser = new LogParser();
            for (var i = 0; i < sources.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                ParseOne(i, parser);
            }
        }

        return MergeInTimeOrder(perSource);
    }

    private static IReadOnlyList<LogEntry> ParseSource(
        LoadSource source, LogParser parser, bool includeDebug, CancellationToken ct)
    {
        var list = new List<LogEntry>(EstimateCapacity(source.SizeHint));

        using var stream = source.Open();
        using var lines = new Utf8LineReader(stream);

        var sinceCheck = 0;
        while (lines.TryReadLine(out var line))
        {
            if (++sinceCheck >= 4096)
            {
                sinceCheck = 0;
                ct.ThrowIfCancellationRequested();
            }

            var entry = parser.TryParse(line, source.Name);
            if (entry is null) continue;
            if (!includeDebug && LogLevels.IsDebug(entry.Level)) continue;
            list.Add(entry);
        }

        return InTimeOrder(list);
    }

    private static int EstimateCapacity(long sizeHint) => sizeHint <= 0
        ? 1024
        : (int)Math.Clamp(sizeHint / EstimatedBytesPerEntry, 1024, MaxEstimatedCapacity);

    /// <summary>
    /// A log file is append-only, so it is nearly always already in time order — checking is O(n)
    /// and saves sorting it. A multi-threaded writer can still interleave two lines, hence the
    /// fallback, which is <see cref="Enumerable.OrderBy{T,TKey}(IEnumerable{T},Func{T,TKey})"/>
    /// rather than <c>List.Sort</c> because that one is unstable: lines sharing a timestamp — and
    /// the sample logs are full of them — came out in an arbitrary order that changed per load.
    /// </summary>
    private static IReadOnlyList<LogEntry> InTimeOrder(List<LogEntry> list)
    {
        for (var i = 1; i < list.Count; i++)
        {
            if (list[i].Time < list[i - 1].Time)
                return list.OrderBy(e => e.Time).ToList();
        }

        return list;
    }

    /// <summary>
    /// Merges already-sorted runs into one list. Ties break on the run's position, so two lines
    /// with the same timestamp always come out in file order — same input, same result, every time.
    /// </summary>
    private static List<LogEntry> MergeInTimeOrder(IReadOnlyList<IReadOnlyList<LogEntry>> runs)
    {
        var total = 0;
        var nonEmpty = 0;
        var last = -1;
        for (var i = 0; i < runs.Count; i++)
        {
            var run = runs[i];
            if (run is null || run.Count == 0) continue;
            total += run.Count;
            nonEmpty++;
            last = i;
        }

        if (nonEmpty == 0) return new List<LogEntry>();
        if (nonEmpty == 1) return runs[last] as List<LogEntry> ?? runs[last].ToList();

        var result = new List<LogEntry>(total);

        // Log files are usually named by date and cover successive periods, so the runs don't
        // overlap and "merging" is just appending them in order — worth checking, because the
        // heap below is otherwise the largest serial cost of a big load.
        if (AreDisjointAndOrdered(runs))
        {
            foreach (var run in runs)
            {
                if (run is null || run.Count == 0) continue;
                result.AddRange(run);
            }

            return result;
        }

        var queue = new PriorityQueue<(int Run, int Pos), (DateTime Time, int Run)>(nonEmpty);

        for (var i = 0; i < runs.Count; i++)
        {
            var run = runs[i];
            if (run is null || run.Count == 0) continue;
            queue.Enqueue((i, 0), (run[0].Time, i));
        }

        while (queue.TryDequeue(out var cur, out _))
        {
            var run = runs[cur.Run];
            result.Add(run[cur.Pos]);

            var next = cur.Pos + 1;
            if (next < run.Count) queue.Enqueue((cur.Run, next), (run[next].Time, cur.Run));
        }

        return result;
    }

    /// <summary>
    /// True when every non-empty run starts no earlier than the previous one ended, so
    /// concatenating them in order already yields a sorted, stable sequence.
    /// </summary>
    private static bool AreDisjointAndOrdered(IReadOnlyList<IReadOnlyList<LogEntry>> runs)
    {
        DateTime? previousEnd = null;
        foreach (var run in runs)
        {
            if (run is null || run.Count == 0) continue;
            if (previousEnd is { } end && run[0].Time < end) return false;
            previousEnd = run[^1].Time;
        }

        return true;
    }

    private static LoadSource FileSource(string path)
    {
        long size = 0;
        try { size = new FileInfo(path).Length; } catch { /* size is only a capacity hint */ }
        return new LoadSource(
            Path.GetFileName(path),
            size,
            () => new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
                                 bufferSize: 0, FileOptions.SequentialScan));
    }

    /// <summary>
    /// One <see cref="ZipArchive"/> per entry rather than a shared one: an archive cannot serve two
    /// entry streams at once, and re-reading the central directory is cheap next to parsing.
    /// </summary>
    private static LoadSource ZipSource(string archivePath, ZipArchiveEntry entry)
    {
        var fullName = entry.FullName;
        return new LoadSource(entry.Name, entry.Length, () =>
        {
            var archive = ZipFile.OpenRead(archivePath);
            var target = archive.GetEntry(fullName);
            if (target is null)
            {
                archive.Dispose();
                throw new InvalidOperationException($"ZIP entry disappeared: {fullName}");
            }

            return new ZipEntryStream(archive, target.Open());
        });
    }

    /// <summary>One thing to parse: a file on disk, an entry in a ZIP, or an uploaded stream.</summary>
    private sealed record LoadSource(string Name, long SizeHint, Func<Stream> Open);

    /// <summary>Keeps the owning archive alive for as long as the entry stream is being read.</summary>
    private sealed class ZipEntryStream(ZipArchive archive, Stream inner) : DelegatingStream(inner)
    {
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing) archive.Dispose();
        }
    }

    private abstract class DelegatingStream(Stream inner) : Stream
    {
        protected readonly Stream Inner = inner;

        public override bool CanRead => Inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => Inner.Length;
        public override long Position
        {
            get => Inner.Position;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => Inner.Read(buffer, offset, count);
        public override void Flush() => Inner.Flush();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing) Inner.Dispose();
            base.Dispose(disposing);
        }
    }

    // ---------------------------------------------------------------- querying

    /// <summary>Filtered entries (chronological), narrowed by the dataset's facet indexes first.</summary>
    public IEnumerable<LogEntry> Query(LogFilter filter)
    {
        var data = _data;
        return data.Candidates(filter).Where(filter.Matches);
    }

    public IReadOnlyList<string> DistinctLevels() =>
        _data.Entries.Select(e => e.Level).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();

    public IReadOnlyList<string> DistinctEnvironments() =>
        _data.Entries.Where(e => e.Environment is not null).Select(e => e.Environment!)
             .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();

    public IReadOnlyList<string> DistinctCompanies() =>
        _data.Entries.Where(e => e.Company is not null).Select(e => e.Company!)
             .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();

    public IReadOnlyList<string> DistinctLoggers() =>
        _data.Entries.Select(e => e.Logger).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();

    /// <summary>Clusters WARN/ERROR (or any requested levels) into signature groups, ordered by count.</summary>
    public IReadOnlyList<MessageGroup> Triage(IEnumerable<string> levels, string? environment)
    {
        var wanted = new HashSet<string>(levels, StringComparer.OrdinalIgnoreCase);
        var groups = new Dictionary<string, MessageGroup>();

        foreach (var e in _data.Entries)
        {
            if (!wanted.Contains(e.Level)) continue;
            if (!string.IsNullOrWhiteSpace(environment) &&
                !string.Equals(e.Environment, environment, StringComparison.OrdinalIgnoreCase))
                continue;

            // LogEntry.Signature is computed once and cached, so re-clustering on a filter change
            // no longer re-normalises every message.
            var sig = e.Level + "|" + e.Signature;
            if (!groups.TryGetValue(sig, out var g))
            {
                g = new MessageGroup
                {
                    Level = e.Level,
                    Signature = sig,
                    SampleMessage = e.ShortMessage,
                    FirstSeen = e.Time,
                    LastSeen = e.Time,
                    Sample = e,
                };
                groups[sig] = g;
            }

            g.Count++;
            if (e.Time < g.FirstSeen) g.FirstSeen = e.Time;
            if (e.Time > g.LastSeen) g.LastSeen = e.Time;
            if (e.Environment is not null) g.Environments.Add(e.Environment);
            if (!string.IsNullOrEmpty(e.Logger)) g.Loggers.Add(e.Logger);
            // Prefer a sample that carries a stack trace.
            if (e.HasStackTrace && g.Sample?.HasStackTrace != true) g.Sample = e;
        }

        return groups.Values
            .OrderByDescending(g => LogLevels.Series(g.Level) == LogLevels.Error)
            .ThenByDescending(g => g.Count)
            .ToList();
    }

    // ---------------------------------------------------------------- stats

    private static LogStats ComputeStats(IReadOnlyList<LogEntry> entries, int fileCount)
    {
        var byLevel = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var byEnv = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var byLogger = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var buckets = new Dictionary<DateTime, TimeBucket>();

        DateTime? first = null, last = null;

        foreach (var e in entries)
        {
            Bump(byLevel, e.Level);
            if (e.Environment is not null) Bump(byEnv, e.Environment);
            if (!string.IsNullOrEmpty(e.Logger)) Bump(byLogger, e.Logger);

            if (e.Time != DateTime.MinValue)
            {
                if (first is null || e.Time < first) first = e.Time;
                if (last is null || e.Time > last) last = e.Time;

                var key = new DateTime(e.Time.Year, e.Time.Month, e.Time.Day, e.Time.Hour, 0, 0);
                if (!buckets.TryGetValue(key, out var b))
                {
                    b = new TimeBucket { Start = key };
                    buckets[key] = b;
                }

                switch (LogLevels.Series(e.Level))
                {
                    case LogLevels.Debug: b.Debug++; break;
                    case LogLevels.Info: b.Info++; break;
                    case LogLevels.Warn: b.Warn++; break;
                    case LogLevels.Error: b.Error++; break;
                }
            }
        }

        return new LogStats
        {
            TotalEntries = entries.Count,
            FileCount = fileCount,
            FirstTime = first,
            LastTime = last,
            ByLevel = byLevel,
            ByEnvironment = byEnv,
            ByLogger = byLogger,
            Timeline = buckets.Values.OrderBy(b => b.Start).ToList(),
        };
    }

    private static void Bump(Dictionary<string, int> map, string key)
        => map[key] = map.TryGetValue(key, out var n) ? n + 1 : 1;
}
