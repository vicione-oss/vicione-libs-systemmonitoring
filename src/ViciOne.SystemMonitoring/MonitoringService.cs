using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ViciOne.SystemMonitoring.Metrics;

namespace ViciOne.SystemMonitoring;

internal sealed class MonitoringService : IHostedService, IDisposable
{
    private readonly IMetricsManager _metricsManager;
    private readonly IOutput[] _outputs;
    private readonly ILogger<MonitoringService> _logger;
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly PeriodicTimer _timer;
    private readonly TimeProvider _timerProvider;
    private Task? _processTask;

    public MonitoringService(IMetricsManager metricsManager, IEnumerable<IOutput> outputs, ILogger<MonitoringService> logger, TimeProvider? timerProvider = null)
    {
        _timerProvider = timerProvider ?? TimeProvider.System;
        _metricsManager = metricsManager;
        _outputs = [.. outputs];
        _logger = logger;
        _timer = new(TimeSpan.FromMinutes(1), _timerProvider);
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (var output in _outputs)
            await output.Connect(cancellationToken);
        await _metricsManager.Initialize(cancellationToken);
        _processTask = Task.Run(() => ProcessTimer(_cancellationTokenSource.Token), cancellationToken);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _cancellationTokenSource.CancelAsync();

        if (_processTask is not null)
        {
            await _processTask.WaitAsync(cancellationToken);
            _processTask = null;
        }

        foreach (var output in _outputs)
            await output.Disconnect(cancellationToken);
    }

    private async Task ProcessTimer(CancellationToken cancellationToken)
    {
        try
        {
            while (await _timer.WaitForNextTickAsync(cancellationToken))
                await ProcessMetrics(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogMonitoringStoppedByCancellationToken();
        }
    }

    private async Task ProcessMetrics(CancellationToken cancellationToken)
    {
        var cycleStartTime = _timerProvider.GetUtcNow();

        _logger.LogProcessingNextInterval(cycleStartTime);

        var currentInterval = CalculateInterval(cycleStartTime);
        var metrics = await _metricsManager.CollectAllMetrics(currentInterval, cycleStartTime, cancellationToken);

        foreach (var output in _outputs)
        {
            try
            {
                await output.Publish(metrics, cycleStartTime, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogPublishFailed(output.GetType().Name, ex);
            }
        }
    }

    private static int CalculateInterval(DateTimeOffset currentTime)
    {
        if (currentTime.Minute is < 0 or >= 60)
            throw new ArgumentOutOfRangeException(nameof(currentTime), "Minute value must be between 0 and 59.");
        if (currentTime.Minute % 15 == 0)
            return 15;
        if (currentTime.Minute % 5 == 0)
            return 5;
        return 1;
    }

    public void Dispose()
    {
        _timer.Dispose();
        _cancellationTokenSource.Dispose();
    }
}
