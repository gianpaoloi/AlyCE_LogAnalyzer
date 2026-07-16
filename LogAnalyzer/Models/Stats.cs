namespace LogAnalyzer.Models;

/// <summary>Aggregate statistics computed once after a load, for the overview and dashboard.</summary>
public sealed class LogStats
{
    public int TotalEntries { get; init; }
    public int FileCount { get; init; }
    public DateTime? FirstTime { get; init; }
    public DateTime? LastTime { get; init; }

    public Dictionary<string, int> ByLevel { get; init; } = new();
    public Dictionary<string, int> ByEnvironment { get; init; } = new();
    public Dictionary<string, int> ByLogger { get; init; } = new();

    /// <summary>Volume per hour bucket, split by level. Key = bucket start.</summary>
    public List<TimeBucket> Timeline { get; init; } = new();

    public TimeSpan? Span => (FirstTime is { } f && LastTime is { } l) ? l - f : null;
}

/// <summary>One time bucket in the volume timeline.</summary>
public sealed class TimeBucket
{
    public DateTime Start { get; init; }
    public int Debug { get; set; }
    public int Info { get; set; }
    public int Warn { get; set; }
    public int Error { get; set; }
    public int Total => Debug + Info + Warn + Error;
}

/// <summary>A cluster of similar WARN/ERROR messages, for triage.</summary>
public sealed class MessageGroup
{
    public string Level { get; init; } = "";
    public string Signature { get; init; } = "";
    public string SampleMessage { get; set; } = "";
    public int Count { get; set; }
    public DateTime FirstSeen { get; set; }
    public DateTime LastSeen { get; set; }
    public HashSet<string> Environments { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> Loggers { get; } = new(StringComparer.OrdinalIgnoreCase);
    public LogEntry? Sample { get; set; }
}
