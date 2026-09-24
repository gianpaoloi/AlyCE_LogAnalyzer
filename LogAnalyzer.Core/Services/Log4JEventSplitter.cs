using System.Buffers;
using System.Text;

namespace LogAnalyzer.Services;

/// <summary>
/// Turns the bytes arriving from one sender into whole <c>&lt;log4j:event&gt;</c> fragments.
/// <para>
/// A datagram usually carries exactly one complete event, but it cannot be relied on: NLog's
/// network target splits a payload larger than <c>maxMessageSize</c> (65 000 bytes by default)
/// across several sends, so one event can arrive in pieces, and several small events can share a
/// send. Both cases are the same problem — find the event boundaries in a stream of bytes — so the
/// listener keeps one of these per sender and feeds everything through it.
/// </para>
/// <para>
/// Not thread-safe, and not meant to be: it is used from the receive loop only.
/// </para>
/// </summary>
public sealed class Log4JEventSplitter
{
    private const string StartTag = "<log4j:event";
    private const string EndTag = "</log4j:event>";

    /// <summary>
    /// Ceiling on an event still waiting for its closing tag. Generous next to the 65 000-byte
    /// default message size, but bounded: without it, a sender that streams anything *other* than
    /// log4j XML at the port would grow this buffer for as long as the app listens.
    /// </summary>
    public const int DefaultMaxBufferedChars = 4 * 1024 * 1024;

    private readonly int _maxBufferedChars;

    /// <summary>
    /// Kept across calls so a multi-byte character split across two datagrams still decodes —
    /// the same reason <see cref="LogWatcher"/> keeps one for its chunked file reads.
    /// </summary>
    private readonly Decoder _decoder = Encoding.UTF8.GetDecoder();

    private string _pending = "";

    public Log4JEventSplitter(int maxBufferedChars = DefaultMaxBufferedChars) =>
        _maxBufferedChars = maxBufferedChars;

    /// <summary>
    /// Bytes thrown away as unusable: a closing tag with nothing opening it (the head of a split
    /// payload was lost, which UDP allows), or a buffer that grew past
    /// <see cref="DefaultMaxBufferedChars"/> without ever closing an event.
    /// </summary>
    public long Discarded { get; private set; }

    /// <summary>True while a partially received event is still buffered.</summary>
    public bool HasPartial => _pending.Length > 0;

    /// <summary>Decodes and buffers one received payload.</summary>
    public void Append(ReadOnlySpan<byte> payload)
    {
        if (payload.IsEmpty) return;

        var max = _decoder.GetCharCount(payload, flush: false);
        var chars = ArrayPool<char>.Shared.Rent(max);
        try
        {
            var count = _decoder.GetChars(payload, chars, flush: false);
            if (count > 0) _pending = string.Concat(_pending, chars.AsSpan(0, count));
        }
        finally
        {
            ArrayPool<char>.Shared.Return(chars);
        }
    }

    /// <summary>
    /// Takes the next complete event, or returns false when the buffer holds no closing tag yet.
    /// Garbage between events is dropped here rather than handed on, so a caller can loop on this
    /// until it returns false and be sure it has seen everything that arrived.
    /// </summary>
    public bool TryTake(out string eventXml)
    {
        while (true)
        {
            var end = _pending.IndexOf(EndTag, StringComparison.Ordinal);
            if (end < 0)
            {
                // Nothing closed yet: either the rest is still on its way, or this is not an event
                // at all. Anything that cannot still turn into one goes now rather than sitting in
                // the buffer until the cap — that is what lets the listener report it as ignored
                // instead of looking idle.
                if (!CouldStillBecomeAnEvent() || _pending.Length > _maxBufferedChars)
                {
                    Discarded += _pending.Length;
                    _pending = "";
                }

                eventXml = "";
                return false;
            }

            var stop = end + EndTag.Length;
            var start = _pending.IndexOf(StartTag, StringComparison.Ordinal);
            var taken = start >= 0 && start < end ? _pending[start..stop] : null;

            // Everything up to the closing tag is consumed either way, including any preamble
            // before the opening tag (a stray BOM, or the tail of a dropped event).
            Discarded += taken is null ? stop : start;
            _pending = _pending[stop..];

            if (taken is not null)
            {
                eventXml = taken;
                return true;
            }
        }
    }

    /// <summary>
    /// Whether what is buffered could still grow into an event: it already holds an opening tag, or
    /// it ends part-way through one (a datagram may be cut anywhere, including inside
    /// <c>&lt;log4j:event</c>).
    /// </summary>
    private bool CouldStillBecomeAnEvent()
    {
        if (_pending.IndexOf(StartTag, StringComparison.Ordinal) >= 0) return true;

        var longestPrefix = Math.Min(_pending.Length, StartTag.Length - 1);
        for (var length = longestPrefix; length > 0; length--)
        {
            if (_pending.AsSpan(_pending.Length - length).SequenceEqual(StartTag.AsSpan(0, length)))
                return true;
        }

        return false;
    }

    /// <summary>Forgets any partial event, e.g. when the listener is restarted.</summary>
    public void Reset()
    {
        _pending = "";
        _decoder.Reset();
    }
}
