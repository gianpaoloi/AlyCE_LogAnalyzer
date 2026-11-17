using LogAnalyzer.Services;
using Xunit;

namespace LogAnalyzer.Tests;

public class LoadMetricsTests
{
    [Fact]
    public void Rows_per_second_is_rows_over_the_load_duration()
    {
        var load = new LoadMetrics("folder", DateTime.UtcNow, TimeSpan.FromSeconds(4), Rows: 10_000, Bytes: 1, Succeeded: true);

        Assert.Equal(2_500, load.RowsPerSecond, precision: 6);
    }

    [Fact]
    public void An_instant_load_has_no_rate()
    {
        var load = new LoadMetrics("folder", DateTime.UtcNow, TimeSpan.Zero, Rows: 10, Bytes: null, Succeeded: true);

        Assert.Equal(0, load.RowsPerSecond);
    }

    [Fact]
    public void A_finished_download_rates_its_lines_over_the_time_it_took()
    {
        var start = new DateTime(2026, 11, 17, 10, 0, 0, DateTimeKind.Utc);
        var download = new GrafanaDownloadMetrics(start, start.AddSeconds(10), Pages: 5, Lines: 5_000, Bytes: 2_000_000);

        // The rate stays fixed at the end, however late it is read.
        Assert.Equal(500, download.LinesPerSecond(start.AddMinutes(30)), precision: 6);
    }

    [Fact]
    public void A_finished_download_rates_its_bytes_over_the_time_it_took()
    {
        var start = new DateTime(2026, 11, 17, 10, 0, 0, DateTimeKind.Utc);
        var download = new GrafanaDownloadMetrics(start, start.AddSeconds(10), Pages: 5, Lines: 5_000, Bytes: 2_000_000);

        Assert.Equal(200_000, download.BytesPerSecond(start.AddMinutes(30)), precision: 6);
    }

    [Fact]
    public void A_running_download_rates_its_lines_up_to_now()
    {
        var start = new DateTime(2026, 11, 17, 10, 0, 0, DateTimeKind.Utc);
        var download = new GrafanaDownloadMetrics(start, null, Pages: 2, Lines: 1_000, Bytes: 400_000);

        Assert.Equal(250, download.LinesPerSecond(start.AddSeconds(4)), precision: 6);
    }
}
