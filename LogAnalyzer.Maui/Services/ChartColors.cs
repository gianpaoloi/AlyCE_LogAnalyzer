namespace LogAnalyzer.Services;

/// <summary>Consistent colors for log levels across charts and badges.</summary>
public static class ChartColors
{
    public static string Level(string level) => level.ToUpperInvariant() switch
    {
        "ERROR" or "FATAL" => "#d64545",
        "WARN" or "WARNING" => "#e0a458",
        "INFO" => "#4c78a8",
        "DEBUG" => "#8a8a8a",
        _ => "#b07fd6",
    };
}
