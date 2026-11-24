using LogAnalyzer.Models;
using LogAnalyzer.Services;
using LogAnalyzer.Services.Ai;
using Xunit;

namespace LogAnalyzer.Tests;

public class AiLogAnalysisTests
{
    private static readonly DateTime T0 = new(2026, 5, 4, 10, 0, 0);

    private static LogEntry Entry(string message, int seconds, string thread = "t1", string? cid = null,
                                  string? env = "prod", string level = "INFO") => new()
    {
        Message = message,
        Level = level,
        Logger = "Shop.Orders",
        Time = T0.AddSeconds(seconds),
        ThreadId = thread,
        Cid = cid,
        Environment = env,
    };

    [Fact]
    public void Find_returns_entries_with_the_same_cid_however_far_apart()
    {
        var seed = Entry("order placed", 0, cid: "c-42");
        var far = Entry("payment ok", 3600, thread: "t9", cid: "c-42");
        var other = Entry("unrelated", 5, thread: "t9", cid: "c-7");

        var found = LogCorrelation.Find(new[] { seed, far, other }, seed, LogCorrelation.DefaultWindow);

        var only = Assert.Single(found);
        Assert.Same(far, only.Entry);
        Assert.Equal(CorrelationReason.Cid, only.Reason);
    }

    [Fact]
    public void Find_returns_same_thread_entries_only_inside_the_window_and_environment()
    {
        var seed = Entry("start", 0);
        var near = Entry("step", 60);
        var tooLate = Entry("later", 600);
        var otherEnv = Entry("staging", 30, env: "test");
        var otherThread = Entry("elsewhere", 30, thread: "t2");

        var found = LogCorrelation.Find(new[] { seed, near, tooLate, otherEnv, otherThread }, seed,
                                        LogCorrelation.DefaultWindow);

        var only = Assert.Single(found);
        Assert.Same(near, only.Entry);
        Assert.Equal(CorrelationReason.Thread, only.Reason);
    }

    [Fact]
    public void Find_never_returns_the_seed_and_gives_nothing_without_a_key()
    {
        var seed = Entry("only line", 0, thread: "", cid: null);
        var same = Entry("same thread", 1, thread: "");

        Assert.Empty(LogCorrelation.Find(new[] { seed, same }, seed, LogCorrelation.DefaultWindow));
    }

    [Fact]
    public void Find_keeps_the_closest_matches_in_time_order_when_capped()
    {
        var seed = Entry("seed", 100, cid: "c");
        var entries = new List<LogEntry> { seed };
        for (var i = 0; i < 10; i++) entries.Add(Entry($"line {i}", i * 10, cid: "c"));

        var found = LogCorrelation.Find(entries, seed, LogCorrelation.DefaultWindow, max: 3);

        Assert.Equal(3, found.Count);
        Assert.Equal(new[] { 70, 80, 90 }, found.Select(f => (int)(f.Entry.Time - T0).TotalSeconds));
    }

    [Fact]
    public void Entry_preview_carries_the_message_and_related_lines_masked()
    {
        var entry = Entry("login failed for jane.doe@example.com from 10.1.2.3", 0, cid: "c");
        var related = new List<CorrelatedEntry>
        {
            new(Entry("retry for jane.doe@example.com", 5, cid: "c"), CorrelationReason.Cid),
        };

        var preview = AiLogAnalysis.BuildEntryPreview(entry, related, "en");

        Assert.Contains("[email]", preview.User);
        Assert.Contains("[ip]", preview.User);
        Assert.DoesNotContain("jane.doe@example.com", preview.User);
        Assert.Contains("Related log lines (1 of 1", preview.User);
        Assert.Contains("same CID", preview.User);
        Assert.Contains("code \"en\"", preview.User);
        Assert.Equal(AiLogAnalysis.EntrySystemPrompt, preview.System);
        Assert.True(preview.Redactions >= 3);
    }

    [Fact]
    public void Entry_preview_has_no_related_section_when_nothing_correlates()
    {
        var preview = AiLogAnalysis.BuildEntryPreview(Entry("alone", 0), Array.Empty<CorrelatedEntry>(), "it");

        Assert.DoesNotContain("Related log lines", preview.User);
        Assert.Contains("code \"it\"", preview.User);
    }

    [Fact]
    public void Triage_preview_lists_clusters_most_severe_first_and_caps_their_number()
    {
        var groups = Enumerable.Range(0, AiLogAnalysis.MaxTriageClusters + 5)
            .Select(i => new MessageGroup
            {
                Level = "ERROR",
                Signature = $"ERROR|sig{i}",
                SampleMessage = $"failure number {i}",
                Count = 100 - i,
                FirstSeen = T0,
                LastSeen = T0.AddHours(1),
            })
            .ToList();

        var preview = AiLogAnalysis.BuildTriagePreview(groups, "en");

        Assert.Contains($"Clusters ({AiLogAnalysis.MaxTriageClusters} of {groups.Count}", preview.User);
        Assert.Contains("failure number 0", preview.User);
        Assert.Contains($"failure number {AiLogAnalysis.MaxTriageClusters - 1}", preview.User);
        Assert.DoesNotContain($"failure number {AiLogAnalysis.MaxTriageClusters}", preview.User);
        Assert.Equal(AiLogAnalysis.TriageSystemPrompt, preview.System);
    }

    [Fact]
    public void Triage_preview_masks_values_in_cluster_messages()
    {
        var group = new MessageGroup
        {
            Level = "WARN",
            SampleMessage = "token=abc123 for ops@example.com",
            Count = 2,
            FirstSeen = T0,
            LastSeen = T0,
        };

        var preview = AiLogAnalysis.BuildTriagePreview(new[] { group }, "en");

        Assert.DoesNotContain("abc123", preview.User);
        Assert.DoesNotContain("ops@example.com", preview.User);
    }
}
