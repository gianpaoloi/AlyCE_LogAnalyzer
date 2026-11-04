using System.Globalization;
using System.Runtime.InteropServices;

namespace LogAnalyzer.Services;

/// <summary>
/// The facts about the computer and the build that a maintainer asks for first when a bug cannot be
/// reproduced: which Windows, which runtime, how much memory, which locale, and when it happened here.
/// <para>
/// Deliberately leaves out the machine name, the user name, file paths and anything read from the logs.
/// Those identify a person or a customer more than they help reproduce a fault, and the report is
/// published to a public issue tracker.
/// </para>
/// </summary>
public static class MachineInfo
{
    /// <summary>
    /// The environment block for a bug report, as Markdown. With <paramref name="includeMachine"/> false
    /// only the app version is written.
    /// </summary>
    public static string Describe(string? appVersion, bool includeMachine = true, DateTimeOffset? now = null)
    {
        var version = string.IsNullOrWhiteSpace(appVersion) ? "unknown" : appVersion.Trim();
        var lines = new List<string> { "### Environment", $"- App version: {version}" };

        if (includeMachine)
        {
            var moment = now ?? DateTimeOffset.Now;
            var memory = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;

            lines.Add($"- OS: {RuntimeInformation.OSDescription}");
            lines.Add($"- Architecture: OS {RuntimeInformation.OSArchitecture}, process {RuntimeInformation.ProcessArchitecture}");
            lines.Add($"- Runtime: {RuntimeInformation.FrameworkDescription}");
            lines.Add($"- Processors: {Environment.ProcessorCount}, memory available to the app: {FormatBytes(memory)}");
            lines.Add($"- Culture: {CultureInfo.CurrentUICulture.Name}");
            lines.Add($"- Local time when filed: {moment.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture)}");
        }

        return string.Join("\n", lines);
    }

    /// <summary>Bytes as whole gibibytes or mebibytes, which is precise enough for a bug report.</summary>
    public static string FormatBytes(long bytes)
    {
        const double MiB = 1024d * 1024;
        const double GiB = MiB * 1024;
        if (bytes <= 0) return "unknown";
        // Invariant culture: the report is read by maintainers everywhere, and an it-IT machine would
        // otherwise write "16,0 GiB" into it.
        return bytes >= GiB
            ? (bytes / GiB).ToString("0.0", CultureInfo.InvariantCulture) + " GiB"
            : (bytes / MiB).ToString("0", CultureInfo.InvariantCulture) + " MiB";
    }
}
