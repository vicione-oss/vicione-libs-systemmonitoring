using System.Globalization;
using System.IO.Abstractions;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using ViciOne.SystemMonitoring.Metrics;

namespace ViciOne.SystemMonitoring.Collectors;

[SupportedOSPlatform("linux")]
public sealed class ProcessMetricsCollector(ILogger<ProcessMetricsCollector> logger, IFileSystem fileSystem) : IScheduledMetricsCollector, IPrepareMetricsCollector, ILiveMetricsCollector
{
    private readonly CpuTimes _intervalBaseline = new();

    public int IntervalInMinutes { get; } = 1;
    public string Name => "process";

    public Task PrepareMetrics(CancellationToken cancellationToken)
    {
        try
        {
            var totalCoreTimeNow = GetTotalCoreTime();
            _intervalBaseline.TotalCoreTime = totalCoreTimeNow;
            GetAllProcesses(totalCoreTimeNow, _intervalBaseline);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Error preparing process metrics.", ex);
        }

        return Task.CompletedTask;
    }

    public Task<List<Metric>> CollectMetrics(int interval, DateTimeOffset cycleStartTime, CancellationToken cancellationToken)
    {
        try
        {
            return Task.FromResult(Collect(_intervalBaseline, IntervalInMinutes));
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Error collecting process metrics.", ex);
        }
    }

    public Task<List<Metric>> CollectLiveMetrics(LiveWindow window, CancellationToken cancellationToken)
    {
        try
        {
            return Task.FromResult(Collect(window.Baseline<CpuTimes>(this), 0));
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Error collecting live process metrics.", ex);
        }
    }

    private List<Metric> Collect(CpuTimes baseline, int intervalInMinutes)
    {
        var totalCoreTimeNow = GetTotalCoreTime();
        var processes = GetAllProcesses(totalCoreTimeNow, baseline);

        baseline.Forget(processes.Select(p => p.Pid).ToHashSet());
        baseline.TotalCoreTime = totalCoreTimeNow;

        return
        [
            new()
            {
                Group = Name,
                Name = "processes",
                IntervalInMinutes = intervalInMinutes,
                Value = processes,
                Unit = MetricUnit.None,
            }
        ];
    }

    private List<LinuxProcessInfo> GetAllProcesses(long totalCoreTimeNow, CpuTimes baseline)
    {
        var processes = new List<LinuxProcessInfo>();
        var procDir = "/proc";

        var totalCoreTimeDelta = totalCoreTimeNow - baseline.TotalCoreTime;
        var systemUptime = GetSystemUptime();

        foreach (var dir in fileSystem.Directory.GetDirectories(procDir))
        {
            if (int.TryParse(fileSystem.Path.GetFileName(dir), out var pid))
            {
                try
                {
                    var processInfo = GetProcessInfo(dir, pid, systemUptime, totalCoreTimeDelta, baseline);
                    if (processInfo != null)
                        processes.Add(processInfo);
                }
                catch (Exception ex)
                {
                    logger.LogErrorProcessingProcess(pid, ex);
                }
            }
        }

        return processes;
    }

    private LinuxProcessInfo? GetProcessInfo(
        string procDir,
        int pid,
        double systemUptime,
        long totalCoreTimeDelta,
        CpuTimes baseline)
    {
        try
        {
            // Read all required files at once
            var statusFile = fileSystem.Path.Combine(procDir, "status");
            var statFile = fileSystem.Path.Combine(procDir, "stat");
            var cmdlineFile = fileSystem.Path.Combine(procDir, "cmdline");

            if (!fileSystem.File.Exists(statusFile) || !fileSystem.File.Exists(statFile))
                return null;

            string[] statusLines;
            string[] statFields;
            string? cmdline = null;

            try
            {
                statusLines = fileSystem.File.ReadAllLines(statusFile);
                statFields = SplitStatFields(fileSystem.File.ReadAllText(statFile));
                if (fileSystem.File.Exists(cmdlineFile))
                    cmdline = fileSystem.File.ReadAllText(cmdlineFile).Replace('\0', ' ').Trim();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Error reading process files for PID '{pid}'.", ex);
            }

            // Extract memory details
            var memoryDetails = GetMemoryDetails(statusLines);

            // Extract CPU usage
            if (statFields.Length <= 21)
                throw new InvalidOperationException($"Insufficient data in stat file for process '{pid}'.");

            var utime = long.TryParse(statFields[13], out var u) ? u : 0;
            var stime = long.TryParse(statFields[14], out var s) ? s : 0;
            var startTime = long.TryParse(statFields[21], out var starttimeVal) ? starttimeVal : 0;

            if (utime < 0 || stime < 0 || startTime < 0)
                throw new InvalidOperationException($"Invalid CPU timing values for PID '{pid}'.");

            var totalProcTimeNow = utime + stime;
            var clockTicks = UnsafeNativeMethods.GetClockTicks();
            var runningSeconds = systemUptime - (startTime / (double)clockTicks);
            var cpuUsage = CpuShare(totalProcTimeNow, baseline.ProcessTimes.TryGetValue(pid, out var prevProcTime) ? prevProcTime : null,
                totalCoreTimeDelta, runningSeconds, clockTicks, Environment.ProcessorCount);

            baseline.ProcessTimes[pid] = totalProcTimeNow;

            return new LinuxProcessInfo
            {
                Name = GetValueFromStatusLines(statusLines, "Name") ?? string.Empty,
                State = GetValueFromStatusLines(statusLines, "State") ?? string.Empty,
                Pid = pid,
                PPid = int.TryParse(GetValueFromStatusLines(statusLines, "PPid"), out var ppid) ? ppid : 0,
                Threads = int.TryParse(GetValueFromStatusLines(statusLines, "Threads"), out var threads) ? threads : 0,
                CpuUsage = cpuUsage,
                MemoryDetails = memoryDetails,
                MemoryUsageKb = memoryDetails.VmRSSKb + memoryDetails.VmSwapKb,
                CmdLine = cmdline,
                StartTime = systemUptime > 0 ? DateTimeOffset.UtcNow - TimeSpan.FromSeconds(Math.Max(runningSeconds, 0)) : null,
            };
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Error processing process '{pid}'.", ex);
        }
    }

    /// <summary>
    /// The share of all cores a process used: since the previous reading, or on its first reading on average since it started.
    /// </summary>
    /// <param name="processTicks">The process's user and system time, in clock ticks.</param>
    /// <param name="previousProcessTicks">The same at the previous reading; <see langword="null"/> on the first reading.</param>
    /// <param name="totalCoreTicksDelta">The time of all cores since the previous reading, in clock ticks.</param>
    /// <param name="runningSeconds">How long the process has been running.</param>
    /// <param name="clockTicks">Clock ticks per second.</param>
    /// <param name="cores">The number of cores.</param>
    /// <returns>The share in percent, 0 to 100.</returns>
    internal static double CpuShare(long processTicks, long? previousProcessTicks, long totalCoreTicksDelta, double runningSeconds,
        int clockTicks, int cores)
    {
        if (previousProcessTicks is { } previous)
            return totalCoreTicksDelta > 0 ? (processTicks - previous) / (double)totalCoreTicksDelta * 100.0 : 0;

        return runningSeconds > 0 && clockTicks > 0 && cores > 0
            ? processTicks / (double)clockTicks / runningSeconds / cores * 100.0
            : 0;
    }

    /// <summary>
    /// The fields of /proc/[pid]/stat by their position; the name in field 1 is in parentheses and may contain spaces and
    /// parentheses itself.
    /// </summary>
    internal static string[] SplitStatFields(string stat)
    {
        var nameStart = stat.IndexOf('(', StringComparison.Ordinal);
        var nameEnd = stat.LastIndexOf(')');
        if (nameStart < 0 || nameEnd < nameStart)
            return stat.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        return [stat[..nameStart].Trim(), stat[nameStart..(nameEnd + 1)], .. stat[(nameEnd + 1)..].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)];
    }

    private static MemoryDetails GetMemoryDetails(string[] statusLines)
    {
        try
        {
            var memoryDetails = new MemoryDetails
            {
                VmSizeKb = ParseMemoryValue(GetValueFromStatusLines(statusLines, "VmSize")), // Virtual memory size = total program size
                VmRSSKb = ParseMemoryValue(GetValueFromStatusLines(statusLines, "VmRSS")),  // Resident set size = non-swapped physical memory
                VmPeakKb = ParseMemoryValue(GetValueFromStatusLines(statusLines, "VmPeak")), // Peak resident set size ("high water mark")
                VmSwapKb = ParseMemoryValue(GetValueFromStatusLines(statusLines, "VmSwap"))  // Swapped memory size
            };
            if (!memoryDetails.IsValid())
            {
                throw new InvalidOperationException("Memory details contain invalid values.");
            }
            return memoryDetails;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Error extracting memory details.", ex);
        }
    }

    private static string? GetValueFromStatusLines(string[] lines, string key)
    {
        try
        {
            foreach (var line in lines)
            {
                if (line.StartsWith(key, StringComparison.InvariantCulture))
                    return line.Split(':')[1].Trim();
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Error extracting key '{key}' from status lines.", ex);
        }

        return null;
    }

    private static long ParseMemoryValue(string? value)
    {
        try
        {
            if (string.IsNullOrEmpty(value))
                return 0;

            if (long.TryParse(value.Split(' ')[0], out var result))
                return result;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Error parsing memory value.", ex);
        }

        return 0;
    }

    private double GetSystemUptime()
    {
        try
        {
            var uptimeLine = fileSystem.File.ReadLines("/proc/uptime").FirstOrDefault();
            if (uptimeLine != null)
            {
                var parts = uptimeLine.Split(' ');
                if (parts.Length > 0 && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var uptime))
                {
                    if (uptime < 0)
                        throw new InvalidOperationException("Invalid system uptime value.");
                    return uptime;
                }
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Error reading system uptime.", ex);
        }

        return 0;
    }

    private long GetTotalCoreTime()
    {
        try
        {
            var line = fileSystem.File.ReadLines("/proc/stat")
                .First(line => line.StartsWith("cpu", StringComparison.InvariantCulture));
            var fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            if (fields[0] != "cpu")
                throw new InvalidOperationException("Invalid CPU line format.");

            try
            {
                return fields.Skip(1).Take(10).Sum(field => long.TryParse(field, out var value) ? value : 0);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Error parsing core stats.", ex);
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Error reading core stats.", ex);
        }
    }

    /// <summary>
    /// CPU times of the processes and of all cores at one reading, the reference for the next difference.
    /// </summary>
    private sealed class CpuTimes
    {
        public Dictionary<int, long> ProcessTimes { get; } = [];

        public long TotalCoreTime { get; set; }

        public void Forget(HashSet<int> runningPids)
        {
            foreach (var pid in ProcessTimes.Keys.Where(pid => !runningPids.Contains(pid)).ToList())
                ProcessTimes.Remove(pid);
        }
    }
}
