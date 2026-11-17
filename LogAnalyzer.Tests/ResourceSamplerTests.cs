using LogAnalyzer.Services;
using Xunit;

namespace LogAnalyzer.Tests;

public class ResourceSamplerTests
{
    [Fact]
    public void A_process_busy_on_one_of_four_processors_for_the_whole_interval_uses_a_quarter_of_the_machine()
    {
        // 10 s of CPU time over 10 s of wall time on a 4-processor machine: one processor fully busy.
        var percent = ResourceSampler.ProcessCpuPercent(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10), 4);

        Assert.Equal(25, percent, precision: 6);
    }

    [Fact]
    public void Process_cpu_is_capped_at_100_percent()
    {
        var percent = ResourceSampler.ProcessCpuPercent(TimeSpan.FromSeconds(50), TimeSpan.FromSeconds(10), 4);

        Assert.Equal(100, percent);
    }

    [Fact]
    public void Process_cpu_is_zero_when_no_time_has_passed()
    {
        Assert.Equal(0, ResourceSampler.ProcessCpuPercent(TimeSpan.FromSeconds(1), TimeSpan.Zero, 4));
    }

    [Fact]
    public void Machine_busy_share_is_the_part_of_the_total_that_was_not_idle()
    {
        // 300 of 1000 units idle: 70 % busy.
        Assert.Equal(70, ResourceSampler.BusyPercent(idleDelta: 300, totalDelta: 1000)!.Value, precision: 6);
    }

    [Fact]
    public void Machine_busy_share_is_unknown_when_no_time_has_passed()
    {
        Assert.Null(ResourceSampler.BusyPercent(idleDelta: 0, totalDelta: 0));
    }

    [Fact]
    public void Machine_busy_share_never_goes_negative()
    {
        Assert.Equal(0, ResourceSampler.BusyPercent(idleDelta: 1200, totalDelta: 1000)!.Value);
    }

    [Fact]
    public void The_first_sample_has_no_cpu_figures_and_the_second_has_them()
    {
        using var sampler = new ResourceSampler();

        var first = sampler.Sample();
        Thread.Sleep(50);
        var second = sampler.Sample();

        Assert.Null(first.AppCpuPercent);
        Assert.NotNull(second.AppCpuPercent);
        Assert.InRange(second.AppCpuPercent!.Value, 0, 100);
    }
}
