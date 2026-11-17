using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace LogAnalyzer.Services;

/// <summary>What this computer and this build are, read once.</summary>
public sealed record ComputerInfo(
    string MachineName, string OsDescription, string Architecture, string Processor,
    int LogicalProcessors, long TotalMemoryBytes, string Runtime, string AppVersion);

/// <summary>Free and total space of the drive that holds the loaded logs.</summary>
public sealed record DiskSnapshot(string Drive, long FreeBytes, long TotalBytes);

/// <summary>Power source. Null fields mean the machine does not report them (a desktop has no battery).</summary>
public sealed record PowerSnapshot(bool? OnBattery, int? BatteryPercent);

/// <summary>A network adapter that is up. <see cref="SpeedBitsPerSecond"/> is null when the adapter does not report it.</summary>
public sealed record AdapterSnapshot(string Name, long? SpeedBitsPerSecond);

/// <summary>The garbage collector's work since this app started.</summary>
public sealed record GcSnapshot(int Gen0Collections, int Gen1Collections, int Gen2Collections, TimeSpan TotalPause, long TotalAllocatedBytes);

/// <summary>
/// One sample of the computer and the app. The CPU percentages are null on the first sample, because a percentage
/// needs two samples to compare.
/// </summary>
public sealed record ResourceSnapshot(
    double? MachineCpuPercent, long MachineMemoryUsedBytes, long MachineMemoryTotalBytes,
    double? AppCpuPercent, long AppWorkingSetBytes, long AppPrivateBytes, long ManagedHeapBytes,
    int Threads, int Handles, TimeSpan AppUptime,
    TimeSpan ComputerUptime, DiskSnapshot? Disk, PowerSnapshot Power, IReadOnlyList<AdapterSnapshot> Adapters,
    GcSnapshot Gc);

/// <summary>
/// Reads CPU, memory, disk, power, network and garbage collector figures for the computer and for this app's
/// process. Keep one instance per page and call <see cref="Sample"/> at a steady interval; the CPU figures are the
/// change since the previous call. The app figures cover this process only, not the WebView2 processes it hosts.
/// </summary>
public sealed class ResourceSampler : IDisposable
{
    private readonly Process _process = Process.GetCurrentProcess();
    private DateTime _lastUtc;
    private TimeSpan _lastAppCpu;
    private bool _hasAppBaseline;
    private (ulong Idle, ulong Total)? _lastSystem;

    /// <summary>The percentage of one process's CPU time over a wall-clock interval, across all processors (0-100).</summary>
    public static double ProcessCpuPercent(TimeSpan cpuDelta, TimeSpan wallDelta, int processors)
    {
        if (wallDelta <= TimeSpan.Zero || processors <= 0) return 0;
        return Math.Clamp(cpuDelta.TotalSeconds / (wallDelta.TotalSeconds * processors) * 100, 0, 100);
    }

    /// <summary>The busy share of the machine's CPU time between two readings (0-100), or null when nothing elapsed.</summary>
    public static double? BusyPercent(ulong idleDelta, ulong totalDelta)
    {
        if (totalDelta == 0) return null;
        var busy = idleDelta >= totalDelta ? 0 : totalDelta - idleDelta;
        return busy * 100.0 / totalDelta;
    }

    public static ComputerInfo ReadComputerInfo() => new(
        Environment.MachineName,
        RuntimeInformation.OSDescription,
        RuntimeInformation.ProcessArchitecture.ToString(),
        ProcessorName(),
        Environment.ProcessorCount,
        GC.GetGCMemoryInfo().TotalAvailableMemoryBytes,
        RuntimeInformation.FrameworkDescription,
        AppVersion.Display);

    /// <summary>Samples everything. <paramref name="loadedPath"/> picks the drive to report: the one holding the loaded logs.</summary>
    public ResourceSnapshot Sample(string? loadedPath = null)
    {
        _process.Refresh();
        var nowUtc = DateTime.UtcNow;

        var appCpu = _process.TotalProcessorTime;
        double? appPercent = _hasAppBaseline
            ? ProcessCpuPercent(appCpu - _lastAppCpu, nowUtc - _lastUtc, Environment.ProcessorCount)
            : null;
        _lastAppCpu = appCpu;
        _lastUtc = nowUtc;
        _hasAppBaseline = true;

        double? machinePercent = null;
        if (OperatingSystem.IsWindows() && TryReadSystemTimes(out var idle, out var total))
        {
            if (_lastSystem is { } last) machinePercent = BusyPercent(idle - last.Idle, total - last.Total);
            _lastSystem = (idle, total);
        }

        var memory = GC.GetGCMemoryInfo();
        return new ResourceSnapshot(
            machinePercent,
            memory.MemoryLoadBytes,
            memory.TotalAvailableMemoryBytes,
            appPercent,
            _process.WorkingSet64,
            _process.PrivateMemorySize64,
            GC.GetTotalMemory(forceFullCollection: false),
            _process.Threads.Count,
            _process.HandleCount,
            nowUtc - _process.StartTime.ToUniversalTime(),
            TimeSpan.FromMilliseconds(Environment.TickCount64),
            ReadDisk(loadedPath),
            ReadPower(),
            ReadAdapters(),
            new GcSnapshot(
                GC.CollectionCount(0),
                GC.CollectionCount(1),
                GC.CollectionCount(2),
                GC.GetTotalPauseDuration(),
                GC.GetTotalAllocatedBytes(precise: false)));
    }

    public void Dispose() => _process.Dispose();

    /// <summary>The drive of the given path, or of the system drive when there is no path. UNC shares have no drive to measure.</summary>
    private static DiskSnapshot? ReadDisk(string? path)
    {
        try
        {
            var root = Path.GetPathRoot(string.IsNullOrWhiteSpace(path) ? Environment.SystemDirectory : path);
            if (string.IsNullOrEmpty(root) || root.StartsWith(@"\\", StringComparison.Ordinal)) return null;

            var drive = new DriveInfo(root);
            return drive.IsReady ? new DiskSnapshot(root, drive.AvailableFreeSpace, drive.TotalSize) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static IReadOnlyList<AdapterSnapshot> ReadAdapters()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up
                            && n.NetworkInterfaceType != NetworkInterfaceType.Loopback
                            && n.NetworkInterfaceType != NetworkInterfaceType.Tunnel)
                .Select(n =>
                {
                    long? speed = n.Speed > 0 ? n.Speed : null;
                    return new AdapterSnapshot(n.Name, speed);
                })
                .ToList();
        }
        catch (Exception)
        {
            return Array.Empty<AdapterSnapshot>();
        }
    }

    private static PowerSnapshot ReadPower()
    {
        if (!OperatingSystem.IsWindows()) return new PowerSnapshot(null, null);
        if (!GetSystemPowerStatus(out var status)) return new PowerSnapshot(null, null);

        // 0 = running on battery, 1 = plugged in, 255 = unknown. A battery percentage of 255 is unknown too.
        bool? onBattery = status.ACLineStatus switch { 0 => true, 1 => false, _ => null };
        int? percent = status.BatteryLifePercent == 255 ? null : status.BatteryLifePercent;
        return new PowerSnapshot(onBattery, percent);
    }

    [SupportedOSPlatform("windows")]
    private static bool TryReadSystemTimes(out ulong idle, out ulong total)
    {
        idle = 0;
        total = 0;
        if (!GetSystemTimes(out var idleTime, out var kernelTime, out var userTime)) return false;

        // Kernel time already includes idle time, so the total is kernel plus user.
        idle = idleTime.Ticks;
        total = kernelTime.Ticks + userTime.Ticks;
        return true;
    }

    private static string ProcessorName()
    {
        if (!OperatingSystem.IsWindows()) return "unknown";
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            return key?.GetValue("ProcessorNameString") as string ?? "unknown";
        }
        catch (Exception)
        {
            return "unknown";
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime
    {
        public uint Low;
        public uint High;

        public readonly ulong Ticks => ((ulong)High << 32) | Low;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PowerStatus
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public uint BatteryLifeTime;
        public uint BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [SupportedOSPlatform("windows")]
    private static extern bool GetSystemTimes(out FileTime idle, out FileTime kernel, out FileTime user);

    [DllImport("kernel32.dll", SetLastError = true)]
    [SupportedOSPlatform("windows")]
    private static extern bool GetSystemPowerStatus(out PowerStatus status);
}
