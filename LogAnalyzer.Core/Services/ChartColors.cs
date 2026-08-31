using LogAnalyzer.Models;

namespace LogAnalyzer.Services;

/// <summary>Consistent colors for log levels across charts and badges.</summary>
public static class ChartColors
{
    /// <summary>
    /// Colour for a level. Goes through <see cref="LogLevels.Series"/> so WARN/WARNING and
    /// ERROR/FATAL can't drift apart, and so the lookup no longer allocates an uppercased copy of
    /// the level on every rendered row.
    /// </summary>
    public static string Level(string? level) => LogLevels.Series(level) switch
    {
        LogLevels.Error => "#d64545",
        LogLevels.Warn => "#e0a458",
        LogLevels.Info => "#4c78a8",
        LogLevels.Debug => "#8a8a8a",
        _ => "#b07fd6",
    };
}
