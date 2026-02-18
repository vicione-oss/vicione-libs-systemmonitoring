using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ViciOne.SystemMonitoring.Collectors;
using ViciOne.SystemMonitoring.Configuration;

namespace ViciOne.SystemMonitoring.Metrics;

public sealed class MetricsManager(IOptionsMonitor<MonitoringConfig> options, ILogger<MetricsManager> logger, IEnumerable<IMetricsCollector> collectors) : IMetricsManager, IDisposable
{
    private readonly List<IMetricsCollector> _collectors = RegisterCollectors(collectors, logger);
    private readonly HashSet<IMetricsCollector> _hasFirstRun = [];

    private static List<IMetricsCollector> RegisterCollectors(IEnumerable<IMetricsCollector> collectors, ILogger<MetricsManager> logger)
    {
        List<IMetricsCollector> addedCollectors = [];

        foreach (var collector in collectors)
        {
            if (collector is IScheduledMetricsCollector scheduledCollector && scheduledCollector.IntervalInMinutes is not 1 and not 5 and not 15)
                throw new ArgumentException("Only intervals of 1, 5, or 15 minutes are allowed.");

            if (addedCollectors.Any(c => c.Name.Equals(collector.Name, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException($"Collector '{collector.Name}' is already registered.");

            addedCollectors.Add(collector);
            logger.LogRegisterCollector(collector.Name);
        }

        return addedCollectors;
    }

    public async Task Initialize(CancellationToken cancellationToken)
    {
        foreach (var collector in _collectors)
        {
            if (collector is not IPrepareMetricsCollector prepareCollector)
                continue;
            if (CollectorIsNotEnabled(options, collector))
                continue;

            try
            {
                logger.LogRunningPrepare(collector.Name);
                await prepareCollector.PrepareMetrics(cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogErrorCollectingMetrics(collector.Name, ex);
            }
        }
    }

    public async Task<List<Metric>> CollectAllMetrics(int currentInterval, DateTimeOffset cycleStartTime, CancellationToken cancellationToken)
    {
        var allMetrics = new List<Metric>();

        if (currentInterval is not 1 and not 5 and not 15)
            throw new ArgumentOutOfRangeException(nameof(currentInterval), "Interval must be 1, 5, or 15 minutes.");

        foreach (var collector in _collectors)
        {
            if (CollectorIsNotEnabled(options, collector))
                continue;
            if (collector is IScheduledMetricsCollector scheduledCollector)
            {
                if (!_hasFirstRun.Add(collector))
                {
                    if (currentInterval % scheduledCollector.IntervalInMinutes != 0)
                        continue;
                }
            }
            if (collector is IOnetimeMetricsCollector)
            {
                if (!_hasFirstRun.Add(collector))
                    continue;
            }

            try
            {
                logger.LogRunningCollector(collector.Name, currentInterval);

                var metrics = await collector.CollectMetrics(currentInterval, cycleStartTime, cancellationToken);
                allMetrics.AddRange(metrics);
            }
            catch (Exception ex)
            {
                logger.LogErrorCollectingMetrics(collector.Name, ex);
            }
        }

        return allMetrics;
    }

    private static bool CollectorIsNotEnabled(IOptionsMonitor<MonitoringConfig> options, IMetricsCollector collector)
        => !options.CurrentValue.EnabledMetrics.TryGetValue(collector.Name, out var isEnabled) || !isEnabled;

    public void Dispose()
    {
        foreach (var collector in _collectors)
        {
            if (collector is IDisposable disposable)
                disposable.Dispose();
        }
    }
}
