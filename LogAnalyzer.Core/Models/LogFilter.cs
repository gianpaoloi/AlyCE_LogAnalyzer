namespace LogAnalyzer.Models;

/// <summary>Criteria used to filter log entries in the explorer and the live view.</summary>
public sealed class LogFilter
{
    /// <summary>Selected levels. Empty means "all levels".</summary>
    public HashSet<string> Levels { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Selected environments. Empty means "all environments".</summary>
    public HashSet<string> Environments { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Selected companies. Empty means "all companies".</summary>
    public HashSet<string> Companies { get; } = new(StringComparer.OrdinalIgnoreCase);

    public string? Text { get; set; }
    public string? Logger { get; set; }

    /// <summary>Prefix filter from the logger tree: matches the exact logger or any child under it.</summary>
    public string? LoggerPrefix { get; set; }

    public string? Environment { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }

    public bool Matches(LogEntry e)
    {
        if (Levels.Count > 0 && !Levels.Contains(e.Level)) return false;
        if (From is { } f && e.Time < f) return false;
        if (To is { } t && e.Time > t) return false;

        if (!string.IsNullOrWhiteSpace(Environment) &&
            !string.Equals(e.Environment, Environment, StringComparison.OrdinalIgnoreCase))
            return false;

        if (Environments.Count > 0 && (e.Environment is null || !Environments.Contains(e.Environment)))
            return false;

        if (Companies.Count > 0 && (e.Company is null || !Companies.Contains(e.Company)))
            return false;

        if (!string.IsNullOrWhiteSpace(Logger) &&
            (e.Logger is null || e.Logger.IndexOf(Logger, StringComparison.OrdinalIgnoreCase) < 0))
            return false;

        if (!string.IsNullOrWhiteSpace(LoggerPrefix) && !MatchesPrefix(e.Logger, LoggerPrefix))
            return false;

        if (!string.IsNullOrWhiteSpace(Text) &&
            e.Message.IndexOf(Text, StringComparison.OrdinalIgnoreCase) < 0)
            return false;

        return true;
    }

    /// <summary>True if <paramref name="logger"/> equals the prefix or is a descendant (prefix + ".").</summary>
    public static bool MatchesPrefix(string? logger, string prefix)
    {
        if (logger is null) return false;
        if (!logger.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
        return logger.Length == prefix.Length || logger[prefix.Length] == '.';
    }
}
