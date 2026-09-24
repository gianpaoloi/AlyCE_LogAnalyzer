using LogAnalyzer.Models;
using LogAnalyzer.Services;
using Xunit;

namespace LogAnalyzer.Tests;

/// <summary>
/// The buffer behind both live pages. Testable on its own now that it no longer lives inside the
/// page — which matters most for the four caps that keep a long watch from freezing the app.
/// </summary>
public class LiveEntryBufferTests
{
    private readonly LiveEntryBuffer _buffer = new();

    private static readonly LiveDisplayFilter NoFilter =
        new(Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), null, null);

    private static LogEntry Entry(
        string message, string level = "INFO", string? environment = null,
        string? company = null, string logger = "A.B") =>
        new()
        {
            Message = message,
            Level = level,
            Environment = environment,
            Company = company,
            Logger = logger,
        };

    private List<string> Messages()
    {
        _buffer.Rebuild(NoFilter);
        return _buffer.Snapshot.Select(e => e.Message).ToList();
    }

    [Fact]
    public void The_newest_entries_come_first()
    {
        _buffer.Ingest(new[] { Entry("one"), Entry("two"), Entry("three") });

        Assert.Equal(new[] { "three", "two", "one" }, Messages());
        Assert.Equal(3, _buffer.BufferedCount);
    }

    /// <summary>
    /// The ring is what keeps a long watch flat. Only the newest MaxKept can survive it, and a
    /// batch bigger than the ring must not be pushed entry by entry just to evict itself.
    /// </summary>
    [Fact]
    public void Only_the_newest_MaxKept_entries_are_kept()
    {
        var batch = Enumerable.Range(1, LiveEntryBuffer.MaxKept + 500).Select(i => Entry($"m{i}")).ToArray();

        _buffer.Ingest(batch);

        Assert.Equal(LiveEntryBuffer.MaxKept, _buffer.BufferedCount);
        var messages = Messages();
        Assert.Equal($"m{batch.Length}", messages[0]);
        Assert.Equal($"m{batch.Length - LiveEntryBuffer.MaxKept + 1}", messages[^1]);
    }

    [Fact]
    public void Entries_arriving_in_several_batches_evict_the_oldest()
    {
        var buffer = new LiveEntryBuffer();
        for (var i = 1; i <= LiveEntryBuffer.MaxKept + 10; i++) buffer.Ingest(new[] { Entry($"m{i}") });

        buffer.Rebuild(NoFilter);

        Assert.Equal(LiveEntryBuffer.MaxKept, buffer.Snapshot.Count);
        Assert.Equal($"m{LiveEntryBuffer.MaxKept + 10}", buffer.Snapshot[0].Message);
        Assert.Equal("m11", buffer.Snapshot[^1].Message);
    }

    /// <summary>The text filter is the only one that gates ingest, so the buffer holds nothing else.</summary>
    [Fact]
    public void The_text_filter_keeps_non_matching_entries_out_of_the_buffer()
    {
        _buffer.TextFilter = "timeout";

        _buffer.Ingest(new[] { Entry("a timeout occurred"), Entry("all good"), Entry("TIMEOUT again") });

        Assert.Equal(2, _buffer.BufferedCount);
        Assert.Equal(new[] { "TIMEOUT again", "a timeout occurred" }, Messages());
    }

    /// <summary>
    /// Narrowing the box has to drop what was buffered under the previous value, or the rows
    /// already on screen stay there.
    /// </summary>
    [Fact]
    public void The_text_filter_also_applies_to_already_buffered_entries()
    {
        _buffer.Ingest(new[] { Entry("a timeout occurred"), Entry("all good") });

        _buffer.Rebuild(NoFilter with { Text = "good" });

        Assert.Equal(new[] { "all good" }, _buffer.Snapshot.Select(e => e.Message));
    }

    [Fact]
    public void The_column_filters_are_applied_at_display_time()
    {
        _buffer.Ingest(new[]
        {
            Entry("boom", "ERROR", environment: "PROD", company: "1", logger: "A.B.C"),
            Entry("meh", "INFO", environment: "PROD", company: "2", logger: "A.B.C"),
            Entry("other", "ERROR", environment: "TEST", company: "1", logger: "X.Y"),
        });

        _buffer.Rebuild(NoFilter with { Levels = new[] { "ERROR" } });
        Assert.Equal(new[] { "other", "boom" }, _buffer.Snapshot.Select(e => e.Message));

        _buffer.Rebuild(NoFilter with { Environments = new[] { "PROD" } });
        Assert.Equal(new[] { "meh", "boom" }, _buffer.Snapshot.Select(e => e.Message));

        _buffer.Rebuild(NoFilter with { Companies = new[] { "1" } });
        Assert.Equal(new[] { "other", "boom" }, _buffer.Snapshot.Select(e => e.Message));

        // The logger tree filters by prefix, so a parent node picks up everything under it.
        _buffer.Rebuild(NoFilter with { LoggerPrefix = "A.B" });
        Assert.Equal(new[] { "meh", "boom" }, _buffer.Snapshot.Select(e => e.Message));
    }

    /// <summary>A cleared dropdown hands back null, which used to take the circuit down.</summary>
    [Fact]
    public void A_null_filter_selection_means_no_filter()
    {
        _buffer.Ingest(new[] { Entry("one") });

        _buffer.Rebuild(new LiveDisplayFilter(null!, null!, null!, null, null));

        Assert.Single(_buffer.Snapshot);
    }

    [Fact]
    public void Freezing_holds_the_view_still_and_counts_what_arrives()
    {
        _buffer.Ingest(new[] { Entry("before") });
        _buffer.Freeze(true);
        _buffer.Rebuild(NoFilter);

        _buffer.Ingest(new[] { Entry("during one"), Entry("during two") });
        _buffer.Rebuild(NoFilter);

        Assert.True(_buffer.IsFrozen);
        Assert.Equal(new[] { "before" }, _buffer.Snapshot.Select(e => e.Message));
        Assert.Equal(2, _buffer.PendingCount);
        // The producer kept buffering behind the frozen view.
        Assert.Equal(3, _buffer.BufferedCount);

        _buffer.Freeze(false);
        _buffer.Rebuild(NoFilter);

        Assert.False(_buffer.IsFrozen);
        Assert.Equal(0, _buffer.PendingCount);
        Assert.Equal(new[] { "during two", "during one", "before" }, _buffer.Snapshot.Select(e => e.Message));
    }

    [Fact]
    public void Clearing_while_frozen_empties_the_frozen_view_too()
    {
        _buffer.Ingest(new[] { Entry("one") });
        _buffer.Freeze(true);

        _buffer.Clear();
        _buffer.Rebuild(NoFilter);

        Assert.Empty(_buffer.Snapshot);
        Assert.Equal(0, _buffer.BufferedCount);
        Assert.True(_buffer.IsFrozen);
    }

    /// <summary>
    /// The level list starts with the four known ones and grows as others turn up — a FATAL line
    /// used to be displayed but impossible to filter for.
    /// </summary>
    [Fact]
    public void Unknown_levels_are_added_to_the_level_filter_as_they_are_seen()
    {
        Assert.Equal(new[] { "ERROR", "WARN", "INFO", "DEBUG" }, _buffer.Levels);

        _buffer.Ingest(new[] { Entry("x", "FATAL"), Entry("y", "TRACE") });

        // Severity order first, then anything unrecognised alphabetically.
        Assert.Equal(new[] { "ERROR", "FATAL", "WARN", "INFO", "DEBUG", "TRACE" }, _buffer.Levels);
    }

    [Fact]
    public void Environments_companies_and_loggers_are_collected_for_the_filters()
    {
        _buffer.Ingest(new[]
        {
            Entry("a", environment: "PROD", company: "1", logger: "A.B"),
            Entry("b", environment: "TEST", company: "2", logger: "A.B"),
            Entry("c", environment: "PROD", company: "1", logger: "X.Y"),
        });

        Assert.Equal(new[] { "PROD", "TEST" }, _buffer.Environments);
        Assert.Equal(new[] { "1", "2" }, _buffer.Companies);
        Assert.Equal(2, _buffer.Loggers["A.B"]);
        Assert.Equal(1, _buffer.Loggers["X.Y"]);
    }

    /// <summary>
    /// The page redraws only when one of these says so, so an ingest that changed nothing visible
    /// must not set them — and each flag is handed out once.
    /// </summary>
    [Fact]
    public void The_dirty_flags_are_raised_once_per_change()
    {
        Assert.False(_buffer.TakeDirty());
        Assert.False(_buffer.TakeBufferDirty());

        _buffer.Ingest(new[] { Entry("one") });

        Assert.True(_buffer.TakeDirty());
        Assert.False(_buffer.TakeDirty());
        Assert.True(_buffer.TakeBufferDirty());
        Assert.False(_buffer.TakeBufferDirty());
    }

    /// <summary>
    /// A batch filtered out entirely still moves the header combos and the logger tree, so it is
    /// worth a redraw — but not a snapshot rebuild, since the rows are unchanged.
    /// </summary>
    [Fact]
    public void A_batch_that_matches_nothing_still_redraws_when_it_adds_a_filter_value()
    {
        _buffer.TextFilter = "nothing matches this";

        _buffer.Ingest(new[] { Entry("one", environment: "PROD") });

        Assert.True(_buffer.TakeDirty());
        Assert.False(_buffer.TakeBufferDirty());
        Assert.Equal(new[] { "PROD" }, _buffer.Environments);
    }
}
