using System.IO.Compression;
using LogAnalyzer.Models;

namespace LogAnalyzer.Services;

/// <summary>
/// In-memory store of parsed log entries for a loaded folder. Singleton: one dataset
/// shared across the app. Loading is done once (with progress) and then queried cheaply.
/// </summary>
public sealed class LogStore
{
    private readonly object _gate = new();
    private List<LogEntry> _entries = new();

    public IReadOnlyList<LogEntry> Entries => _entries;
    public LogStats? Stats { get; private set; }
    public string? LoadedPath { get; private set; }
    public bool IsLoaded => Stats is not null;

    // ---- progress state, observed by the UI while a load runs ----
    public bool IsLoading { get; private set; }
    public int FilesProcessed { get; private set; }
    public int FilesTotal { get; private set; }
    public long EntriesParsed { get; private set; }
    public string? LoadError { get; private set; }

    public event Action? Changed;
    private void Raise() => Changed?.Invoke();

    /// <summary>Loads every *.log file in <paramref name="folder"/>. Optionally skips levels below a minimum.</summary>
    public async Task LoadAsync(string folder, bool includeDebug, IProgress<int>? progress, CancellationToken ct)
    {
        if (IsLoading) return;
        IsLoading = true;
        LoadError = null;
        FilesProcessed = 0;
        EntriesParsed = 0;
        Raise();

        try
        {
            if (!Directory.Exists(folder))
                throw new DirectoryNotFoundException($"Folder not found: {folder}");

            var files = Directory.GetFiles(folder, "*.log").OrderBy(f => f).ToArray();
            FilesTotal = files.Length;
            Raise();

            var result = await Task.Run(() =>
            {
                var parser = new LogParser();
                var list = new List<LogEntry>(capacity: 1_000_000);

                foreach (var file in files)
                {
                    ct.ThrowIfCancellationRequested();
                    var name = Path.GetFileName(file);
                    foreach (var line in File.ReadLines(file))
                    {
                        var entry = parser.TryParse(line, name);
                        if (entry is null) continue;
                        if (!includeDebug && entry.Level.Equals("DEBUG", StringComparison.OrdinalIgnoreCase))
                            continue;
                        list.Add(entry);
                    }

                    FilesProcessed++;
                    EntriesParsed = list.Count;
                    progress?.Report(FilesProcessed);
                    Raise();
                }

                list.Sort((a, b) => a.Time.CompareTo(b.Time));
                return list;
            }, ct);

            var stats = ComputeStats(result, FilesTotal);

            lock (_gate)
            {
                _entries = result;
                Stats = stats;
                LoadedPath = folder;
            }
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
            IsLoading = false;
            Raise();
        }
    }

    /// <summary>
    /// Loads every *.log entry inside an uploaded ZIP. The stream is copied to a temp file
    /// first (ZipArchive needs a seekable source), then each entry is parsed like a folder load.
    /// </summary>
    public async Task LoadFromZipAsync(Stream zipStream, string displayName, bool includeDebug, CancellationToken ct)
    {
        if (IsLoading) return;
        IsLoading = true;
        LoadError = null;
        FilesProcessed = 0;
        FilesTotal = 0;
        EntriesParsed = 0;
        Raise();

        string? temp = null;
        try
        {
            temp = Path.GetTempFileName();
            await using (var fs = File.Create(temp))
                await zipStream.CopyToAsync(fs, ct);

            var tempPath = temp;
            var result = await Task.Run(() =>
            {
                var parser = new LogParser();
                var list = new List<LogEntry>(capacity: 1_000_000);

                using var archive = ZipFile.OpenRead(tempPath);
                var logs = archive.Entries
                    .Where(e => !string.IsNullOrEmpty(e.Name) &&
                                e.Name.EndsWith(".log", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(e => e.FullName, StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                if (logs.Length == 0)
                    throw new InvalidOperationException("The ZIP contains no .log files.");

                FilesTotal = logs.Length;
                Raise();

                foreach (var ze in logs)
                {
                    ct.ThrowIfCancellationRequested();
                    using var stream = ze.Open();
                    using var reader = new StreamReader(stream);
                    string? line;
                    while ((line = reader.ReadLine()) is not null)
                    {
                        var entry = parser.TryParse(line, ze.Name);
                        if (entry is null) continue;
                        if (!includeDebug && entry.Level.Equals("DEBUG", StringComparison.OrdinalIgnoreCase))
                            continue;
                        list.Add(entry);
                    }

                    FilesProcessed++;
                    EntriesParsed = list.Count;
                    Raise();
                }

                list.Sort((a, b) => a.Time.CompareTo(b.Time));
                return list;
            }, ct);

            var stats = ComputeStats(result, FilesTotal);

            lock (_gate)
            {
                _entries = result;
                Stats = stats;
                LoadedPath = displayName;
            }
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
            if (temp is not null)
            {
                try { File.Delete(temp); } catch { /* best effort */ }
            }
            IsLoading = false;
            Raise();
        }
    }

    // ---------------------------------------------------------------- querying

    /// <summary>Filtered entries (chronological). Cheap linear scan over the in-memory list.</summary>
    public IEnumerable<LogEntry> Query(LogFilter filter) => _entries.Where(filter.Matches);

    public (IReadOnlyList<LogEntry> Page, int Total) QueryPage(LogFilter filter, int skip, int take, bool newestFirst)
    {
        var matched = _entries.Where(filter.Matches);
        if (newestFirst) matched = matched.Reverse();
        var all = matched.ToList();
        var page = all.Skip(skip).Take(take).ToList();
        return (page, all.Count);
    }

    public IReadOnlyList<string> DistinctLevels() =>
        _entries.Select(e => e.Level).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();

    public IReadOnlyList<string> DistinctEnvironments() =>
        _entries.Where(e => e.Environment is not null).Select(e => e.Environment!)
                .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();

    public IReadOnlyList<string> DistinctCompanies() =>
        _entries.Where(e => e.Company is not null).Select(e => e.Company!)
                .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();

    public IReadOnlyList<string> DistinctLoggers() =>
        _entries.Select(e => e.Logger).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();

    /// <summary>Clusters WARN/ERROR (or any requested levels) into signature groups, ordered by count.</summary>
    public IReadOnlyList<MessageGroup> Triage(IEnumerable<string> levels, string? environment)
    {
        var wanted = new HashSet<string>(levels, StringComparer.OrdinalIgnoreCase);
        var groups = new Dictionary<string, MessageGroup>();

        foreach (var e in _entries)
        {
            if (!wanted.Contains(e.Level)) continue;
            if (!string.IsNullOrWhiteSpace(environment) &&
                !string.Equals(e.Environment, environment, StringComparison.OrdinalIgnoreCase))
                continue;

            var sig = e.Level + "|" + MessageNormalizer.Signature(e.Message);
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
            .OrderByDescending(g => g.Level.Equals("ERROR", StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(g => g.Count)
            .ToList();
    }

    // ---------------------------------------------------------------- stats

    private static LogStats ComputeStats(List<LogEntry> entries, int fileCount)
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
                switch (e.Level.ToUpperInvariant())
                {
                    case "DEBUG": b.Debug++; break;
                    case "INFO": b.Info++; break;
                    case "WARN": case "WARNING": b.Warn++; break;
                    case "ERROR": case "FATAL": b.Error++; break;
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
