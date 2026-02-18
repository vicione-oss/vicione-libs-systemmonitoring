using Microsoft.Extensions.Logging;

namespace ViciOne.SystemMonitoring.Collectors;

internal static partial class ProcessMetricsCollectorLog
{
    [LoggerMessage(0, LogLevel.Warning, "Error processing process '{Pid}'.")]
    public static partial void LogErrorProcessingProcess(this ILogger<ProcessMetricsCollector> logger, int pid, Exception ex);
}
