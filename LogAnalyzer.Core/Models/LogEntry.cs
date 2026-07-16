namespace LogAnalyzer.Models;

/// <summary>
/// A single parsed log record. One JSON line in a *.log file maps to one instance.
/// </summary>
public sealed class LogEntry
{
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

    public bool HasStackTrace =>
        Message.Contains("stackTrace:", StringComparison.OrdinalIgnoreCase);

    /// <summary>First line of the message (stack trace and CRLF markers stripped).</summary>
    public string ShortMessage
    {
        get
        {
            var idx = Message.IndexOf("stackTrace:", StringComparison.OrdinalIgnoreCase);
            var head = idx >= 0 ? Message[..idx] : Message;
            var crlf = head.IndexOf("\\CRLF", StringComparison.Ordinal);
            if (crlf >= 0) head = head[..crlf];
            head = head.Trim();
            return head.Length > 300 ? head[..300] + "…" : head;
        }
    }

    /// <summary>The message with embedded "\CRLF" markers turned into real newlines (for stack traces).</summary>
    public string PrettyMessage => Message.Replace("\\CRLF", "\n");
}
