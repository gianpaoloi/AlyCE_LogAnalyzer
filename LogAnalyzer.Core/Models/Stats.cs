namespace LogAnalyzer.Models;

/// <summary>Aggregate statistics computed once after a load, for the overview page.</summary>
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

    /// <summary>
    /// Anything outside the four known levels — TRACE, a custom level, or a line with no <c>level</c>
    /// field at all. Without this the timeline quietly undercounted those entries, so its bars did
    /// not add up to the entry total.
    /// </summary>
    public int Other { get; set; }

    public int Total => Debug + Info + Warn + Error + Other;
}

/// <summary>
/// A timeline reduced to a chart-sized number of buckets, together with how many hours each of
/// them covers (so a caller can label the axis).
/// </summary>
public readonly record struct TimelineView(IReadOnlyList<TimeBucket> Buckets, int HoursPerBucket)
{
    /// <summary>Wording for the bucket size, e.g. "per hour", "per 6 hours", "per 2 days".</summary>
    public string Describe() => HoursPerBucket switch
    {
        1 => "per hour",
        < 24 => $"per {HoursPerBucket} hours",
        24 => "per day",
        _ => $"per {HoursPerBucket / 24} days",
    };

    /// <summary>Axis label for a bucket, dropping the time of day once buckets span whole days.</summary>
    public string Label(TimeBucket bucket) =>
        HoursPerBucket >= 24 ? bucket.Start.ToString("MM-dd") : bucket.Start.ToString("MM-dd HH:00");

    /// <summary>
    /// Groups <paramref name="hourly"/> into at most <paramref name="maxBuckets"/> buckets.
    /// <para>
    /// <see cref="LogStats.Timeline"/> is fixed hourly buckets over the whole span, so a month of
    /// logs is 720 of them and a year is 8 760 — more than a chart can draw without locking the
    /// browser up. Grouping is by elapsed time rather than list position, because the timeline only
    /// holds the hours that actually have entries; grouping by position would produce buckets of
    /// different widths.
    /// </para>
    /// </summary>
    public static TimelineView Downsample(IReadOnlyList<TimeBucket> hourly, int maxBuckets)
    {
        if (hourly.Count == 0) return new TimelineView(Array.Empty<TimeBucket>(), 1);

        var origin = hourly[0].Start;
        var spanHours = (hourly[^1].Start - origin).TotalHours + 1;
        var hoursPerBucket = Math.Max(1, (int)Math.Ceiling(spanHours / Math.Max(1, maxBuckets)));

        if (hoursPerBucket == 1) return new TimelineView(hourly, 1);

        var grouped = new List<TimeBucket>(Math.Min(hourly.Count, maxBuckets + 1));
        TimeBucket? current = null;

        foreach (var b in hourly)
        {
            var index = (long)((b.Start - origin).TotalHours / hoursPerBucket);
            var start = origin.AddHours(index * hoursPerBucket);

            if (current is null || current.Start != start)
            {
                current = new TimeBucket { Start = start };
                grouped.Add(current);
            }

            current.Debug += b.Debug;
            current.Info += b.Info;
            current.Warn += b.Warn;
            current.Error += b.Error;
            current.Other += b.Other;
        }

        return new TimelineView(grouped, hoursPerBucket);
    }
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
