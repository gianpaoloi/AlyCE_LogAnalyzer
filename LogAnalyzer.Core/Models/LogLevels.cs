namespace LogAnalyzer.Models;

/// <summary>
/// Canonical handling of the level strings found in log lines. The grid filters, the chart
/// series, the badges and the stats buckets all have to agree that WARN and WARNING (and ERROR
/// and FATAL) mean the same thing, so the mapping lives here instead of being repeated — with
/// slightly different spellings — in each of them.
/// </summary>
public static class LogLevels
{
    public const string Debug = "DEBUG";
    public const string Info = "INFO";
    public const string Warn = "WARN";
    public const string Error = "ERROR";

    /// <summary>Anything that isn't one of the four known series (TRACE, VERBOSE, UNKNOWN…).</summary>
    public const string Other = "OTHER";

    /// <summary>
    /// Maps a raw level string onto one of the five series. Deliberately allocation-free: this
    /// runs once per rendered row per render (level badges, chart segments, stats buckets), and
    /// the <c>ToUpperInvariant()</c> it replaces allocated a string on every one of those calls.
    /// </summary>
    public static string Series(string? level)
    {
        if (string.IsNullOrEmpty(level)) return Other;
        if (Eq(level, Debug)) return Debug;
        if (Eq(level, Info)) return Info;
        if (Eq(level, Warn) || Eq(level, "WARNING")) return Warn;
        if (Eq(level, Error) || Eq(level, "FATAL")) return Error;
        return Other;
    }

    /// <summary>True for the level that <c>include DEBUG</c> filters out at parse time.</summary>
    public static bool IsDebug(string? level) => Eq(level, Debug);

    private static bool Eq(string? a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
