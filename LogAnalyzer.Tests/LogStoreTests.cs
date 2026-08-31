using System.IO.Compression;
using System.Text;
using LogAnalyzer.Models;
using LogAnalyzer.Services;
using Xunit;

namespace LogAnalyzer.Tests;

public class LogStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"alyce-tests-{Guid.NewGuid():N}");

    public LogStoreTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    private static string Line(string time, string level, string message,
                              string? env = null, string? company = null, string logger = "A.B") =>
        $$"""{ "time": "{{time}}", "level": "{{level}}", "message": "{{message}}", "logger": "{{logger}}"{{
            (env is null ? "" : $", \"environment\": \"{env}\"")}}{{
            (company is null ? "" : $", \"company\": \"{company}\"")}} }""";

    private string WriteLog(string name, params string[] lines)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllLines(path, lines, new UTF8Encoding(false));
        return path;
    }

    [Fact]
    public async Task Loads_a_folder_and_computes_stats()
    {
        WriteLog("a.log",
            Line("2026-07-08 10:00:00.0000", "INFO", "one", env: "E1"),
            Line("2026-07-08 10:00:01.0000", "ERROR", "two", env: "E1"));
        WriteLog("b.log",
            Line("2026-07-08 11:00:00.0000", "WARN", "three", env: "E2"));

        var store = new LogStore();
        await store.LoadAsync(_dir, includeDebug: false, CancellationToken.None);

        Assert.Null(store.LoadError);
        Assert.True(store.IsLoaded);
        Assert.Equal(3, store.Stats!.TotalEntries);
        Assert.Equal(2, store.Stats.FileCount);
        Assert.Equal(2, store.Stats.ByEnvironment.Count);
        Assert.Equal(new DateTime(2026, 7, 8, 10, 0, 0), store.Stats.FirstTime);
        Assert.Equal(new DateTime(2026, 7, 8, 11, 0, 0), store.Stats.LastTime);
        Assert.Equal(2, store.Stats.Timeline.Count);           // two distinct hours
    }

    [Fact]
    public async Task Skips_debug_unless_asked_for()
    {
        WriteLog("a.log",
            Line("2026-07-08 10:00:00.0000", "DEBUG", "noisy"),
            Line("2026-07-08 10:00:01.0000", "INFO", "useful"));

        var store = new LogStore();
        await store.LoadAsync(_dir, includeDebug: false, CancellationToken.None);
        Assert.Equal(1, store.Stats!.TotalEntries);

        await store.LoadAsync(_dir, includeDebug: true, CancellationToken.None);
        Assert.Equal(2, store.Stats!.TotalEntries);
    }

    /// <summary>
    /// Entries sharing a timestamp used to come out in an arbitrary order — <c>List.Sort</c> is
    /// unstable — which changed from load to load. They must now always be in file order.
    /// </summary>
    [Fact]
    public async Task Entries_sharing_a_timestamp_keep_a_stable_deterministic_order()
    {
        const string sameTime = "2026-07-08 10:00:00.0000";
        WriteLog("a.log",
            Line(sameTime, "INFO", "a1"), Line(sameTime, "INFO", "a2"), Line(sameTime, "INFO", "a3"));
        WriteLog("b.log",
            Line(sameTime, "INFO", "b1"), Line(sameTime, "INFO", "b2"));

        string[]? first = null;
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var store = new LogStore();
            await store.LoadAsync(_dir, includeDebug: false, CancellationToken.None);

            var order = store.Entries.Select(e => e.Message).ToArray();
            first ??= order;
            Assert.Equal(first, order);
        }

        // a.log sorts before b.log, and each file keeps its internal order.
        Assert.Equal(new[] { "a1", "a2", "a3", "b1", "b2" }, first);
    }

    [Fact]
    public async Task Entries_are_in_time_order_across_files()
    {
        WriteLog("a.log",
            Line("2026-07-08 10:00:00.0000", "INFO", "first"),
            Line("2026-07-08 10:00:04.0000", "INFO", "third"));
        WriteLog("b.log",
            Line("2026-07-08 10:00:02.0000", "INFO", "second"),
            Line("2026-07-08 10:00:06.0000", "INFO", "fourth"));

        var store = new LogStore();
        await store.LoadAsync(_dir, includeDebug: false, CancellationToken.None);

        Assert.Equal(new[] { "first", "second", "third", "fourth" },
                     store.Entries.Select(e => e.Message));
    }

    /// <summary>A file whose own lines are out of order still ends up sorted.</summary>
    [Fact]
    public async Task An_out_of_order_file_is_sorted()
    {
        WriteLog("a.log",
            Line("2026-07-08 10:00:05.0000", "INFO", "late"),
            Line("2026-07-08 10:00:01.0000", "INFO", "early"));

        var store = new LogStore();
        await store.LoadAsync(_dir, includeDebug: false, CancellationToken.None);

        Assert.Equal(new[] { "early", "late" }, store.Entries.Select(e => e.Message));
    }

    [Fact]
    public async Task A_missing_folder_reports_an_error_rather_than_throwing()
    {
        var store = new LogStore();
        await store.LoadAsync(Path.Combine(_dir, "nope"), includeDebug: false, CancellationToken.None);

        Assert.False(store.IsLoaded);
        Assert.Contains("not found", store.LoadError!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task An_empty_folder_loads_to_an_empty_dataset()
    {
        var store = new LogStore();
        await store.LoadAsync(_dir, includeDebug: false, CancellationToken.None);

        Assert.Null(store.LoadError);
        Assert.True(store.IsLoaded);
        Assert.Equal(0, store.Stats!.TotalEntries);
    }

    [Fact]
    public async Task Malformed_lines_are_skipped_without_failing_the_load()
    {
        WriteLog("a.log",
            "this is not json",
            "",
            Line("2026-07-08 10:00:00.0000", "INFO", "good"),
            "{ truncated");

        var store = new LogStore();
        await store.LoadAsync(_dir, includeDebug: false, CancellationToken.None);

        Assert.Null(store.LoadError);
        Assert.Equal(1, store.Stats!.TotalEntries);
    }

    [Fact]
    public async Task Clear_resets_everything()
    {
        WriteLog("a.log", Line("2026-07-08 10:00:00.0000", "INFO", "one"));
        var store = new LogStore();
        await store.LoadAsync(_dir, includeDebug: false, CancellationToken.None);

        store.Clear();

        Assert.False(store.IsLoaded);
        Assert.Empty(store.Entries);
        Assert.Null(store.LoadedPath);
    }

    [Fact]
    public async Task Loading_publishes_a_new_dataset_instead_of_mutating_the_old_one()
    {
        WriteLog("a.log", Line("2026-07-08 10:00:00.0000", "INFO", "one"));
        var store = new LogStore();
        await store.LoadAsync(_dir, includeDebug: false, CancellationToken.None);

        // A reader that took the reference before the second load must keep seeing what it had.
        var beforeReload = store.Entries;
        var countBefore = beforeReload.Count;

        WriteLog("b.log", Line("2026-07-08 10:00:01.0000", "INFO", "two"));
        await store.LoadAsync(_dir, includeDebug: false, CancellationToken.None);

        Assert.Equal(countBefore, beforeReload.Count);
        Assert.NotSame(beforeReload, store.Entries);
        Assert.Equal(2, store.Entries.Count);
    }

    /// <summary>
    /// The regression behind the "collection was modified" crash: appending used to mutate and
    /// re-sort the list the UI was enumerating, from another thread.
    /// </summary>
    [Fact]
    public async Task Appending_while_a_query_is_being_enumerated_does_not_disturb_it()
    {
        for (var i = 0; i < 40; i++)
            WriteLog($"f{i:D2}.log", Line($"2026-07-08 10:00:{i:D2}.0000", "INFO", $"m{i}"));

        var store = new LogStore();
        await store.LoadAsync(_dir, includeDebug: false, CancellationToken.None);

        // Lazily enumerate a query, appending in the middle of it.
        var enumerated = 0;
        var filter = new LogFilter();
        var appended = false;

        foreach (var _ in store.Query(filter))
        {
            enumerated++;
            if (!appended && enumerated == 5)
            {
                appended = true;
                var extra = Encoding.UTF8.GetBytes(Line("2026-07-08 09:00:00.0000", "ERROR", "injected"));
                await store.EnqueueFromStreamsAsync(
                    new (string, Func<Stream>)[] { ("new.log", () => new MemoryStream(extra)) },
                    includeDebug: false, CancellationToken.None);
            }
        }

        Assert.Equal(40, enumerated);            // the in-flight query saw the old dataset, intact
        Assert.Equal(41, store.Entries.Count);   // and the append landed
        Assert.Equal("injected", store.Entries[0].Message);   // merged into time order
    }

    [Fact]
    public async Task Appending_merges_into_time_order_and_grows_the_file_count()
    {
        WriteLog("a.log",
            Line("2026-07-08 10:00:00.0000", "INFO", "one"),
            Line("2026-07-08 10:00:04.0000", "INFO", "three"));

        var store = new LogStore();
        await store.LoadAsync(_dir, includeDebug: false, CancellationToken.None);

        var extra = Encoding.UTF8.GetBytes(
            Line("2026-07-08 10:00:02.0000", "INFO", "two") + "\n" +
            Line("2026-07-08 10:00:06.0000", "INFO", "four"));

        await store.EnqueueFromStreamsAsync(
            new (string, Func<Stream>)[] { ("b.log", () => new MemoryStream(extra)) },
            includeDebug: false, CancellationToken.None);

        Assert.Equal(new[] { "one", "two", "three", "four" }, store.Entries.Select(e => e.Message));
        Assert.Equal(2, store.Stats!.FileCount);
        Assert.Contains("b.log", store.LoadedPath!);
    }

    /// <summary>
    /// A Blazor Server upload stream (<c>RemoteBrowserFileStream</c>) rejects synchronous reads, so
    /// the store has to get the bytes off it asynchronously before parsing them.
    /// </summary>
    [Fact]
    public async Task Enqueues_from_a_stream_that_only_supports_async_reads()
    {
        var bytes = Encoding.UTF8.GetBytes(
            Line("2026-07-08 10:00:00.0000", "INFO", "uploaded-one") + "\n" +
            Line("2026-07-08 10:00:01.0000", "ERROR", "uploaded-two") + "\n");

        var store = new LogStore();
        await store.EnqueueFromStreamsAsync(
            new (string, Func<Stream>)[] { ("upload.log", () => new AsyncOnlyStream(bytes)) },
            includeDebug: false, CancellationToken.None);

        Assert.Null(store.LoadError);
        Assert.Equal(new[] { "uploaded-one", "uploaded-two" }, store.Entries.Select(e => e.Message));
        Assert.Equal("upload.log", store.Entries[0].SourceFile);
        Assert.Equal("upload.log", store.LoadedPath);
    }

    [Fact]
    public async Task Enqueues_several_async_only_streams_at_once()
    {
        var store = new LogStore();
        var files = Enumerable.Range(0, 4).Select(i =>
        {
            var bytes = Encoding.UTF8.GetBytes(Line($"2026-07-08 10:00:0{i}.0000", "INFO", $"file{i}") + "\n");
            return ($"upload{i}.log", new Func<Stream>(() => new AsyncOnlyStream(bytes)));
        }).ToArray();

        await store.EnqueueFromStreamsAsync(files, includeDebug: false, CancellationToken.None);

        Assert.Null(store.LoadError);
        Assert.Equal(new[] { "file0", "file1", "file2", "file3" }, store.Entries.Select(e => e.Message));
        Assert.Equal(4, store.Stats!.FileCount);
    }

    /// <summary>Mimics a Blazor Server browser-file stream: async reads only, forward only.</summary>
    private sealed class AsyncOnlyStream(byte[] data) : Stream
    {
        private int _position;

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException("Synchronous reads are not supported.");

        public override int Read(Span<byte> buffer) =>
            throw new NotSupportedException("Synchronous reads are not supported.");

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            var n = Math.Min(buffer.Length, data.Length - _position);
            if (n <= 0) return ValueTask.FromResult(0);
            data.AsSpan(_position, n).CopyTo(buffer.Span);
            _position += n;
            return ValueTask.FromResult(n);
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) =>
            ReadAsync(buffer.AsMemory(offset, count), ct).AsTask();

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => _position; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    [Fact]
    public async Task Loads_a_zip()
    {
        var zipPath = Path.Combine(_dir, "logs.zip");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            foreach (var (name, time, message) in new[]
                     {
                         ("a.log", "2026-07-08 10:00:00.0000", "one"),
                         ("nested/b.log", "2026-07-08 10:00:01.0000", "two"),
                         ("readme.txt", "", "ignored"),
                     })
            {
                var entry = zip.CreateEntry(name);
                using var writer = new StreamWriter(entry.Open());
                writer.Write(time.Length == 0 ? "not a log" : Line(time, "INFO", message));
            }
        }

        var store = new LogStore();
        await using (var stream = File.OpenRead(zipPath))
            await store.LoadFromZipAsync(stream, "logs.zip", includeDebug: false, CancellationToken.None);

        Assert.Null(store.LoadError);
        Assert.Equal(new[] { "one", "two" }, store.Entries.Select(e => e.Message));
        Assert.Equal("logs.zip", store.LoadedPath);
    }

    [Fact]
    public async Task A_zip_with_no_logs_reports_an_error()
    {
        var zipPath = Path.Combine(_dir, "empty.zip");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            using var writer = new StreamWriter(zip.CreateEntry("readme.txt").Open());
            writer.Write("nothing here");
        }

        var store = new LogStore();
        await using (var stream = File.OpenRead(zipPath))
            await store.LoadFromZipAsync(stream, "empty.zip", includeDebug: false, CancellationToken.None);

        Assert.False(store.IsLoaded);
        Assert.Contains("no .log files", store.LoadError!);
    }

    [Fact]
    public async Task Loading_the_real_sample_logs_works_end_to_end()
    {
        var samples = SampleLogs.Files().ToList();
        if (samples.Count == 0) return;   // corpus not copied; nothing to assert

        foreach (var file in samples) File.Copy(file, Path.Combine(_dir, Path.GetFileName(file)));

        var store = new LogStore();
        await store.LoadAsync(_dir, includeDebug: true, CancellationToken.None);

        Assert.Null(store.LoadError);
        Assert.True(store.Stats!.TotalEntries > 1000, $"only {store.Stats.TotalEntries} entries parsed");
        Assert.True(store.Entries.Zip(store.Entries.Skip(1)).All(p => p.First.Time <= p.Second.Time),
                    "entries are not in time order");
        Assert.NotEmpty(store.DistinctLevels());
        Assert.NotEmpty(store.DistinctLoggers());
    }

    // ---------------------------------------------------------------- filtering

    [Fact]
    public async Task Query_applies_every_facet()
    {
        WriteLog("a.log",
            Line("2026-07-08 10:00:00.0000", "INFO", "alpha", env: "E1", company: "C1", logger: "A.B.C"),
            Line("2026-07-08 10:00:01.0000", "ERROR", "beta", env: "E1", company: "C2", logger: "A.B"),
            Line("2026-07-08 10:00:02.0000", "ERROR", "gamma", env: "E2", company: "C1", logger: "X.Y"));

        var store = new LogStore();
        await store.LoadAsync(_dir, includeDebug: false, CancellationToken.None);

        Assert.Equal(new[] { "beta", "gamma" }, Messages(store, f => f.Levels.Add("ERROR")));
        Assert.Equal(new[] { "alpha", "beta" }, Messages(store, f => f.Environments.Add("E1")));
        Assert.Equal(new[] { "alpha", "gamma" }, Messages(store, f => f.Companies.Add("C1")));
        Assert.Equal(new[] { "alpha", "beta" }, Messages(store, f => f.LoggerPrefix = "A.B"));
        Assert.Equal(new[] { "gamma" }, Messages(store, f => f.Text = "gam"));
        Assert.Equal(new[] { "beta" }, Messages(store, f =>
        {
            f.Levels.Add("ERROR");
            f.Environments.Add("E1");
        }));
        Assert.Empty(Messages(store, f => f.Levels.Add("FATAL")));
    }

    [Fact]
    public async Task Query_is_case_insensitive_on_the_facets()
    {
        WriteLog("a.log", Line("2026-07-08 10:00:00.0000", "Error", "boom", env: "Prod"));

        var store = new LogStore();
        await store.LoadAsync(_dir, includeDebug: false, CancellationToken.None);

        Assert.Equal(new[] { "boom" }, Messages(store, f => f.Levels.Add("ERROR")));
        Assert.Equal(new[] { "boom" }, Messages(store, f => f.Environments.Add("PROD")));
    }

    /// <summary>
    /// Above 50 000 entries the dataset builds facet indexes and queries take a different path;
    /// it has to give exactly the same answers as the linear scan.
    /// </summary>
    [Fact]
    public async Task The_indexed_path_agrees_with_the_scanning_path()
    {
        const int count = 60_000;
        var lines = new List<string>(count);
        for (var i = 0; i < count; i++)
        {
            var level = (i % 100) switch { 0 => "ERROR", 1 or 2 => "WARN", < 40 => "INFO", _ => "DEBUG" };
            lines.Add(Line(
                new DateTime(2026, 7, 8, 0, 0, 0).AddSeconds(i).ToString("yyyy-MM-dd HH:mm:ss.ffff"),
                level, $"m{i}", env: $"E{i % 7}", company: $"C{i % 3}"));
        }

        WriteLog("big.log", lines.ToArray());

        var store = new LogStore();
        await store.LoadAsync(_dir, includeDebug: true, CancellationToken.None);
        Assert.Equal(count, store.Entries.Count);

        void SameAsScan(Action<LogFilter> configure)
        {
            var filter = new LogFilter();
            configure(filter);

            var indexed = store.Query(filter).Select(e => e.Message).ToList();
            var scanned = store.Entries.Where(filter.Matches).Select(e => e.Message).ToList();

            Assert.Equal(scanned, indexed);
        }

        SameAsScan(f => f.Levels.Add("ERROR"));
        SameAsScan(f => { f.Levels.Add("ERROR"); f.Levels.Add("WARN"); });
        SameAsScan(f => f.Environments.Add("E3"));
        SameAsScan(f => { f.Environments.Add("E3"); f.Environments.Add("E5"); });
        SameAsScan(f => f.Companies.Add("C2"));
        SameAsScan(f => { f.Levels.Add("ERROR"); f.Environments.Add("E1"); f.Companies.Add("C0"); });
        SameAsScan(f => f.Environment = "E4");
        SameAsScan(f => f.Levels.Add("NOPE"));
        SameAsScan(f => { f.Levels.Add("ERROR"); f.Text = "m1"; });
        SameAsScan(_ => { });
    }

    [Fact]
    public async Task Triage_clusters_similar_messages()
    {
        WriteLog("a.log",
            Line("2026-07-08 10:00:00.0000", "ERROR", "Timeout after 100 ms", env: "E1"),
            Line("2026-07-08 10:00:01.0000", "ERROR", "Timeout after 250 ms", env: "E2"),
            Line("2026-07-08 10:00:02.0000", "ERROR", "Disk full", env: "E1"),
            Line("2026-07-08 10:00:03.0000", "WARN", "Slow query 12 s", env: "E1"),
            Line("2026-07-08 10:00:04.0000", "INFO", "Ignored", env: "E1"));

        var store = new LogStore();
        await store.LoadAsync(_dir, includeDebug: false, CancellationToken.None);

        var groups = store.Triage(new[] { "ERROR", "WARN" }, environment: null);

        Assert.Equal(3, groups.Count);
        var timeout = groups.Single(g => g.SampleMessage.StartsWith("Timeout"));
        Assert.Equal(2, timeout.Count);
        Assert.Equal(2, timeout.Environments.Count);
        Assert.Equal(new DateTime(2026, 7, 8, 10, 0, 0), timeout.FirstSeen);
        Assert.Equal(new DateTime(2026, 7, 8, 10, 0, 1), timeout.LastSeen);

        // Errors sort ahead of warnings.
        Assert.Equal("ERROR", groups[0].Level);

        var scoped = store.Triage(new[] { "ERROR", "WARN" }, environment: "E2");
        Assert.Single(scoped);
    }

    [Fact]
    public async Task A_load_can_be_cancelled()
    {
        // Enough files that cancellation lands mid-load rather than after it.
        for (var i = 0; i < 200; i++)
        {
            var lines = Enumerable.Range(0, 500)
                .Select(n => Line($"2026-07-08 10:00:{n % 60:D2}.0000", "INFO", $"m{i}-{n}"))
                .ToArray();
            WriteLog($"f{i:D3}.log", lines);
        }

        var store = new LogStore();
        using var cts = new CancellationTokenSource();
        var load = store.LoadAsync(_dir, includeDebug: true, cts.Token);
        cts.Cancel();
        await load;

        Assert.False(store.IsLoaded);
        Assert.Equal("Load cancelled.", store.LoadError);
    }

    [Fact]
    public async Task Cancelling_leaves_the_dataset_already_loaded_in_place()
    {
        WriteLog("a.log", Line("2026-07-08 10:00:00.0000", "INFO", "keep me"));
        var store = new LogStore();
        await store.LoadAsync(_dir, includeDebug: false, CancellationToken.None);

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await store.LoadAsync(_dir, includeDebug: false, cts.Token);

        Assert.Equal("Load cancelled.", store.LoadError);
        Assert.Equal(new[] { "keep me" }, store.Entries.Select(e => e.Message));
    }

    [Fact]
    public async Task Progress_events_are_throttled_but_the_dataset_event_is_not()
    {
        for (var i = 0; i < 60; i++)
            WriteLog($"f{i:D2}.log", Line("2026-07-08 10:00:00.0000", "INFO", $"m{i}"));

        var store = new LogStore();
        var progress = 0;
        var dataset = 0;
        store.ProgressChanged += () => Interlocked.Increment(ref progress);
        store.DatasetChanged += () => Interlocked.Increment(ref dataset);

        await store.LoadAsync(_dir, includeDebug: false, CancellationToken.None);

        // 60 files used to mean 60 notifications, each triggering a full requery on every page.
        Assert.Equal(1, dataset);
        Assert.InRange(progress, 1, 10);
    }

    private static List<string> Messages(LogStore store, Action<LogFilter> configure)
    {
        var filter = new LogFilter();
        configure(filter);
        return store.Query(filter).Select(e => e.Message).ToList();
    }
}
