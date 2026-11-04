using LogAnalyzer.Services;
using Xunit;

namespace LogAnalyzer.Tests;

public class MachineInfoTests
{
    private static readonly DateTimeOffset Moment = new(2026, 11, 4, 10, 12, 0, TimeSpan.FromHours(1));

    [Fact]
    public void Describe_includes_the_version_and_the_machine_facts()
    {
        var text = MachineInfo.Describe("1.2.3+abc", includeMachine: true, now: Moment);

        Assert.StartsWith("### Environment\n- App version: 1.2.3+abc", text);
        Assert.Contains("- OS: ", text);
        Assert.Contains("- Architecture: OS ", text);
        Assert.Contains("- Runtime: ", text);
        Assert.Contains("- Processors: ", text);
        Assert.Contains("- Culture: ", text);
        Assert.Contains("- Local time when filed: 2026-11-04 10:12:00 +01:00", text);
    }

    [Fact]
    public void Describe_without_machine_details_keeps_only_the_version()
    {
        var text = MachineInfo.Describe("1.2.3", includeMachine: false, now: Moment);

        Assert.Equal("### Environment\n- App version: 1.2.3", text);
    }

    [Fact]
    public void Describe_never_names_the_user_or_the_computer()
    {
        var text = MachineInfo.Describe("1.2.3", includeMachine: true, now: Moment);

        if (!string.IsNullOrEmpty(Environment.MachineName))
            Assert.DoesNotContain(Environment.MachineName, text, StringComparison.OrdinalIgnoreCase);
        if (!string.IsNullOrEmpty(Environment.UserName))
            Assert.DoesNotContain(Environment.UserName, text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Describe_with_no_version_says_unknown()
    {
        Assert.StartsWith("### Environment\n- App version: unknown", MachineInfo.Describe(null, includeMachine: false));
    }

    [Theory]
    [InlineData(0L, "unknown")]
    [InlineData(512L * 1024 * 1024, "512 MiB")]
    [InlineData(16L * 1024 * 1024 * 1024, "16.0 GiB")]
    public void Memory_is_formatted_in_whole_units(long bytes, string expected)
    {
        Assert.Equal(expected, MachineInfo.FormatBytes(bytes));
    }
}
