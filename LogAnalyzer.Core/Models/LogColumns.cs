namespace LogAnalyzer.Models;

/// <summary>An optional grid column mapped to a field of the JSON log structure.</summary>
public sealed record LogColumn(string Key, string Title, Func<LogEntry, string?> Value, string Width);

/// <summary>
/// Optional columns that can be added to the Explorer / Live grids on top of the
/// always-visible Time / Level / Environment / Message columns. Each maps to a
/// property of <see cref="LogEntry"/> (i.e. a field of the JSON log line).
/// </summary>
public static class LogColumns
{
    public static readonly IReadOnlyList<LogColumn> Optional = new List<LogColumn>
    {
        new("Username",   "Username",    e => e.Username,   "150px"),
        new("ThreadId",   "Thread",      e => e.ThreadId,   "90px"),
        new("Cid",        "Cid",         e => e.Cid,        "230px"),
        new("Logger",     "Logger",      e => e.Logger,     "260px"),
        new("SourceFile", "Source file", e => e.SourceFile, "200px"),
    };

    /// <summary>Columns shown by default (in addition to the fixed ones).</summary>
    public static IEnumerable<string> DefaultKeys => new List<string>();
}
