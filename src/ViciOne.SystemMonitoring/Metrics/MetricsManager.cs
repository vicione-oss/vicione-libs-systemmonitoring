using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ViciOne.SystemMonitoring.Collectors;
using ViciOne.SystemMonitoring.Configuration;

namespace ViciOne.SystemMonitoring.Metrics;

public sealed class MetricsManager(IOptionsMonitor<MonitoringConfig> options, ILogger<MetricsManager> logger, IEnumerable<IMetricsCollector> collectors) : IMetricsManager, ILiveMetricsSource, IDisposable
{
    private readonly List<IMetricsCollector> _collectors = RegisterCollectors(collectors, logger);
    private readonly HashSet<IMetricsCollector> _hasFirstRun = [];
    private readonly HashSet<string> _failedLiveCollectors = [];

    // Regular and live collection must not run at the same time.
    private readonly SemaphoreSlim _gate = new(1, 1);
    private int _disposed;

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
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await InitializeCollectors(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<List<Metric>> CollectAllMetrics(int currentInterval, DateTimeOffset cycleStartTime, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return await CollectRegularMetrics(currentInterval, cycleStartTime, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<List<Metric>> CollectLiveMetrics(CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _disposed) == 1)
            return [];

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var allMetrics = new List<Metric>();

            foreach (var collector in _collectors.OfType<ILiveMetricsCollector>())
            {
                if (CollectorIsNotEnabled(options, collector))
                    continue;

                try
                {
                    allMetrics.AddRange(await collector.CollectLiveMetrics(cancellationToken));
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    if (_failedLiveCollectors.Add(collector.Name))
                        logger.LogErrorCollectingLiveMetrics(collector.Name, ex);
                }
            }

            return allMetrics;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task InitializeCollectors(CancellationToken cancellationToken)
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

    private async Task<List<Metric>> CollectRegularMetrics(int currentInterval, DateTimeOffset cycleStartTime, CancellationToken cancellationToken)
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
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
            return;

        // A collection that is still running holds the gate and must finish before the gate and the collectors go away.
        _gate.Wait();

        foreach (var collector in _collectors)
        {
            if (collector is IDisposable disposable)
                disposable.Dispose();
        }

        _gate.Dispose();
    }
}
