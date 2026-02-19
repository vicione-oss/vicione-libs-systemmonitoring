using System.IO.Abstractions;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using ViciOne.SystemMonitoring.Metrics;

namespace ViciOne.SystemMonitoring.Collectors;

[SupportedOSPlatform("linux")]
public sealed class ProcessMetricsCollector(ILogger<ProcessMetricsCollector> logger, IFileSystem fileSystem) : IScheduledMetricsCollector, IPrepareMetricsCollector
{
    public int IntervalInMinutes { get; } = 1;
    public string Name => "process";

    private readonly Dictionary<int, long> _previousProcessCpu = [];
    private long _previousTotalCoreTime;

    public Task PrepareMetrics(CancellationToken cancellationToken)
    {
        try
        {
            var totalCoreTimeNow = GetTotalCoreTime();
            _previousTotalCoreTime = totalCoreTimeNow;

            GetAllProcesses(totalCoreTimeNow);
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
            var totalCoreTimeNow = GetTotalCoreTime();
            var processes = GetAllProcesses(totalCoreTimeNow);

            // cleanup old cache entries for processes that no longer exist
            var currentPids = processes.Select(p => p.Pid).ToHashSet();
            foreach (var pid in _previousProcessCpu.Keys.Where(pid => !currentPids.Contains(pid)).ToList())
                _previousProcessCpu.Remove(pid);

            _previousTotalCoreTime = totalCoreTimeNow;

            return Task.FromResult(new List<Metric>
            {
                new()
                {
                    Group = Name,
                    Name = "processes",
                    IntervalInMinutes = IntervalInMinutes,
                    Value = processes,
                    Unit = MetricUnit.None,
                }
            });
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Error collecting process metrics.", ex);
        }
    }

    private List<LinuxProcessInfo> GetAllProcesses(long totalCoreTimeNow)
    {
        var processes = new List<LinuxProcessInfo>();
        var procDir = "/proc";

        var totalCoreTimeDelta = totalCoreTimeNow - _previousTotalCoreTime;
        var systemUptime = GetSystemUptime();

        foreach (var dir in fileSystem.Directory.GetDirectories(procDir))
        {
            if (int.TryParse(fileSystem.Path.GetFileName(dir), out var pid))
            {
                try
                {
                    var processInfo = GetProcessInfo(dir, pid, systemUptime, totalCoreTimeDelta);
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
        long totalCoreTimeDelta)
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
                statFields = fileSystem.File.ReadAllText(statFile).Split(' ');
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

            double cpuUsage;
            var totalProcTimeNow = utime + stime;

            if (_previousProcessCpu.TryGetValue(pid, out var prevProcTime))
            {
                var deltaProc = totalProcTimeNow - prevProcTime;
                cpuUsage = totalCoreTimeDelta > 0
                    ? deltaProc / (double)totalCoreTimeDelta * 100.0
                    : 0;
            }
            else
            {
                var clockTicks = UnsafeNativeMethods.GetClockTicks();
                var seconds = systemUptime - (startTime / (double)clockTicks);
                cpuUsage = seconds > 0 ? totalProcTimeNow / clockTicks / seconds * 100.0 : 0;
            }

            _previousProcessCpu[pid] = totalProcTimeNow;

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
            };
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Error processing process '{pid}'.", ex);
        }
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
                if (parts.Length > 0 && double.TryParse(parts[0], out var uptime))
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
}
