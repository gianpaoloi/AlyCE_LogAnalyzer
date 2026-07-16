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
    public bool IncludeDebug { get; set; }

    // ---- progress state, observed by the UI while a load runs ----
    public bool IsLoading { get; private set; }
    public int FilesProcessed { get; private set; }
    public int FilesTotal { get; private set; }
    public long EntriesParsed { get; private set; }
    public string? LoadError { get; private set; }

    public event Action? Changed;
    private void Raise() => Changed?.Invoke();

    /// <summary>Clears the currently loaded dataset and resets progress/error state.</summary>
    public void Clear()
    {
        if (IsLoading) return;

        lock (_gate)
        {
            _entries = new List<LogEntry>();
            Stats = null;
            LoadedPath = null;
        }

        LoadError = null;
        FilesProcessed = 0;
        FilesTotal = 0;
        EntriesParsed = 0;
        Raise();
    }

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
            Raise();
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
            foreach (var file in logFiles)
            {
                await using var stream = File.OpenRead(file);
                await EnqueueFromFileAsync(stream, Path.GetFileName(file), includeDebug, ct);
            }
            return;
        }

        LoadError = "Only .zip and .log files are supported.";
        Raise();
    }

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

    /// <summary>
    /// Enqueues (appends) entries from a single uploaded .log file to the existing dataset.
    /// If no data is loaded, this acts like a fresh load.
    /// </summary>
    public async Task EnqueueFromFileAsync(Stream fileStream, string fileName, bool includeDebug, CancellationToken ct)
    {
        if (IsLoading) return;
        IsLoading = true;
        LoadError = null;
        var startingCount = _entries.Count;
        var startingFileCount = Stats?.FileCount ?? 0;
        EntriesParsed = startingCount;
        FilesProcessed = 0;
        FilesTotal = 1;
        Raise();

        try
        {
            var result = await Task.Run(async () =>
            {
                var parser = new LogParser();
                var newEntries = new List<LogEntry>();

                using var reader = new StreamReader(fileStream);
                string? line;
                while ((line = await reader.ReadLineAsync(ct)) is not null)
                {
                    ct.ThrowIfCancellationRequested();
                    var entry = parser.TryParse(line, fileName);
                    if (entry is null) continue;
                    if (!includeDebug && entry.Level.Equals("DEBUG", StringComparison.OrdinalIgnoreCase))
                        continue;
                    newEntries.Add(entry);
                }

                return newEntries;
            }, ct);

            lock (_gate)
            {
                _entries.AddRange(result);
                _entries.Sort((a, b) => a.Time.CompareTo(b.Time));
                Stats = ComputeStats(_entries, startingFileCount + 1);
                if (string.IsNullOrEmpty(LoadedPath))
                    LoadedPath = fileName;
                else
                    LoadedPath = $"{LoadedPath} + {fileName}";
            }

            FilesProcessed = 1;
            EntriesParsed = _entries.Count;
        }
        catch (OperationCanceledException)
        {
            LoadError = "Enqueue cancelled.";
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
