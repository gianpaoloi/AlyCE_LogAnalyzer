using LogAnalyzer.Models;

namespace LogAnalyzer.Services;

/// <summary>
/// Per-session UI state. Registered as a scoped service, so in Blazor Server it lives
/// for the duration of the user's SignalR circuit — filters set on one page survive
/// navigating to another page and back (reset only on a full reload / reconnect).
/// </summary>
public sealed class SessionState
{
    // ---- Shared ----
    /// <summary>Collapsed state of the load-files panel, on the pages that allow collapsing it.</summary>
    public bool LoadPanelCollapsed { get; set; }

    /// <summary>
    /// Backing store for the multi-select filters below, which are never null.
    /// <para>
    /// Clicking a Radzen dropdown's clear (×) writes <c>default(TValue)</c> back through
    /// <c>@bind-Value</c> — and for <c>IEnumerable&lt;string&gt;</c> that is null, not an empty set
    /// (<c>DropDownBase&lt;T&gt;.ClearAll</c>). The pages then enumerate the value or call
    /// <c>Contains</c> on it, so removing a filter threw a <see cref="NullReferenceException"/> on
    /// the renderer. In Blazor Server an unhandled exception there ends the circuit, which takes
    /// every later click with it — the app looks frozen rather than broken, and the same click in
    /// the MAUI build kills the WebView's circuit just as dead.
    /// </para>
    /// <para>
    /// Coalescing on the way in keeps "no filter" as one representation — an empty set — for every
    /// reader, instead of asking each of the eight call sites to remember that null is possible.
    /// </para>
    /// </summary>
    private static IEnumerable<string> NoneIfNull(IEnumerable<string>? values) =>
        values ?? Array.Empty<string>();

    // ---- Explorer ----
    private IEnumerable<string> _explorerLevels = Array.Empty<string>();
    private IEnumerable<string> _explorerEnvironments = Array.Empty<string>();
    private IEnumerable<string> _explorerCompanies = Array.Empty<string>();
    private IEnumerable<string> _explorerColumns = LogColumns.DefaultKeys;

    public IEnumerable<string> ExplorerLevels
    {
        get => _explorerLevels;
        set => _explorerLevels = NoneIfNull(value);
    }

    public IEnumerable<string> ExplorerEnvironments
    {
        get => _explorerEnvironments;
        set => _explorerEnvironments = NoneIfNull(value);
    }

    public IEnumerable<string> ExplorerCompanies
    {
        get => _explorerCompanies;
        set => _explorerCompanies = NoneIfNull(value);
    }

    public string? ExplorerText { get; set; }
    public string? ExplorerLogger { get; set; }
    public string? ExplorerLoggerPrefix { get; set; }

    public IEnumerable<string> ExplorerColumns
    {
        get => _explorerColumns;
        set => _explorerColumns = NoneIfNull(value);
    }

    public bool ExplorerShowLoggers { get; set; }
    public bool ExplorerVolumeCollapsed { get; set; }

    /// <summary>Time window picked on the volume chart. Null means "the whole span".</summary>
    public TimeRange? ExplorerRange { get; set; }

    // ---- Live ----
    private IEnumerable<string> _liveLevels = Array.Empty<string>();
    private IEnumerable<string> _liveEnvironments = Array.Empty<string>();
    private IEnumerable<string> _liveCompanies = Array.Empty<string>();
    private IEnumerable<string> _liveColumns = LogColumns.DefaultKeys;

    public IEnumerable<string> LiveLevels
    {
        get => _liveLevels;
        set => _liveLevels = NoneIfNull(value);
    }

    public IEnumerable<string> LiveEnvironments
    {
        get => _liveEnvironments;
        set => _liveEnvironments = NoneIfNull(value);
    }

    public IEnumerable<string> LiveCompanies
    {
        get => _liveCompanies;
        set => _liveCompanies = NoneIfNull(value);
    }

    public string? LiveText { get; set; }
    public string? LiveLoggerPrefix { get; set; }

    public IEnumerable<string> LiveColumns
    {
        get => _liveColumns;
        set => _liveColumns = NoneIfNull(value);
    }

    public bool LiveShowLoggers { get; set; }
    public string? LivePath { get; set; }
    public bool LiveFromStart { get; set; }
    public bool LiveSettingsCollapsed { get; set; }

    /// <summary>
    /// Whether Live watch keeps pulling new lines into the grid. Turning it off freezes what is on
    /// screen so a row can be read or clicked while the tail keeps buffering behind it.
    /// </summary>
    public bool LiveAutoScroll { get; set; } = true;
}
