using System.Text;
using LogAnalyzer.Models;
using LogAnalyzer.Services;
using Xunit;

namespace LogAnalyzer.Tests;

public class LogWatcherTests : IAsyncDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"alyce-watch-{Guid.NewGuid():N}");
    private readonly LogWatcher _watcher = new() { PollInterval = TimeSpan.FromMilliseconds(30) };
    private readonly List<LogEntry> _seen = new();
    private readonly object _gate = new();

    public LogWatcherTests()
    {
        Directory.CreateDirectory(_dir);
        _watcher.EntriesAppended += entries =>
        {
            lock (_gate) _seen.AddRange(entries);
        };
    }

    public async ValueTask DisposeAsync()
    {
        await _watcher.DisposeAsync();
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    private static string Line(string message, string level = "INFO") =>
        $$"""{ "time": "2026-07-08 10:00:00.0000", "level": "{{level}}", "message": "{{message}}", "logger": "A.B" }""";

    private string NewFile(string name, params string[] lines)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, lines.Length == 0 ? "" : string.Join("\n", lines) + "\n", new UTF8Encoding(false));
        return path;
    }

    private static void Append(string path, params string[] lines)
    {
        using var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        using var writer = new StreamWriter(fs, new UTF8Encoding(false));
        foreach (var line in lines) writer.Write(line + "\n");
    }

    private List<string> Messages()
    {
        lock (_gate) return _seen.Select(e => e.Message).ToList();
    }

    /// <summary>Polls until <paramref name="predicate"/> holds, so the tests don't race the tailer.</summary>
    private async Task<bool> WaitFor(Func<bool> predicate, int timeoutMs = 5000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            if (predicate()) return true;
            await Task.Delay(20);
        }

        return predicate();
    }

    [Fact]
    public async Task Reads_the_whole_file_when_started_from_the_start()
    {
        var path = NewFile("all.log", Line("one"), Line("two"), Line("three"));

        await _watcher.StartAsync(path, fromStart: true);

        Assert.True(await WaitFor(() => Messages().Count == 3), $"got {Messages().Count}");
        Assert.Equal(new[] { "one", "two", "three" }, Messages());
        Assert.Equal(3, _watcher.TotalEntries);
    }

    [Fact]
    public async Task Skips_existing_content_when_not_started_from_the_start()
    {
        var path = NewFile("all.log", Line("old"));

        await _watcher.StartAsync(path, fromStart: false);
        Append(path, Line("new"));

        Assert.True(await WaitFor(() => Messages().Count == 1), $"got {Messages().Count}");
        Assert.Equal(new[] { "new" }, Messages());
    }

    [Fact]
    public async Task Picks_up_appended_lines()
    {
        var path = NewFile("all.log");
        await _watcher.StartAsync(path, fromStart: true);

        Append(path, Line("a"));
        Assert.True(await WaitFor(() => Messages().Count == 1));

        Append(path, Line("b"), Line("c"));
        Assert.True(await WaitFor(() => Messages().Count == 3), $"got {Messages().Count}");
        Assert.Equal(new[] { "a", "b", "c" }, Messages());
    }

    /// <summary>A line written in two pieces must not be parsed until it is complete.</summary>
    [Fact]
    public async Task A_partial_line_is_held_until_its_newline_arrives()
    {
        var path = NewFile("all.log");
        await _watcher.StartAsync(path, fromStart: true);

        var line = Line("split");
        var half = line.Length / 2;

        using (var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
        using (var writer = new StreamWriter(fs, new UTF8Encoding(false)) { AutoFlush = true })
        {
            writer.Write(line[..half]);
            await Task.Delay(150);
            Assert.Empty(Messages());

            writer.Write(line[half..]);
            writer.Write("\n");
        }

        Assert.True(await WaitFor(() => Messages().Count == 1), $"got {Messages().Count}");
        Assert.Equal(new[] { "split" }, Messages());
    }

    [Fact]
    public async Task Truncation_is_treated_as_a_restart()
    {
        var path = NewFile("all.log", Line("before"));
        await _watcher.StartAsync(path, fromStart: true);
        Assert.True(await WaitFor(() => Messages().Count == 1));

        // Same file, emptied and rewritten shorter.
        File.WriteAllText(path, Line("after") + "\n", new UTF8Encoding(false));

        Assert.True(await WaitFor(() => Messages().Contains("after")),
                    $"never saw the rewritten content, got [{string.Join(", ", Messages())}]");
    }

    /// <summary>
    /// The rotation that used to silence the tail for good: the file is renamed away and a new one
    /// takes its place, so the length never drops below the read position.
    /// </summary>
    [Fact]
    public async Task A_rotated_file_replaced_by_a_longer_one_is_still_followed()
    {
        var path = NewFile("all.log", Line("first"));
        await _watcher.StartAsync(path, fromStart: true);
        Assert.True(await WaitFor(() => Messages().Count == 1));

        File.Move(path, Path.Combine(_dir, "all.log.1"));

        // Deliberately longer than what we had already read, which is what defeated the old
        // shrink-only check. Recreated immediately, so NTFS file tunneling gives the new file the
        // old one's creation timestamp too — the head fingerprint is the only thing left to notice.
        File.WriteAllText(path, Line("rotated-one") + "\n" + Line("rotated-two") + "\n" + Line("rotated-three") + "\n",
                          new UTF8Encoding(false));

        Assert.True(await WaitFor(() => Messages().Contains("rotated-one")),
                    $"rotation was not noticed, got [{string.Join(", ", Messages())}]");
    }

    /// <summary>
    /// Catching up used to consume one 4 MB chunk per poll tick. With several chunks' worth of
    /// backlog and a slow poll interval, finishing quickly proves chunks now follow each other
    /// within a single poll.
    /// </summary>
    [Fact]
    public async Task Catch_up_does_not_wait_a_poll_per_chunk()
    {
        var path = Path.Combine(_dir, "big.log");
        var line = Line(new string('p', 900));

        // ~12 MB: more than the 4 MB per-chunk cap, so it takes several chunks.
        using (var writer = new StreamWriter(path, append: false, new UTF8Encoding(false)))
        {
            var lines = 12 * 1024 * 1024 / (line.Length + 1);
            for (var i = 0; i < lines; i++) writer.Write(line + "\n");
        }

        var expected = File.ReadLines(path).Count();
        var watcher = new LogWatcher { PollInterval = TimeSpan.FromSeconds(5) };
        var count = 0;
        watcher.EntriesAppended += entries => Interlocked.Add(ref count, entries.Count);

        await using (watcher)
        {
            await watcher.StartAsync(path, fromStart: true);

            // One poll's worth of time. At one chunk per poll this could not finish.
            var done = await WaitFor(() => Volatile.Read(ref count) >= expected, timeoutMs: 4000);
            Assert.True(done, $"only read {Volatile.Read(ref count)} of {expected} entries in one poll");
        }
    }

    [Fact]
    public async Task Starting_on_a_missing_file_reports_an_error_instead_of_throwing()
    {
        await _watcher.StartAsync(Path.Combine(_dir, "nope.log"), fromStart: true);

        Assert.False(_watcher.IsWatching);
        Assert.Contains("not found", _watcher.LastError!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Stop_waits_for_the_poll_loop_and_no_entries_arrive_afterwards()
    {
        var path = NewFile("all.log", Line("one"));
        await _watcher.StartAsync(path, fromStart: true);
        Assert.True(await WaitFor(() => Messages().Count == 1));

        await _watcher.StopAsync();
        Assert.False(_watcher.IsWatching);

        var countAtStop = Messages().Count;
        Append(path, Line("after-stop"));
        await Task.Delay(200);

        Assert.Equal(countAtStop, Messages().Count);
    }

    /// <summary>
    /// Restarting used to leave the previous poll loop running against shared position and
    /// pending-line state, so the two could corrupt each other's reads.
    /// </summary>
    [Fact]
    public async Task Restarting_on_another_file_does_not_mix_the_two()
    {
        var first = NewFile("first.log", Line("from-first"));
        var second = NewFile("second.log", Line("from-second"));

        await _watcher.StartAsync(first, fromStart: true);
        Assert.True(await WaitFor(() => Messages().Count == 1));

        lock (_gate) _seen.Clear();
        await _watcher.StartAsync(second, fromStart: true);

        Assert.True(await WaitFor(() => Messages().Count == 1), $"got {Messages().Count}");
        Assert.Equal(new[] { "from-second" }, Messages());
        Assert.Equal(1, _watcher.TotalEntries);   // reset per watch
        Assert.All(_seen, e => Assert.Equal("second.log", e.SourceFile));
    }

    [Fact]
    public async Task Rapid_restarts_do_not_corrupt_the_read_state()
    {
        var path = NewFile("all.log", Line("one"), Line("two"), Line("three"));

        for (var i = 0; i < 10; i++)
        {
            lock (_gate) _seen.Clear();
            await _watcher.StartAsync(path, fromStart: true);
        }

        Assert.True(await WaitFor(() => Messages().Count == 3), $"got {Messages().Count}");
        Assert.Equal(new[] { "one", "two", "three" }, Messages());
    }

    [Fact]
    public async Task A_byte_order_mark_is_not_fed_to_the_parser()
    {
        var path = Path.Combine(_dir, "bom.log");
        File.WriteAllText(path, Line("bom-first") + "\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        await _watcher.StartAsync(path, fromStart: true);

        Assert.True(await WaitFor(() => Messages().Count == 1), "the BOM line was rejected");
        Assert.Equal(new[] { "bom-first" }, Messages());
    }

    [Fact]
    public async Task Multi_byte_characters_split_across_reads_are_decoded_correctly()
    {
        var path = NewFile("utf8.log");
        await _watcher.StartAsync(path, fromStart: true);

        var accented = string.Concat(Enumerable.Repeat("àèìòù🚀", 500));
        Append(path, Line(accented));

        Assert.True(await WaitFor(() => Messages().Count == 1), $"got {Messages().Count}");
        Assert.Equal(accented, Messages()[0]);
    }
}
