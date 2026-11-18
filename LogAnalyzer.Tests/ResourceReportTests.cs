using LogAnalyzer.Services;
using Xunit;

namespace LogAnalyzer.Tests;

public class ResourceReportTests
{
    private static ResourceReportInput Input() => new(
        new ComputerInfo("MY-PC", "Windows 11", "X64", "Test CPU", 8, 16L << 30, ".NET 10", "1.2.6"),
        new ResourceSnapshot(
            12.5, 4L << 30, 16L << 30, 3.0, 200L << 20, 250L << 20, 50L << 20, 30, 400, TimeSpan.FromMinutes(5),
            TimeSpan.FromHours(2), new DiskSnapshot(@"D:\", 1L << 30, 100L << 30), new PowerSnapshot(null, null),
            [new AdapterSnapshot("Ethernet", true, 1_000_000_000), new AdapterSnapshot("VPN", true, null)],
            new GcSnapshot(1, 2, 3, TimeSpan.FromMilliseconds(10), 1L << 30)),
        LoadedRows: 42, IsLoading: false, IsLoaded: true,
        LastLoad: new LoadMetrics(@"D:\logs\app.log", DateTime.UtcNow, TimeSpan.FromSeconds(2), 42, 1000, true),
        Grafana: null,
        Watching: true, WatchedFile: @"D:\logs\live.log", WatchedRows: 7,
        Listening: true, Endpoint: "192.168.1.5:9999", NetworkRows: 3, NetworkBytes: 900, NetworkIgnored: 1);

    [Fact]
    public void Feedback_text_leaves_out_the_computer_name_and_paths()
    {
        var text = ResourceReport.ToMarkdown(ResourceReport.Panels(Input(), locations: false));

        Assert.DoesNotContain("MY-PC", text);
        Assert.DoesNotContain(@"D:\logs", text);
        Assert.DoesNotContain("192.168.1.5", text);
        Assert.Contains("Rows loaded: 42", text);
        Assert.Contains("### Resources", text);
    }

    [Fact]
    public void Copied_text_keeps_the_locations_the_user_asked_for()
    {
        var text = ResourceReport.ToText(ResourceReport.ComputerPanel(Input()));

        Assert.Contains("Machine: MY-PC", text);
    }

    [Fact]
    public void Only_up_adapters_with_a_known_speed_are_listed_as_up()
    {
        var lines = ResourceReport.ComputerPanel(Input()).Lines.Select(l => l.Label).ToList();

        Assert.Contains("Adapter Ethernet", lines);
        Assert.DoesNotContain("Adapter VPN", lines);
        Assert.Contains("Other adapter VPN", lines);
    }
}
