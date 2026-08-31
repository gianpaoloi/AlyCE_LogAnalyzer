using System.Globalization;
using System.Text;
using LogAnalyzer.Services;
using Xunit;

namespace LogAnalyzer.Tests;

public class LogParserTests
{
    private static readonly LogParser Parser = new();

    [Fact]
    public void Parses_the_fields_of_a_real_line()
    {
        const string line = """
            { "time": "2026-07-08 00:00:00.1600", "level": "WARN", "threadid": ".NET TP Worker", "environment": "GDB_TSE10_2189", "username": "admin", "company": "0", "message": "MakeSubscriptionOptions - specify the destination!", "logger": "TeamSystem.AlyCE" }
            """;

        var entry = Parser.TryParse(line, "all.log");

        Assert.NotNull(entry);
        Assert.Equal(new DateTime(2026, 7, 8, 0, 0, 0, 160), entry!.Time);
        Assert.Equal("WARN", entry.Level);
        Assert.Equal(".NET TP Worker", entry.ThreadId);
        Assert.Equal("GDB_TSE10_2189", entry.Environment);
        Assert.Equal("admin", entry.Username);
        Assert.Equal("0", entry.Company);
        Assert.Equal("TeamSystem.AlyCE", entry.Logger);
        Assert.Equal("all.log", entry.SourceFile);
        Assert.Contains("MakeSubscriptionOptions", entry.Message);
    }

    [Theory]
    [InlineData("2026-07-08 00:00:00.1600", "2026-07-08T00:00:00.1600000")]
    [InlineData("2026-07-08 00:00:00.160", "2026-07-08T00:00:00.1600000")]
    [InlineData("2026-07-08 00:00:00.16", "2026-07-08T00:00:00.1600000")]
    [InlineData("2026-07-08 00:00:00.1", "2026-07-08T00:00:00.1000000")]
    [InlineData("2026-07-08 00:00:00", "2026-07-08T00:00:00.0000000")]
    [InlineData("2026-07-08T00:00:00.160", "2026-07-08T00:00:00.1600000")]
    public void Accepts_every_documented_timestamp_shape(string time, string expected)
    {
        var entry = Parser.TryParse($$"""{ "time": "{{time}}", "level": "INFO" }""", "x.log");

        Assert.NotNull(entry);
        Assert.Equal(DateTime.Parse(expected, CultureInfo.InvariantCulture), entry!.Time);
    }

    /// <summary>
    /// The fallback used to be a bare <c>DateTime.TryParse</c>, which reads the current culture —
    /// so the same file parsed differently on an it-IT machine than on an en-US one.
    /// </summary>
    [Theory]
    [InlineData("it-IT")]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    public void Timestamps_do_not_depend_on_the_machine_culture(string culture)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);
            var entry = Parser.TryParse("""{ "time": "2026-07-08 13:45:01.5", "level": "INFO" }""", "x.log");

            Assert.NotNull(entry);
            Assert.Equal(new DateTime(2026, 7, 8, 13, 45, 1, 500), entry!.Time);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Missing_or_unparseable_time_becomes_MinValue()
    {
        Assert.Equal(DateTime.MinValue, Parser.TryParse("""{ "level": "INFO" }""", "x.log")!.Time);
        Assert.Equal(DateTime.MinValue, Parser.TryParse("""{ "time": "not a date" }""", "x.log")!.Time);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json at all")]
    [InlineData("{ broken")]
    [InlineData("[1,2,3]")]
    public void Rejects_blank_and_malformed_lines(string line) =>
        Assert.Null(Parser.TryParse(line, "x.log"));

    [Fact]
    public void Missing_optional_fields_become_null_and_level_falls_back()
    {
        var entry = Parser.TryParse("""{ "time": "2026-07-08 00:00:00", "message": "hi" }""", "x.log");

        Assert.NotNull(entry);
        Assert.Equal("UNKNOWN", entry!.Level);
        Assert.Null(entry.Environment);
        Assert.Null(entry.Username);
        Assert.Null(entry.Company);
        Assert.Null(entry.Cid);
        Assert.Equal("", entry.Logger);
        Assert.Equal("", entry.ThreadId);
    }

    [Fact]
    public void Blank_optional_fields_are_treated_as_missing()
    {
        var entry = Parser.TryParse("""{ "level": "INFO", "environment": "", "company": "  " }""", "x.log");

        Assert.NotNull(entry);
        Assert.Null(entry!.Environment);
        Assert.Null(entry.Company);
    }

    /// <summary>The reader has to step over values it doesn't care about, of any shape.</summary>
    [Fact]
    public void Skips_unknown_properties_including_nested_ones()
    {
        const string line = """
            { "extra": { "nested": [1, 2, { "deep": true }] }, "level": "ERROR", "tags": ["a","b"], "n": 42, "flag": null, "message": "boom" }
            """;

        var entry = Parser.TryParse(line, "x.log");

        Assert.NotNull(entry);
        Assert.Equal("ERROR", entry!.Level);
        Assert.Equal("boom", entry.Message);
    }

    [Fact]
    public void Non_string_values_for_known_fields_do_not_break_the_line()
    {
        var entry = Parser.TryParse("""{ "level": "INFO", "company": 12345, "message": "ok" }""", "x.log");

        Assert.NotNull(entry);
        Assert.Null(entry!.Company);
        Assert.Equal("ok", entry.Message);
    }

    [Fact]
    public void Handles_escaped_and_multi_byte_characters()
    {
        var entry = Parser.TryParse(
            """{ "level": "INFO", "message": "quote \" backslash \\ unicode è emoji 🚀 accented àèì" }""",
            "x.log");

        Assert.NotNull(entry);
        Assert.Contains("quote \" backslash \\", entry!.Message);
        Assert.Contains("è", entry.Message);
        Assert.Contains("🚀", entry.Message);
        Assert.Contains("àèì", entry.Message);
    }

    [Fact]
    public void Repeated_low_cardinality_values_are_interned_to_one_instance()
    {
        var parser = new LogParser();
        var a = parser.TryParse("""{ "level": "INFO", "logger": "TeamSystem.AlyCE" }""", "x.log");
        var b = parser.TryParse("""{ "level": "INFO", "logger": "TeamSystem.AlyCE" }""", "x.log");

        Assert.NotNull(a);
        Assert.NotNull(b);
        Assert.Same(a!.Logger, b!.Logger);
        Assert.Same(a.Level, b.Level);
    }

    [Fact]
    public void A_leading_byte_order_mark_does_not_hide_the_first_line()
    {
        var bytes = Encoding.UTF8.GetPreamble()
            .Concat(Encoding.UTF8.GetBytes("""{ "level": "INFO", "message": "first" }"""))
            .ToArray();

        // The BOM is stripped by Utf8LineReader / LogWatcher before the parser sees the line;
        // on its own the parser must still reject it, which is what proves the strip matters.
        Assert.Null(Parser.TryParse(bytes, "x.log"));

        using var stream = new MemoryStream(bytes);
        using var reader = new Utf8LineReader(stream);
        Assert.True(reader.TryReadLine(out var line));
        Assert.Equal("first", Parser.TryParse(line, "x.log")!.Message);
    }

    [Fact]
    public void Parses_every_line_of_the_sample_logs()
    {
        foreach (var file in SampleLogs.Files())
        {
            var parser = new LogParser();
            using var stream = File.OpenRead(file);
            using var reader = new Utf8LineReader(stream);

            int lines = 0, parsedOk = 0;
            while (reader.TryReadLine(out var line))
            {
                if (line.IsEmpty) continue;
                lines++;
                if (parser.TryParse(line, Path.GetFileName(file)) is not null) parsedOk++;
            }

            Assert.True(lines > 0, $"no lines read from {file}");
            Assert.Equal(lines, parsedOk);
        }
    }
}
