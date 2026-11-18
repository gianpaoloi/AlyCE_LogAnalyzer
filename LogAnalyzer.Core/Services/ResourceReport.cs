using System.Globalization;

namespace LogAnalyzer.Services;

/// <summary>Everything the Resources page shows, gathered once so the page, the copy buttons and the feedback form read the same figures.</summary>
public sealed record ResourceReportInput(
    ComputerInfo Computer,
    ResourceSnapshot Snapshot,
    int LoadedRows,
    bool IsLoading,
    bool IsLoaded,
    LoadMetrics? LastLoad,
    GrafanaDownloadMetrics? Grafana,
    bool Watching,
    string? WatchedFile,
    long WatchedRows,
    bool Listening,
    string? Endpoint,
    long NetworkRows,
    long NetworkBytes,
    long NetworkIgnored);

/// <summary>One labelled figure in a panel.</summary>
public sealed record ResourceLine(string Label, string Value);

/// <summary>One of the three panels on the Resources page.</summary>
public sealed record ResourcePanel(string Title, IReadOnlyList<ResourceLine> Lines);

/// <summary>
/// Builds the text of the Resources panels: plain text for the Copy buttons and Markdown for the feedback form.
/// <para>
/// <c>locations</c> controls the fields that name this machine or a file: the computer name, the loaded file or folder,
/// the watched file and the listener address. The Copy buttons keep them. Feedback leaves them out, as its
/// privacy note promises.
/// </para>
/// </summary>
public static class ResourceReport
{
    public static ResourceReportInput Input(
        ComputerInfo computer, ResourceSnapshot snapshot, LogStore store, LogWatcher watcher, NetworkLogListener listener) =>
        new(computer, snapshot,
            store.Entries.Count, store.IsLoading, store.IsLoaded, store.LastLoad, store.Grafana,
            watcher.IsWatching, watcher.WatchedFile, watcher.TotalEntries,
            listener.IsListening, listener.Endpoint, listener.TotalEntries, listener.TotalBytes, listener.Ignored);

    public static ResourcePanel ComputerPanel(ResourceReportInput input, bool locations = true)
    {
        var s = input.Snapshot;
        var c = input.Computer;
        var lines = new List<ResourceLine>();

        if (locations) lines.Add(new("Machine", c.MachineName));
        lines.Add(new("Operating system", c.OsDescription));
        lines.Add(new("Architecture", c.Architecture));
        lines.Add(new("Processor", c.Processor));
        lines.Add(new("Logical processors", c.LogicalProcessors.ToString(CultureInfo.InvariantCulture)));
        lines.Add(new("Memory", $"{Format(c.TotalMemoryBytes)} total"));
        lines.Add(new("Runtime", c.Runtime));
        lines.Add(new("App version", c.AppVersion));
        lines.Add(new("Computer running for", Duration(s.ComputerUptime)));
        lines.Add(new("Disk of the loaded logs",
            s.Disk is { } disk
                ? $"{(locations ? disk.Drive + " " : "")}{Format(disk.FreeBytes)} free of {Format(disk.TotalBytes)}"
                : "not available"));
        lines.Add(new("Power", PowerText(s.Power)));

        var up = s.Adapters.Where(IsListedAsUp).ToList();
        if (up.Count == 0) lines.Add(new("Network adapters (up)", "none"));
        foreach (var adapter in up)
            lines.Add(new($"Adapter {adapter.Name}", $"{adapter.SpeedBitsPerSecond / 1_000_000:N0} Mb/s"));

        foreach (var adapter in s.Adapters.Where(a => a.SpeedBitsPerSecond is null))
            lines.Add(new($"Other adapter {adapter.Name}", $"{(adapter.IsUp ? "up" : "down")}, speed unknown"));

        return new ResourcePanel("This computer", lines);
    }

    public static ResourcePanel UsagePanel(ResourceReportInput input)
    {
        var s = input.Snapshot;
        return new ResourcePanel("Computer usage", new List<ResourceLine>
        {
            new("CPU", Percent(s.MachineCpuPercent)),
            new("Memory", $"{MemoryPercent(s):0} % ({Format(s.MachineMemoryUsedBytes)} of {Format(s.MachineMemoryTotalBytes)})"),
        });
    }

    public static ResourcePanel AppPanel(ResourceReportInput input, bool locations = true)
    {
        var s = input.Snapshot;
        var lines = new List<ResourceLine>
        {
            new("CPU", Percent(s.AppCpuPercent)),
            new("Memory (working set)", $"{Format(s.AppWorkingSetBytes)} ({AppMemoryPercent(s):0} % of total)"),
            new("Private memory", Format(s.AppPrivateBytes)),
            new("Managed heap", Format(s.ManagedHeapBytes)),
            new("Threads", s.Threads.ToString(CultureInfo.InvariantCulture)),
            new("Handles", s.Handles.ToString(CultureInfo.InvariantCulture)),
            new("Running for", Duration(s.AppUptime)),
            new("Collections (gen 0 / 1 / 2)",
                $"{s.Gc.Gen0Collections:N0} / {s.Gc.Gen1Collections:N0} / {s.Gc.Gen2Collections:N0}"),
            new("Time paused", PauseText(s.Gc.TotalPause)),
            new("Allocated since start", Format(s.Gc.TotalAllocatedBytes)),
            new("Rows loaded", input.LoadedRows.ToString("N0", CultureInfo.InvariantCulture)),
            new("Loaded logs state",
                input.IsLoading ? "loading" : input.IsLoaded ? "loaded" : "nothing loaded"),
        };

        if (input.LastLoad is { } load)
        {
            lines.Add(new("Last load", locations ? load.Source : "from a source"));
            lines.Add(new("Last load started", load.StartedUtc.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture)));
            lines.Add(new("Last load duration", Duration(load.Duration)));
            lines.Add(new("Rows per second", load.RowsPerSecond.ToString("N0", CultureInfo.InvariantCulture)));
            lines.Add(new("Bytes read", load.Bytes is long bytes ? Format(bytes) : "not known"));
            lines.Add(new("Last load result", load.Succeeded ? "ok" : "failed or cancelled"));
        }

        if (input.Grafana is { } download)
        {
            var now = DateTime.UtcNow;
            lines.Add(new("Grafana download state",
                download.EndedUtc is null ? "downloading" : $"finished at {download.EndedUtc.Value.ToLocalTime():HH:mm:ss}"));
            lines.Add(new("Grafana pages", download.Pages.ToString("N0", CultureInfo.InvariantCulture)));
            lines.Add(new("Grafana lines", download.Lines.ToString("N0", CultureInfo.InvariantCulture)));
            lines.Add(new("Grafana lines per second", download.LinesPerSecond(now).ToString("N0", CultureInfo.InvariantCulture)));
            lines.Add(new("Grafana downloaded", Format(download.Bytes)));
            lines.Add(new("Grafana download speed", $"{Format((long)download.BytesPerSecond(now))}/s"));
        }

        lines.Add(new("File watch",
            input.Watching ? (locations && input.WatchedFile is { } file ? $"watching {file}" : "on") : "off"));
        lines.Add(new("Rows read from the watched file", input.WatchedRows.ToString("N0", CultureInfo.InvariantCulture)));
        lines.Add(new("Network listener",
            input.Listening ? (locations && input.Endpoint is { } endpoint ? $"listening on {endpoint}" : "on") : "off"));
        lines.Add(new("Rows received over the network", input.NetworkRows.ToString("N0", CultureInfo.InvariantCulture)));
        lines.Add(new("Bytes received over the network", Format(input.NetworkBytes)));
        lines.Add(new("Datagrams ignored", input.NetworkIgnored.ToString("N0", CultureInfo.InvariantCulture)));

        return new ResourcePanel("This app", lines);
    }

    /// <summary>The three panels in page order.</summary>
    public static IReadOnlyList<ResourcePanel> Panels(ResourceReportInput input, bool locations = true) =>
        [ComputerPanel(input, locations), UsagePanel(input), AppPanel(input, locations)];

    /// <summary>A panel as plain text: its title, then one "Label: value" line per figure.</summary>
    public static string ToText(ResourcePanel panel) =>
        string.Join("\n", new[] { panel.Title }.Concat(panel.Lines.Select(l => $"{l.Label}: {l.Value}")));

    /// <summary>Panels as Markdown for an issue body: a heading, then a bold title and a bullet per figure.</summary>
    public static string ToMarkdown(IEnumerable<ResourcePanel> panels)
    {
        var sections = panels.Select(p =>
            $"**{p.Title}**\n" + string.Join("\n", p.Lines.Select(l => $"- {l.Label}: {l.Value}")));
        return "### Resources\n" + string.Join("\n\n", sections);
    }

    /// <summary>The share of the computer's memory in use (0-100).</summary>
    public static double MemoryPercent(ResourceSnapshot snapshot) =>
        snapshot.MachineMemoryTotalBytes <= 0
            ? 0
            : Math.Clamp(snapshot.MachineMemoryUsedBytes * 100.0 / snapshot.MachineMemoryTotalBytes, 0, 100);

    /// <summary>This app's working set as a share of the computer's total memory (0-100).</summary>
    public static double AppMemoryPercent(ResourceSnapshot snapshot) =>
        snapshot.MachineMemoryTotalBytes <= 0
            ? 0
            : Math.Clamp(snapshot.AppWorkingSetBytes * 100.0 / snapshot.MachineMemoryTotalBytes, 0, 100);

    public static string PowerText(PowerSnapshot power)
    {
        var battery = power.BatteryPercent is int percent ? $", battery {percent} %" : "";
        return power.OnBattery switch
        {
            true => "on battery" + battery,
            false => "plugged in" + battery,
            null => power.BatteryPercent is int only ? $"battery {only} %" : "not reported",
        };
    }

    public static string PauseText(TimeSpan pause) =>
        pause.TotalSeconds < 1 ? $"{pause.TotalMilliseconds:0} ms" : $"{pause.TotalSeconds:0.0} s";

    public static string Format(long bytes) => bytes switch
    {
        >= 1L << 30 => $"{bytes / (double)(1L << 30):0.0} GB",
        >= 1L << 20 => $"{bytes / (double)(1L << 20):0} MB",
        >= 1L << 10 => $"{bytes / (double)(1L << 10):0} KB",
        _ => $"{bytes} B",
    };

    public static string Duration(TimeSpan span) =>
        span.TotalDays >= 1 ? $"{(int)span.TotalDays} d {span.Hours} h"
        : span.TotalHours >= 1 ? $"{(int)span.TotalHours} h {span.Minutes} min"
        : $"{(int)span.TotalMinutes} min {span.Seconds} s";

    /// <summary>Same rule the page's adapter list uses: up, with a known speed.</summary>
    public static bool IsListedAsUp(AdapterSnapshot adapter) => adapter.IsUp && adapter.SpeedBitsPerSecond is not null;

    private static string Percent(double? percent) =>
        percent is double value ? $"{value:0} %" : "measuring";
}
