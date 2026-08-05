namespace LogAnalyzer.Models;

/// <summary>
/// An inclusive time window, as selected on the volume chart. Kept as one value so a
/// half-set range (a start without an end) can't be represented.
/// </summary>
public readonly record struct TimeRange(DateTime From, DateTime To)
{
    public bool Contains(DateTime t) => t >= From && t <= To;

    /// <summary>True if the window overlaps the half-open bucket <c>[start, start + length)</c>.</summary>
    public bool Overlaps(DateTime start, TimeSpan length) => start <= To && start + length > From;

    public override string ToString() =>
        From.Date == To.Date
            ? $"{From:MM/dd HH:mm:ss} → {To:HH:mm:ss}"
            : $"{From:MM/dd HH:mm:ss} → {To:MM/dd HH:mm:ss}";
}
