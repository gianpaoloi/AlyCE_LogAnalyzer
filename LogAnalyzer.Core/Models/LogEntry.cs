using LogAnalyzer.Services;

namespace LogAnalyzer.Models;

/// <summary>
/// A single parsed log record. One JSON line in a *.log file maps to one instance.
/// <para>
/// The four values derived from <see cref="Message"/> are computed on first use and cached: each
/// one re-scanned the whole message, and they are read once per visible row per render, plus once
/// per entry on every triage recomputation. The caches are written without a lock — two threads
/// racing produce the same value, so the worst case is computing it twice.
/// </para>
/// </summary>
public sealed class LogEntry
{
    /// <summary>Marker that separates the human part of a message from its stack trace.</summary>
    private const string StackTraceMarker = "stackTrace:";

    /// <summary>What the emitter writes instead of a real line break.</summary>
    private const string CrLfMarker = "\\CRLF";

    private const int NotComputed = -2;

    public DateTime Time { get; init; }
    public string Level { get; init; } = "";
    public string ThreadId { get; init; } = "";
    public string? Environment { get; init; }
    public string? Username { get; init; }
    public string? Company { get; init; }
    public string? Cid { get; init; }
    public string Message { get; init; } = "";
    public string Logger { get; init; } = "";

    /// <summary>Name of the source .log file (without directory).</summary>
    public string SourceFile { get; init; } = "";

    private int _stackIndex = NotComputed;
    private string? _shortMessage;
    private string? _signature;

    /// <summary>Index of the stack-trace marker in <see cref="Message"/>, or -1. Computed once.</summary>
    private int StackIndex
    {
        get
        {
            var idx = _stackIndex;
            if (idx == NotComputed)
                _stackIndex = idx = Message.IndexOf(StackTraceMarker, StringComparison.OrdinalIgnoreCase);
            return idx;
        }
    }

    public bool HasStackTrace => StackIndex >= 0;

    /// <summary>First line of the message (stack trace and CRLF markers stripped).</summary>
    public string ShortMessage => _shortMessage ??= BuildShortMessage();

    /// <summary>
    /// Stable clustering key for this message, with the varying parts masked. Cached because
    /// triage recomputes its clusters on every filter change and this is the expensive part.
    /// </summary>
    public string Signature => _signature ??= MessageNormalizer.Signature(HumanPart());

    /// <summary>The message with embedded "\CRLF" markers turned into real newlines (for stack traces).</summary>
    public string PrettyMessage => Message.Replace(CrLfMarker, "\n");

    /// <summary>The message up to the stack trace and the first CRLF marker — what a human wrote.</summary>
    private ReadOnlySpan<char> HumanPart()
    {
        var head = StackIndex >= 0 ? Message.AsSpan(0, StackIndex) : Message.AsSpan();
        var crlf = head.IndexOf(CrLfMarker.AsSpan(), StringComparison.Ordinal);
        return crlf >= 0 ? head[..crlf] : head;
    }

    private string BuildShortMessage()
    {
        var head = HumanPart().Trim();
        return head.Length > 300 ? string.Concat(head[..300], "…") : head.ToString();
    }
}
