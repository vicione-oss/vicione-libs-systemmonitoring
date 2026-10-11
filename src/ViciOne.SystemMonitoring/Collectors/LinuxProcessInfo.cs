namespace ViciOne.SystemMonitoring.Collectors;

public class LinuxProcessInfo
{
    public required string Name { get; set; }
    public required string State { get; set; }
    public int Pid { get; set; }
    public int PPid { get; set; }
    public int Threads { get; set; }
    public double CpuUsage { get; set; }
    public required MemoryDetails MemoryDetails { get; set; }
    public long MemoryUsageKb { get; set; }
    public string? CmdLine { get; set; }

    /// <summary>
    /// When the process started, derived from the system uptime; <c>null</c> when the uptime could not be read.
    /// </summary>
    public DateTimeOffset? StartTime { get; set; }
}
