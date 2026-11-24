using LogAnalyzer.Models;
using LogAnalyzer.Services.Ai;
using Xunit;

namespace LogAnalyzer.Tests;

public class AiFeaturesTests
{
    private static readonly DateTime T0 = new(2026, 5, 4, 10, 0, 0);

    private static LogEntry Entry(string level, string message, int minutes, string logger = "Shop.Orders") => new()
    {
        Level = level,
        Message = message,
        Logger = logger,
        Time = T0.AddMinutes(minutes),
        Environment = "prod",
    };

    [Fact]
    public void ExtractObject_reads_json_wrapped_in_prose_or_a_code_fence()
    {
        var obj = AiJson.ExtractObject("Here you go:\n```json\n{\"title\": \"NPE in payments\"}\n```");

        Assert.NotNull(obj);
        Assert.Equal("NPE in payments", obj!.Value.GetProperty("title").GetString());
    }

    [Fact]
    public void ExtractObject_returns_null_when_there_is_no_object()
    {
        Assert.Null(AiJson.ExtractObject("no braces here"));
        Assert.Null(AiJson.ExtractObject("{ not json }"));
    }

    [Fact]
    public void ParseQuery_keeps_only_the_levels_and_environment_that_exist()
    {
        const string reply = "{\"levels\": [\"error\", \"DEBUG\", \"nonsense\"], \"environment\": \"PROD\", " +
                             "\"logger\": \"payment\", \"text\": null, \"from\": \"2026-05-04T09:00:00\", \"to\": null}";

        var query = AiFeatures.ParseQuery(reply, new[] { "ERROR", "WARN" }, new[] { "prod", "test" });

        Assert.Equal(new[] { "ERROR" }, query.Levels);
        Assert.Equal("prod", query.Environment);
        Assert.Equal("payment", query.Logger);
        Assert.Null(query.Text);
        Assert.Equal(new DateTime(2026, 5, 4, 9, 0, 0), query.From);
        Assert.Null(query.To);
    }

    [Fact]
    public void ParseQuery_drops_an_unknown_environment()
    {
        var query = AiFeatures.ParseQuery("{\"environment\": \"moon\"}", new[] { "ERROR" }, new[] { "prod" });

        Assert.Null(query.Environment);
    }

    [Fact]
    public void ParseQuery_rejects_an_answer_that_is_not_a_filter()
    {
        var e = Assert.Throws<AiException>(() => AiFeatures.ParseQuery("I cannot help with that.", new[] { "ERROR" }, Array.Empty<string>()));
        Assert.Contains("not a filter", e.Message);
    }

    [Fact]
    public void ParseIssueDraft_needs_both_a_title_and_a_body()
    {
        var draft = AiFeatures.ParseIssueDraft("{\"title\": \"Timeout\", \"body\": \"Steps\"}");
        Assert.Equal("Timeout", draft.Title);
        Assert.Equal("Steps", draft.Body);

        Assert.Throws<AiException>(() => AiFeatures.ParseIssueDraft("{\"title\": \"Only a title\"}"));
    }

    [Fact]
    public void Spike_preview_includes_the_baseline_and_masks_values()
    {
        var window = new[] { Entry("ERROR", "timeout for ops@example.com", 60), Entry("ERROR", "timeout for ops@example.com", 61) };
        var baseline = new[] { Entry("INFO", "ok", 0) };

        var preview = AiFeatures.BuildWindowPreview(WindowMode.Spike, window, baseline, "en");

        Assert.Contains("Baseline, the same length just before the window", preview.User);
        Assert.Contains("[ERROR] x2", preview.User);
        Assert.DoesNotContain("ops@example.com", preview.User);
        Assert.Equal(AiFeatures.SpikeSystemPrompt, preview.System);
    }

    [Fact]
    public void Incident_preview_has_no_baseline()
    {
        var preview = AiFeatures.BuildWindowPreview(WindowMode.Incident, new[] { Entry("WARN", "slow", 0) },
                                                    Array.Empty<LogEntry>(), "en");

        Assert.DoesNotContain("Baseline", preview.User);
        Assert.Equal(AiFeatures.IncidentSystemPrompt, preview.System);
    }

    [Fact]
    public void Query_preview_lists_names_and_masks_the_question()
    {
        var preview = AiFeatures.BuildQueryPreview("errors for ops@example.com", new[] { "ERROR" },
                                                   new[] { "prod" }, new[] { "Shop.Orders" }, "it");

        Assert.Contains("Levels: ERROR", preview.User);
        Assert.Contains("Environments: prod", preview.User);
        Assert.Contains("Loggers: Shop.Orders", preview.User);
        Assert.DoesNotContain("ops@example.com", preview.User);
        Assert.Equal(AiFeatures.QuerySystemPrompt, preview.System);
    }

    [Fact]
    public void Overview_preview_has_counts_busiest_hours_and_problems()
    {
        var stats = new LogStats
        {
            TotalEntries = 1200,
            FileCount = 2,
            FirstTime = T0,
            LastTime = T0.AddHours(3),
            ByLevel = new() { ["ERROR"] = 40, ["INFO"] = 1160 },
            ByEnvironment = new() { ["prod"] = 1200 },
            ByLogger = new() { ["Shop.Orders"] = 1200 },
            Timeline = new List<TimeBucket>
            {
                new() { Start = T0, Error = 5, Warn = 1, Info = 10 },
                new() { Start = T0.AddHours(1), Error = 30, Info = 100 },
            },
        };
        var problem = new MessageGroup { Level = "ERROR", SampleMessage = "payment failed", Count = 30, FirstSeen = T0, LastSeen = T0 };

        var preview = AiFeatures.BuildOverviewPreview(stats, new[] { problem }, "en");

        Assert.Contains("in 2 file(s)", preview.User);
        Assert.Contains("ERROR 40", preview.User);
        Assert.Contains("Busiest hours", preview.User);
        Assert.Contains("payment failed", preview.User);
        Assert.Equal(AiFeatures.OverviewSystemPrompt, preview.System);
    }

    [Fact]
    public void Issue_draft_preview_is_in_english_and_masks_values()
    {
        var group = new MessageGroup { Level = "ERROR", SampleMessage = "login for jane.doe@example.com", Count = 3, FirstSeen = T0, LastSeen = T0 };

        var preview = AiFeatures.BuildIssueDraftPreview(group);

        Assert.DoesNotContain("jane.doe@example.com", preview.User);
        Assert.Contains("in English", preview.System);
    }

    [Fact]
    public void Query_describe_says_what_the_filter_asks_for()
    {
        var query = new AiQuery(new[] { "ERROR" }, "prod", null, "timeout", null, null);

        Assert.Equal("levels ERROR; environment prod; message contains \"timeout\"", query.Describe());
    }
}
