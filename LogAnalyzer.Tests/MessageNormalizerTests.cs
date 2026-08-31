using System.Text.RegularExpressions;
using LogAnalyzer.Models;
using LogAnalyzer.Services;
using Xunit;

namespace LogAnalyzer.Tests;

/// <summary>
/// <see cref="MessageNormalizer"/> replaced seven chained <c>Regex.Replace</c> calls with a single
/// scan. The original chain is kept here as a reference implementation so the replacement can be
/// held to it — over the real sample corpus, not just hand-picked strings.
/// </summary>
public class MessageNormalizerTests
{
    [Theory]
    [InlineData("Order 12345 failed", "Order {N} failed")]
    [InlineData("User 'admin' not found", "User '{VAL}' not found")]
    [InlineData("Took 01:02:03 to finish", "Took {DURATION} to finish")]
    [InlineData("Took 1:02:03.456 to finish", "Took {DURATION} to finish")]
    [InlineData("Due 2026-07-08 at noon", "Due {DATE} at noon")]
    [InlineData("Due 08/07/2026 at noon", "Due {DATE} at noon")]
    [InlineData("Id 550e8400-e29b-41d4-a716-446655440000 rejected", "Id {GUID} rejected")]
    [InlineData("Hash 0123456789abcdef01 mismatch", "Hash {HEX} mismatch")]
    [InlineData("lots   of\t whitespace", "lots of whitespace")]
    [InlineData("  trimmed  ", "trimmed")]
    [InlineData("", "")]
    [InlineData("nothing to mask here", "nothing to mask here")]
    public void Masks_the_varying_shapes(string message, string expected) =>
        Assert.Equal(expected, MessageNormalizer.Signature(message));

    [Fact]
    public void An_unterminated_quote_is_left_alone()
    {
        // '[^']*' needs a closing quote, so "it's" must survive as written.
        Assert.Equal("it's {N} items", MessageNormalizer.Signature("it's 5 items"));
    }

    [Fact]
    public void Two_occurrences_of_the_same_event_share_a_signature()
    {
        var a = MessageNormalizer.Signature("Timeout after 30012 ms calling 'GetInvoice' for company 4471");
        var b = MessageNormalizer.Signature("Timeout after 45 ms calling 'GetOrder' for company 9");

        Assert.Equal(a, b);
    }

    [Fact]
    public void Different_events_do_not_share_a_signature()
    {
        var a = MessageNormalizer.Signature("Timeout after 30012 ms");
        var b = MessageNormalizer.Signature("Connection refused after 30012 ms");

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Result_is_capped()
    {
        var signature = MessageNormalizer.Signature(new string('x', 5_000));
        Assert.Equal(400, signature.Length);
    }

    /// <summary>The property that actually matters: same output as the code it replaced.</summary>
    [Fact]
    public void Matches_the_original_regex_pipeline_over_the_sample_corpus()
    {
        var messages = SampleLogs.Messages().ToList();
        Assert.True(messages.Count > 500, $"corpus too small to be meaningful ({messages.Count} messages)");

        var mismatches = new List<string>();
        foreach (var message in messages)
        {
            var humanPart = HumanPart(message);
            var expected = ReferencePipeline(humanPart);
            var actual = MessageNormalizer.Signature(humanPart);
            if (expected != actual && mismatches.Count < 5)
                mismatches.Add($"in:  {humanPart}\nold: {expected}\nnew: {actual}");
        }

        Assert.Empty(mismatches);
    }

    [Theory]
    [InlineData("Order 12345 failed at 2026-07-08 12:00:00 for 'X'")]
    [InlineData("Id 550e8400-e29b-41d4-a716-446655440000 hash 0123456789abcdef0123")]
    [InlineData("no digits at all, just words")]
    [InlineData("abcdef")]
    [InlineData("x12:34:56")]
    [InlineData("12'34'")]
    [InlineData("'a'b'c'")]
    [InlineData("mixed 1:2:3 and 01:02:03 and 2026/1/1")]
    [InlineData("path C:\\logs\\2026-07-08\\all.log")]
    [InlineData("\\\\server\\share\\Logs\\all_2026-06-30.log")]
    public void Matches_the_original_regex_pipeline_on_awkward_inputs(string message) =>
        Assert.Equal(ReferencePipeline(message), MessageNormalizer.Signature(message));

    /// <summary>How <c>LogEntry.Signature</c> trims a message before normalising it.</summary>
    private static string HumanPart(string message)
    {
        var idx = message.IndexOf("stackTrace:", StringComparison.OrdinalIgnoreCase);
        var s = idx >= 0 ? message[..idx] : message;
        var crlf = s.IndexOf("\\CRLF", StringComparison.Ordinal);
        return crlf >= 0 ? s[..crlf] : s;
    }

    // ---- the implementation that was replaced, verbatim apart from the trimming above ----

    private static readonly Regex Guid = new(@"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}");
    private static readonly Regex Date = new(@"\b\d{2,4}[-/]\d{1,2}[-/]\d{1,4}\b");
    private static readonly Regex Duration = new(@"\b\d{1,2}:\d{2}:\d{2}(\.\d+)?\b");
    private static readonly Regex Hex = new(@"\b[0-9A-Fa-f]{16,}\b");
    private static readonly Regex Quoted = new(@"'[^']*'");
    private static readonly Regex Number = new(@"\d+");
    private static readonly Regex Whitespace = new(@"\s+");

    private static string ReferencePipeline(string humanPart)
    {
        if (string.IsNullOrEmpty(humanPart)) return "";

        var s = humanPart;
        s = Guid.Replace(s, "{GUID}");
        s = Duration.Replace(s, "{DURATION}");
        s = Date.Replace(s, "{DATE}");
        s = Hex.Replace(s, "{HEX}");
        s = Quoted.Replace(s, "'{VAL}'");
        s = Number.Replace(s, "{N}");
        s = Whitespace.Replace(s, " ").Trim();

        return s.Length > 400 ? s[..400] : s;
    }
}

public class LogEntryTests
{
    [Fact]
    public void ShortMessage_stops_at_the_stack_trace()
    {
        var entry = new LogEntry { Message = @"Boom happened\CRLFstackTrace: at Foo() at Bar()" };

        Assert.Equal("Boom happened", entry.ShortMessage);
        Assert.True(entry.HasStackTrace);
    }

    [Fact]
    public void ShortMessage_stops_at_the_first_CRLF_marker()
    {
        var entry = new LogEntry { Message = @"line one\CRLFline two" };

        Assert.Equal("line one", entry.ShortMessage);
        Assert.False(entry.HasStackTrace);
    }

    [Fact]
    public void ShortMessage_is_capped_with_an_ellipsis()
    {
        var entry = new LogEntry { Message = new string('y', 400) };

        Assert.Equal(301, entry.ShortMessage.Length);
        Assert.EndsWith("…", entry.ShortMessage);
    }

    [Fact]
    public void PrettyMessage_turns_markers_into_newlines()
    {
        var entry = new LogEntry { Message = @"a\CRLFb" };

        Assert.Equal("a\nb", entry.PrettyMessage);
    }

    /// <summary>Derived values are cached; asking twice must not give two different answers.</summary>
    [Fact]
    public void Derived_values_are_stable_across_repeated_reads()
    {
        var entry = new LogEntry { Message = "Order 42 failed stackTrace: at Foo()" };

        Assert.Same(entry.ShortMessage, entry.ShortMessage);
        Assert.Same(entry.Signature, entry.Signature);
        Assert.Equal("Order {N} failed", entry.Signature);
        Assert.True(entry.HasStackTrace);
    }

    [Fact]
    public void Empty_message_is_handled()
    {
        var entry = new LogEntry();

        Assert.Equal("", entry.ShortMessage);
        Assert.Equal("", entry.Signature);
        Assert.False(entry.HasStackTrace);
    }
}
