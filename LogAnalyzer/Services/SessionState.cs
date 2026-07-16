using LogAnalyzer.Models;

namespace LogAnalyzer.Services;

/// <summary>
/// Per-session UI state. Registered as a scoped service, so in Blazor Server it lives
/// for the duration of the user's SignalR circuit — filters set on one page survive
/// navigating to another page and back (reset only on a full reload / reconnect).
/// </summary>
public sealed class SessionState
{
    // ---- Explorer ----
    public IEnumerable<string> ExplorerLevels { get; set; } = new List<string>();
    public IEnumerable<string> ExplorerEnvironments { get; set; } = new List<string>();
    public IEnumerable<string> ExplorerCompanies { get; set; } = new List<string>();
    public string? ExplorerText { get; set; }
    public string? ExplorerLogger { get; set; }
    public string? ExplorerLoggerPrefix { get; set; }
    public IEnumerable<string> ExplorerColumns { get; set; } = LogColumns.DefaultKeys;
    public bool ExplorerShowLoggers { get; set; }

    // ---- Live ----
    public IEnumerable<string> LiveLevels { get; set; } = new List<string>();
    public IEnumerable<string> LiveEnvironments { get; set; } = new List<string>();
    public IEnumerable<string> LiveCompanies { get; set; } = new List<string>();
    public string? LiveText { get; set; }
    public string? LiveLoggerPrefix { get; set; }
    public IEnumerable<string> LiveColumns { get; set; } = LogColumns.DefaultKeys;
    public bool LiveShowLoggers { get; set; }
    public string? LivePath { get; set; }
    public bool LiveFromStart { get; set; }
}
