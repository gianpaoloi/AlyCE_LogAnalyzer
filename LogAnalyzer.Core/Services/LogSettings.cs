using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace LogAnalyzer.Services;

/// <summary>
/// Settings for the application's own troubleshooting log (<see cref="FileLoggerProvider"/>), read from the
/// <c>LogAnalyzer:Log</c> section of <c>appsettings.json</c>, which sits beside the executable.
/// <para>
/// Every key is optional. Anything absent, empty or unparseable falls back to the default, so a
/// mistyped value never stops the app from starting.
/// </para>
/// </summary>
public sealed record LogSettings
{
    /// <summary>Configuration section the hosts read these from.</summary>
    public const string Section = "LogAnalyzer:Log";

    /// <summary>Daily files older than this are deleted when the log starts.</summary>
    public const int DefaultRetentionDays = 14;

    /// <summary>The level used when none is configured: failures only.</summary>
    public const LogLevel DefaultLevel = LogLevel.Error;

    /// <summary>Upper bound for <see cref="RetentionDays"/>, so a typo cannot keep files for decades.</summary>
    public const int MaxRetentionDays = 365;

    /// <summary>Whether the log is written at all. When false nothing is created or written.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// Where the daily files go. <c>null</c> means a <c>logs</c> folder beside the executable. A relative
    /// path is taken from the executable's folder; an absolute path is used as it is.
    /// </summary>
    public string? Folder { get; init; }

    /// <summary>
    /// The least severe entry that is recorded. Defaults to <see cref="LogLevel.Error"/>, so a normal run
    /// writes only failures; set <see cref="LogLevel.Debug"/> to record everything.
    /// </summary>
    public LogLevel Level { get; init; } = DefaultLevel;

    /// <summary>How many days of daily files to keep, between 1 and <see cref="MaxRetentionDays"/>.</summary>
    public int RetentionDays { get; init; } = DefaultRetentionDays;

    /// <summary>
    /// Reads the <c>LogAnalyzer:Log</c> section. Uses the indexer rather than the binder, matching
    /// <see cref="Updates.UpdateOptions.FromConfiguration"/>, so no extra package is needed.
    /// </summary>
    public static LogSettings FromConfiguration(IConfiguration? configuration)
    {
        if (configuration is null) return new LogSettings();

        var defaults = new LogSettings();

        var folder = configuration[$"{Section}:Folder"];

        return new LogSettings
        {
            Enabled = bool.TryParse(configuration[$"{Section}:Enabled"], out var enabled) ? enabled : defaults.Enabled,
            Folder = string.IsNullOrWhiteSpace(folder) ? null : folder.Trim(),
            Level = ParseLevel(configuration[$"{Section}:Level"], defaults.Level),
            RetentionDays = int.TryParse(configuration[$"{Section}:RetentionDays"], out var days)
                ? Math.Clamp(days, 1, MaxRetentionDays)
                : defaults.RetentionDays,
        };
    }

    /// <summary>
    /// Accepts the standard level names (<c>Trace</c>, <c>Debug</c>, <c>Information</c>, <c>Warning</c>,
    /// <c>Error</c>, <c>Critical</c>, <c>None</c>), ignoring case, plus the short forms <c>Info</c> and <c>Warn</c>.
    /// </summary>
    public static LogLevel ParseLevel(string? value, LogLevel fallback)
    {
        var name = value?.Trim().ToLowerInvariant();
        switch (name)
        {
            case "info": return LogLevel.Information;
            case "warn": return LogLevel.Warning;
        }

        return Enum.TryParse<LogLevel>(name, ignoreCase: true, out var level) ? level : fallback;
    }

    public override string ToString() =>
        $"enabled={Enabled}, folder={Folder ?? "(default)"}, level={Level}, retentionDays={RetentionDays}";
}
