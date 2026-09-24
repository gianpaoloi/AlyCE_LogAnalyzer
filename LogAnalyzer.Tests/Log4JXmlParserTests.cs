using LogAnalyzer.Models;
using LogAnalyzer.Services;
using Xunit;

namespace LogAnalyzer.Tests;

public class Log4JXmlParserTests
{
    private readonly Log4JXmlParser _parser = new();

    /// <summary>2026-07-08 10:00:00 UTC, as the epoch milliseconds the target puts on the wire.</summary>
    private const long Timestamp = 1783677600000;

    private static DateTime Expected(long unixMs) =>
        DateTimeOffset.FromUnixTimeMilliseconds(unixMs).ToLocalTime().DateTime;

    /// <summary>
    /// What the target actually sends with the documented configuration — including the fact that
    /// the <c>log4j:</c> and <c>nlog:</c> prefixes are never declared on it.
    /// </summary>
    [Fact]
    public void Parses_the_fields_of_a_real_NLogViewer_event()
    {
        var xml = $"""
            <log4j:event logger="TeamSystem.AlyCE.Billing" level="WARN" timestamp="{Timestamp}" thread="12">
              <log4j:message>MakeSubscriptionOptions - specify the destination!</log4j:message>
              <log4j:properties>
                <log4j:data name="log4japp" value="AlyCE.Service(4242)" />
                <log4j:data name="log4jmachinename" value="HOST01" />
              </log4j:properties>
            </log4j:event>
            """;

        var entry = _parser.TryParse(xml, "127.0.0.1:51000");

        Assert.NotNull(entry);
        Assert.Equal(Expected(Timestamp), entry!.Time);
        Assert.Equal("WARN", entry.Level);
        Assert.Equal("TeamSystem.AlyCE.Billing", entry.Logger);
        Assert.Equal("12", entry.ThreadId);
        Assert.Equal("MakeSubscriptionOptions - specify the destination!", entry.Message);
        // No environment on the wire, so the sending machine takes that column.
        Assert.Equal("HOST01", entry.Environment);
        // And the sending application takes the source column.
        Assert.Equal("AlyCE.Service(4242)", entry.SourceFile);
    }

    /// <summary>
    /// NLog writes its own level names ("Warn"), so without normalising them the same level would
    /// appear twice in the filter — once from the network and once from the files.
    /// </summary>
    [Theory]
    [InlineData("Info", "INFO")]
    [InlineData("Warn", "WARN")]
    [InlineData("Error", "ERROR")]
    [InlineData("Fatal", "FATAL")]
    [InlineData("Trace", "TRACE")]
    public void Level_names_are_normalised_to_upper_case(string sent, string expected)
    {
        var entry = _parser.TryParse(Event(level: sent), "s");

        Assert.NotNull(entry);
        Assert.Equal(expected, entry!.Level);
    }

    [Fact]
    public void An_event_with_no_recognisable_timestamp_gets_DateTime_MinValue()
    {
        var entry = _parser.TryParse(
            """<log4j:event logger="A" level="INFO" timestamp="not-a-number"><log4j:message>x</log4j:message></log4j:event>""",
            "s");

        Assert.NotNull(entry);
        Assert.Equal(DateTime.MinValue, entry!.Time);
    }

    /// <summary>A timestamp far outside the representable range must not throw.</summary>
    [Theory]
    [InlineData("999999999999999999")]
    [InlineData("-999999999999999999")]
    public void An_out_of_range_timestamp_gets_DateTime_MinValue(string timestamp)
    {
        var entry = _parser.TryParse(
            $"""<log4j:event logger="A" level="INFO" timestamp="{timestamp}"><log4j:message>x</log4j:message></log4j:event>""",
            "s");

        Assert.NotNull(entry);
        Assert.Equal(DateTime.MinValue, entry!.Time);
    }

    /// <summary>
    /// The throwable is a separate element on the wire but has to end up behind the marker the rest
    /// of the app looks for, or the row shows no "stack" badge and the detail dialog no trace.
    /// </summary>
    [Fact]
    public void A_throwable_becomes_the_entrys_stack_trace()
    {
        var xml = $"""
            <log4j:event logger="A" level="ERROR" timestamp="{Timestamp}">
              <log4j:message>Boom</log4j:message>
              <log4j:throwable>System.InvalidOperationException: Boom
               at Foo.Bar()
               at Foo.Baz()</log4j:throwable>
            </log4j:event>
            """;

        var entry = _parser.TryParse(xml, "s");

        Assert.NotNull(entry);
        Assert.True(entry!.HasStackTrace);
        Assert.Equal("Boom", entry.ShortMessage);
        Assert.Contains("System.InvalidOperationException", entry.PrettyMessage);
        Assert.Contains("at Foo.Baz()", entry.PrettyMessage);
        // Real newlines are stored as the file format's marker, so the grid still shows one line.
        Assert.DoesNotContain('\n', entry.Message);
    }

    /// <summary>
    /// A message can carry line breaks of its own. They must be stored as markers too, or the grid
    /// cell renders several lines and ShortMessage stops being short.
    /// </summary>
    [Fact]
    public void Newlines_inside_a_message_become_markers()
    {
        var xml = $"""
            <log4j:event logger="A" level="INFO" timestamp="{Timestamp}"><log4j:message>line one
            line two</log4j:message></log4j:event>
            """;

        var entry = _parser.TryParse(xml, "s");

        Assert.NotNull(entry);
        Assert.Equal("line one", entry!.ShortMessage);
        Assert.Equal("line one\nline two", entry.PrettyMessage);
    }

    [Fact]
    public void The_call_site_is_kept_when_the_sender_includes_it()
    {
        var xml = $"""
            <log4j:event logger="A" level="ERROR" timestamp="{Timestamp}">
              <log4j:message>Boom</log4j:message>
              <log4j:locationInfo class="Foo.Service" method="Run" file="C:\src\Service.cs" line="42" />
              <nlog:locationInfo assembly="Foo, Version=1.0.0.0" />
            </log4j:event>
            """;

        var entry = _parser.TryParse(xml, "s");

        Assert.NotNull(entry);
        Assert.True(entry!.HasStackTrace);
        Assert.Contains(@"at Foo.Service.Run in C:\src\Service.cs:42", entry.PrettyMessage);
    }

    /// <summary>
    /// Properties arrive under <c>log4j:</c> or <c>nlog:</c> depending on <c>includeNLogData</c>,
    /// so the prefix must not matter.
    /// </summary>
    [Theory]
    [InlineData("log4j")]
    [InlineData("nlog")]
    public void Known_properties_fill_the_matching_columns(string prefix)
    {
        var xml = $"""
            <log4j:event logger="A" level="INFO" timestamp="{Timestamp}">
              <log4j:message>x</log4j:message>
              <{prefix}:properties>
                <{prefix}:data name="environment" value="GDB_TSE10_2189" />
                <{prefix}:data name="username" value="admin" />
                <{prefix}:data name="company" value="0" />
                <{prefix}:data name="cid" value="8f1c-44" />
                <{prefix}:data name="log4jmachinename" value="HOST01" />
              </{prefix}:properties>
            </log4j:event>
            """;

        var entry = _parser.TryParse(xml, "s");

        Assert.NotNull(entry);
        // An explicit environment property wins over the machine name.
        Assert.Equal("GDB_TSE10_2189", entry!.Environment);
        Assert.Equal("admin", entry.Username);
        Assert.Equal("0", entry.Company);
        Assert.Equal("8f1c-44", entry.Cid);
    }

    [Fact]
    public void The_nested_diagnostic_context_stands_in_for_a_correlation_id()
    {
        var xml = $"""
            <log4j:event logger="A" level="INFO" timestamp="{Timestamp}">
              <log4j:message>x</log4j:message>
              <log4j:NDC>order-4711</log4j:NDC>
            </log4j:event>
            """;

        var entry = _parser.TryParse(xml, "s");

        Assert.NotNull(entry);
        Assert.Equal("order-4711", entry!.Cid);
    }

    [Fact]
    public void The_sender_is_used_as_the_source_when_the_event_does_not_name_an_application()
    {
        var entry = _parser.TryParse(Event(), "192.168.1.9:49512");

        Assert.NotNull(entry);
        Assert.Equal("192.168.1.9:49512", entry!.SourceFile);
    }

    /// <summary>The receive loop hands over whatever arrived, so nothing here may throw.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not xml at all")]
    [InlineData("<log4j:event logger=\"A\"")]                       // truncated
    [InlineData("<log4j:event><log4j:message>x</log4j:event>")]     // unbalanced
    [InlineData("<other:thing xmlns:other=\"urn:x\" />")]           // valid XML, not an event
    [InlineData("<log4j:event><log4j:message>&x;</log4j:message></log4j:event>")] // undefined entity
    public void Anything_that_is_not_a_parseable_event_is_ignored(string payload)
    {
        Assert.Null(_parser.TryParse(payload, "s"));
    }

    /// <summary>
    /// A DTD is the standard way to turn XML parsing into a denial of service, and this parser reads
    /// straight off a socket.
    /// </summary>
    [Fact]
    public void A_doctype_is_rejected_rather_than_expanded()
    {
        const string billionLaughs = """
            <!DOCTYPE lolz [<!ENTITY lol "lol"><!ENTITY lol2 "&lol;&lol;&lol;&lol;&lol;">]>
            <log4j:event logger="A" level="INFO" timestamp="0"><log4j:message>&lol2;</log4j:message></log4j:event>
            """;

        Assert.Null(_parser.TryParse(billionLaughs, "s"));
    }

    /// <summary>Low-cardinality values are shared, as they are for file lines.</summary>
    [Fact]
    public void Repeated_values_are_interned()
    {
        var first = _parser.TryParse(Event(logger: "Some.Long.Logger.Name"), "s");
        var second = _parser.TryParse(Event(logger: "Some.Long.Logger.Name"), "s");

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Same(first!.Logger, second!.Logger);
    }

    private static string Event(string level = "INFO", string logger = "A.B", string message = "x") =>
        $"""
        <log4j:event logger="{logger}" level="{level}" timestamp="{Timestamp}" thread="1">
          <log4j:message>{message}</log4j:message>
        </log4j:event>
        """;
}
