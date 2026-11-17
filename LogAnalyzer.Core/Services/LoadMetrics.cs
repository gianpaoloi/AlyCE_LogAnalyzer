namespace LogAnalyzer.Services;

/// <summary>How the last load went: where it came from, how long it took and how much it read.</summary>
/// <param name="Bytes">Size of the source files; null when the source has no size (a Grafana download).</param>
public sealed record LoadMetrics(string Source, DateTime StartedUtc, TimeSpan Duration, long Rows, long? Bytes, bool Succeeded)
{
    /// <summary>Rows per second over the whole load (0 for an instant load).</summary>
    public double RowsPerSecond => Duration.TotalSeconds > 0 ? Rows / Duration.TotalSeconds : 0;
}

/// <summary>
/// Progress of the most recent Grafana download. <see cref="EndedUtc"/> is null while it still runs.
/// <see cref="Bytes"/> is the response body received so far (the JSON from the proxy).
/// </summary>
public sealed record GrafanaDownloadMetrics(DateTime StartedUtc, DateTime? EndedUtc, int Pages, long Lines, long Bytes)
{
    /// <summary>Lines per second from the start until the end, or until now while the download runs.</summary>
    public double LinesPerSecond(DateTime nowUtc)
    {
        var elapsed = (EndedUtc ?? nowUtc) - StartedUtc;
        return elapsed.TotalSeconds > 0 ? Lines / elapsed.TotalSeconds : 0;
    }

    /// <summary>Bytes per second from the start until the end, or until now while the download runs.</summary>
    public double BytesPerSecond(DateTime nowUtc)
    {
        var elapsed = (EndedUtc ?? nowUtc) - StartedUtc;
        return elapsed.TotalSeconds > 0 ? Bytes / elapsed.TotalSeconds : 0;
    }
}
