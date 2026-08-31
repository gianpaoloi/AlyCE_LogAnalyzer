using System.Text;
using System.Text.Json;
using LogAnalyzer.Models;
using LogAnalyzer.Services;
using Xunit;

namespace LogAnalyzer.Tests;

public class LogExportTests
{
    private static readonly LogEntry[] Sample =
    {
        new()
        {
            Time = new DateTime(2026, 7, 8, 10, 0, 0, 160),
            Level = "ERROR",
            ThreadId = ".NET TP Worker",
            Environment = "E1",
            Company = "C1",
            Username = "admin",
            Cid = "abc-123",
            Logger = "A.B",
            Message = @"Boom\CRLFat Foo()",
        },
        new()
        {
            Time = new DateTime(2026, 7, 8, 10, 0, 1),
            Level = "INFO",
            Message = "plain",
        },
    };

    private static string Csv(params LogEntry[] entries) =>
        Encoding.UTF8.GetString(LogExport.ToCsv(entries));

    [Fact]
    public void Csv_starts_with_a_bom_so_excel_reads_it_as_utf8()
    {
        var bytes = LogExport.ToCsv(Sample);

        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes.Take(3));
    }

    [Fact]
    public void Csv_has_a_header_and_one_row_per_entry()
    {
        var lines = Csv(Sample).TrimEnd('\r', '\n').Split("\r\n");

        Assert.Equal(3, lines.Length);
        Assert.EndsWith("time,level,environment,company,username,threadid,cid,logger,message", lines[0]);
        Assert.StartsWith("2026-07-08 10:00:00.1600,ERROR,E1,C1,admin,", lines[1]);
    }

    [Fact]
    public void Csv_expands_crlf_markers_into_a_quoted_multiline_field()
    {
        var csv = Csv(Sample[0]);

        Assert.Contains("\"Boom\nat Foo()\"", csv);
    }

    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("has,comma", "\"has,comma\"")]
    [InlineData("has\"quote", "\"has\"\"quote\"")]
    [InlineData("both,\"x\"", "\"both,\"\"x\"\"\"")]
    [InlineData("line\nbreak", "\"line\nbreak\"")]
    [InlineData("", "")]
    public void Csv_quotes_and_escapes_exactly_when_needed(string message, string expected)
    {
        var csv = Csv(new LogEntry { Level = "INFO", Message = message });

        Assert.EndsWith("," + expected + "\r\n", csv);
    }

    /// <summary>
    /// Log text is attacker-influenced, and a leading <c>=</c> makes Excel evaluate it as a formula.
    /// </summary>
    [Theory]
    [InlineData("=1+1", "\"'=1+1\"")]
    [InlineData("+cmd", "\"'+cmd\"")]
    [InlineData("@import", "\"'@import\"")]
    public void Csv_defuses_leading_formula_characters(string message, string expected)
    {
        var csv = Csv(new LogEntry { Level = "INFO", Message = message });

        Assert.EndsWith("," + expected + "\r\n", csv);
    }

    /// <summary>A negative number is not a formula worth mangling every log line for.</summary>
    [Fact]
    public void Csv_leaves_a_leading_minus_alone()
    {
        var csv = Csv(new LogEntry { Level = "INFO", Message = "-1 item" });

        Assert.EndsWith(",-1 item\r\n", csv);
    }

    [Fact]
    public void Json_lines_round_trip_back_through_the_parser()
    {
        var bytes = LogExport.ToJsonLines(Sample);
        var text = Encoding.UTF8.GetString(bytes);
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(2, lines.Length);

        var parser = new LogParser();
        var reparsed = lines.Select(l => parser.TryParse(l, "export.log")).ToList();

        Assert.All(reparsed, e => Assert.NotNull(e));
        Assert.Equal(Sample[0].Time, reparsed[0]!.Time);
        Assert.Equal(Sample[0].Level, reparsed[0]!.Level);
        Assert.Equal(Sample[0].Message, reparsed[0]!.Message);
        Assert.Equal(Sample[0].Environment, reparsed[0]!.Environment);
        Assert.Equal(Sample[0].Cid, reparsed[0]!.Cid);
        Assert.Equal(Sample[1].Message, reparsed[1]!.Message);
        Assert.Null(reparsed[1]!.Environment);
    }

    [Fact]
    public void Json_lines_are_one_valid_document_per_line()
    {
        var text = Encoding.UTF8.GetString(LogExport.ToJsonLines(Sample));

        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            using var doc = JsonDocument.Parse(line);
            Assert.Equal(JsonValueKind.Object, doc.RootElement.ValueKind);
        }
    }

    [Fact]
    public void Empty_input_produces_just_the_csv_header_and_no_json_lines()
    {
        Assert.Empty(LogExport.ToJsonLines(Array.Empty<LogEntry>()));

        var csv = Csv(Array.Empty<LogEntry>());
        Assert.Single(csv.TrimEnd('\r', '\n').Split("\r\n"));
    }

    [Fact]
    public async Task OpenExportAsync_gives_a_readable_stream_that_cleans_up_after_itself()
    {
        string path;
        await using (var stream = await LogExport.OpenExportAsync(Sample, jsonLines: false))
        {
            path = ((FileStream)stream).Name;
            Assert.True(File.Exists(path));

            using var reader = new StreamReader(stream);
            var text = await reader.ReadToEndAsync();
            Assert.Contains("ERROR", text);
            Assert.Contains("plain", text);
        }

        Assert.False(File.Exists(path));   // FileOptions.DeleteOnClose
    }

    [Fact]
    public async Task OpenExportAsync_matches_the_in_memory_output()
    {
        foreach (var json in new[] { false, true })
        {
            await using var stream = await LogExport.OpenExportAsync(Sample, json);
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms);

            var expected = json ? LogExport.ToJsonLines(Sample) : LogExport.ToCsv(Sample);
            Assert.Equal(expected, ms.ToArray());
        }
    }
}

public class LogFilterTests
{
    [Theory]
    [InlineData("A.B", "A.B", true)]
    [InlineData("A.B.C", "A.B", true)]
    [InlineData("a.b.c", "A.B", true)]
    [InlineData("A.Bc", "A.B", false)]
    [InlineData("A", "A.B", false)]
    [InlineData("X.A.B", "A.B", false)]
    [InlineData("", "A.B", false)]
    public void MatchesPrefix_only_matches_the_node_or_its_descendants(string logger, string prefix, bool expected) =>
        Assert.Equal(expected, LogFilter.MatchesPrefix(logger, prefix));

    [Fact]
    public void MatchesPrefix_rejects_null()
    {
        Assert.False(LogFilter.MatchesPrefix(null, "A"));
    }

    [Fact]
    public void An_empty_filter_matches_everything()
    {
        var filter = new LogFilter();

        Assert.True(filter.Matches(new LogEntry { Level = "INFO" }));
        Assert.True(filter.Matches(new LogEntry { Level = "ERROR", Environment = "E1" }));
    }

    [Fact]
    public void A_time_window_is_inclusive_at_both_ends()
    {
        var filter = new LogFilter
        {
            From = new DateTime(2026, 7, 8, 10, 0, 0),
            To = new DateTime(2026, 7, 8, 11, 0, 0),
        };

        Assert.True(filter.Matches(new LogEntry { Time = filter.From.Value }));
        Assert.True(filter.Matches(new LogEntry { Time = filter.To.Value }));
        Assert.False(filter.Matches(new LogEntry { Time = filter.From.Value.AddTicks(-1) }));
        Assert.False(filter.Matches(new LogEntry { Time = filter.To.Value.AddTicks(1) }));
    }

    [Fact]
    public void Entries_without_a_facet_value_are_excluded_when_that_facet_is_filtered()
    {
        var filter = new LogFilter();
        filter.Environments.Add("E1");

        Assert.False(filter.Matches(new LogEntry { Level = "INFO", Environment = null }));
        Assert.True(filter.Matches(new LogEntry { Level = "INFO", Environment = "E1" }));
    }
}

public class LogLevelsTests
{
    [Theory]
    [InlineData("DEBUG", LogLevels.Debug)]
    [InlineData("debug", LogLevels.Debug)]
    [InlineData("INFO", LogLevels.Info)]
    [InlineData("WARN", LogLevels.Warn)]
    [InlineData("WARNING", LogLevels.Warn)]
    [InlineData("warning", LogLevels.Warn)]
    [InlineData("ERROR", LogLevels.Error)]
    [InlineData("FATAL", LogLevels.Error)]
    [InlineData("TRACE", LogLevels.Other)]
    [InlineData("UNKNOWN", LogLevels.Other)]
    [InlineData("", LogLevels.Other)]
    [InlineData(null, LogLevels.Other)]
    public void Series_folds_the_aliases_together(string? level, string expected) =>
        Assert.Equal(expected, LogLevels.Series(level));

    [Fact]
    public void Colours_agree_with_the_series()
    {
        Assert.Equal(ChartColors.Level("ERROR"), ChartColors.Level("FATAL"));
        Assert.Equal(ChartColors.Level("WARN"), ChartColors.Level("warning"));
        Assert.NotEqual(ChartColors.Level("ERROR"), ChartColors.Level("WARN"));
    }
}
