using System.Text;
using LogAnalyzer.Services;
using Xunit;

namespace LogAnalyzer.Tests;

public class Log4JEventSplitterTests
{
    private readonly Log4JEventSplitter _splitter = new();

    private static byte[] Utf8(string s) => Encoding.UTF8.GetBytes(s);

    private static string Event(string message) =>
        $"""<log4j:event logger="A" level="INFO" timestamp="0"><log4j:message>{message}</log4j:message></log4j:event>""";

    private List<string> TakeAll()
    {
        var taken = new List<string>();
        while (_splitter.TryTake(out var e)) taken.Add(e);
        return taken;
    }

    [Fact]
    public void One_datagram_with_one_event_yields_it()
    {
        _splitter.Append(Utf8(Event("one")));

        Assert.Equal(new[] { Event("one") }, TakeAll());
        Assert.False(_splitter.HasPartial);
    }

    /// <summary>The target sends without a delimiter, so several events can share one datagram.</summary>
    [Fact]
    public void Several_events_in_one_datagram_are_split()
    {
        _splitter.Append(Utf8(Event("one") + Event("two") + Event("three")));

        Assert.Equal(new[] { Event("one"), Event("two"), Event("three") }, TakeAll());
    }

    /// <summary>
    /// A payload over the target's maxMessageSize is sent in pieces, so an event can arrive across
    /// two datagrams. Until the closing tag turns up there is nothing to hand on.
    /// </summary>
    [Fact]
    public void An_event_split_across_datagrams_is_reassembled()
    {
        var whole = Event("split");
        var cut = whole.Length / 2;

        _splitter.Append(Utf8(whole[..cut]));
        Assert.Empty(TakeAll());
        Assert.True(_splitter.HasPartial);

        _splitter.Append(Utf8(whole[cut..]));
        Assert.Equal(new[] { whole }, TakeAll());
        Assert.False(_splitter.HasPartial);
    }

    /// <summary>
    /// The split can land inside a multi-byte character, which decoded per datagram would produce a
    /// replacement character in the middle of a message.
    /// </summary>
    [Fact]
    public void A_character_split_across_datagrams_still_decodes()
    {
        var bytes = Utf8(Event("caffè è wörth it"));
        // A cut inside the two-byte 'è' — found by walking to the first continuation byte.
        var cut = Array.FindIndex(bytes, b => (b & 0xC0) == 0x80) + 1;
        Assert.InRange(cut, 1, bytes.Length - 1);

        _splitter.Append(bytes.AsSpan(0, cut));
        _splitter.Append(bytes.AsSpan(cut));

        Assert.Equal(new[] { Event("caffè è wörth it") }, TakeAll());
    }

    /// <summary>Whatever sits between two events is not part of either.</summary>
    [Fact]
    public void Text_between_events_is_dropped()
    {
        _splitter.Append(Utf8("﻿" + Event("one") + "\r\n" + Event("two")));

        Assert.Equal(new[] { Event("one"), Event("two") }, TakeAll());
        Assert.True(_splitter.Discarded > 0);
    }

    /// <summary>
    /// UDP may lose the piece that carried the opening tag. The orphaned tail must not stall the
    /// events behind it — TryTake returning false has to mean "nothing complete buffered".
    /// </summary>
    [Fact]
    public void A_closing_tag_with_nothing_opening_it_is_skipped()
    {
        _splitter.Append(Utf8("<log4j:message>lost head</log4j:message></log4j:event>" + Event("next")));

        Assert.Equal(new[] { Event("next") }, TakeAll());
    }

    /// <summary>
    /// Something other than an NLogViewer target pointed at the port must not look like a partial
    /// event: it has to be dropped straight away, both so the buffer cannot grow and so the
    /// listener can report that it received something it could not use.
    /// </summary>
    [Fact]
    public void A_payload_that_cannot_become_an_event_is_dropped_at_once()
    {
        _splitter.Append(Utf8("GET / HTTP/1.1\r\n\r\n"));

        Assert.False(_splitter.TryTake(out _));
        Assert.False(_splitter.HasPartial);
        Assert.Equal(18, _splitter.Discarded);
    }

    /// <summary>
    /// …but a datagram cut inside the opening tag itself has to be held on to, or a split event
    /// would be thrown away right before its second half turned up.
    /// </summary>
    [Fact]
    public void A_payload_cut_inside_the_opening_tag_is_kept()
    {
        var whole = Event("cut in the tag");
        _splitter.Append(Utf8("<log4j:ev"));

        Assert.False(_splitter.TryTake(out _));
        Assert.True(_splitter.HasPartial);

        _splitter.Append(Utf8(whole["<log4j:ev".Length..]));
        Assert.Equal(new[] { whole }, TakeAll());
    }

    /// <summary>
    /// A sender that opens an event and never closes it would otherwise grow the buffer for as
    /// long as the app listens.
    /// </summary>
    [Fact]
    public void A_buffer_that_never_closes_an_event_is_eventually_dropped()
    {
        var splitter = new Log4JEventSplitter(maxBufferedChars: 64);

        splitter.Append(Utf8("<log4j:event" + new string('x', 200)));

        Assert.False(splitter.TryTake(out _));
        Assert.False(splitter.HasPartial);
        Assert.Equal(212, splitter.Discarded);
    }

    [Fact]
    public void Reset_forgets_a_partial_event()
    {
        _splitter.Append(Utf8(Event("half")[..20]));
        _splitter.Reset();
        _splitter.Append(Utf8(Event("whole")));

        Assert.Equal(new[] { Event("whole") }, TakeAll());
    }
}
