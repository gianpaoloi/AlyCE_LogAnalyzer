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
    /// Whether the first <c>LoadPanel</c> mounted this session has already synced <see
    /// cref="LoadPanelCollapsed"/> with whatever <c>LogStore</c> reports. Needed because a load
    /// started before any page exists to observe it — the MAUI host loading a file passed on the
    /// command line, from Explorer's right-click "Open with" — can finish before the panel
    /// subscribes to <c>DatasetChanged</c>, so the event that would normally collapse it never
    /// reaches a listener. Checked once so a later manual expand/collapse survives navigating
    /// between pages instead of being overridden back to whatever the store currently says.
    /// </summary>
    public bool LoadPanelSyncedOnce { get; set; }

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
    /// Whether File Live Watch keeps pulling new lines into the grid. Turning it off freezes what is on
    /// screen so a row can be read or clicked while the tail keeps buffering behind it.
    /// </summary>
    public bool LiveAutoScroll { get; set; } = true;

    // ---- Network Live Watch ----
    // Its own set rather than a share of the Live ones: the two pages buffer different sources and
    // are meant to be usable side by side, so a level filter set on one must not narrow the other.
    private IEnumerable<string> _networkLevels = Array.Empty<string>();
    private IEnumerable<string> _networkEnvironments = Array.Empty<string>();
    private IEnumerable<string> _networkCompanies = Array.Empty<string>();
    private IEnumerable<string> _networkColumns = LogColumns.DefaultKeys;

    public IEnumerable<string> NetworkLevels
    {
        get => _networkLevels;
        set => _networkLevels = NoneIfNull(value);
    }

    /// <summary>The Environment column, which for a log4j event holds the sending machine.</summary>
    public IEnumerable<string> NetworkEnvironments
    {
        get => _networkEnvironments;
        set => _networkEnvironments = NoneIfNull(value);
    }

    public IEnumerable<string> NetworkCompanies
    {
        get => _networkCompanies;
        set => _networkCompanies = NoneIfNull(value);
    }

    public string? NetworkText { get; set; }
    public string? NetworkLoggerPrefix { get; set; }

    public IEnumerable<string> NetworkColumns
    {
        get => _networkColumns;
        set => _networkColumns = NoneIfNull(value);
    }

    public bool NetworkShowLoggers { get; set; }

    /// <summary>UDP port to listen on. Matches the sample NLog target in the docs.</summary>
    public int NetworkPort { get; set; } = NetworkLogListener.DefaultPort;

    /// <summary>
    /// False binds the loopback address only — enough for a sender configured with
    /// <c>udp://127.0.0.1:9999</c>, and it leaves no port open to the network.
    /// </summary>
    public bool NetworkAllInterfaces { get; set; }

    public bool NetworkSettingsCollapsed { get; set; }

    /// <summary>See <see cref="LiveAutoScroll"/>; same idea, for the received events.</summary>
    public bool NetworkAutoScroll { get; set; } = true;
}
