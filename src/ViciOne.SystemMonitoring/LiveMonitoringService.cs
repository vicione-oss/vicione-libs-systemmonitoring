using Microsoft.Extensions.Logging;
using ViciOne.SystemMonitoring.Metrics;

namespace ViciOne.SystemMonitoring;

internal sealed class LiveMonitoringService(ILiveMetricsSource source, ILogger<LiveMonitoringService> logger, TimeProvider? timeProvider = null)
    : ILiveMonitoring, IAsyncDisposable
{
    /// <summary>
    /// The shortest interval <see cref="Subscribe"/> accepts.
    /// </summary>
    private static readonly TimeSpan s_minimumInterval = TimeSpan.FromMilliseconds(100);

    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private readonly Lock _lock = new();
    private readonly List<Subscription> _subscriptions = [];

    private CancellationTokenSource? _cancellation;
    private Task? _sampling;
    private TimeSpan _interval;

    public IDisposable Subscribe(TimeSpan interval, Action<LiveSample> onSample)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(interval, s_minimumInterval);
        ArgumentNullException.ThrowIfNull(onSample);

        var subscription = new Subscription(this, interval, onSample);
        lock (_lock)
        {
            _subscriptions.Add(subscription);
            Restart();
        }

        return subscription;
    }

    public async ValueTask DisposeAsync()
    {
        Task? sampling;
        lock (_lock)
        {
            _subscriptions.Clear();
            sampling = _sampling;
            _cancellation?.Cancel();
        }

        if (sampling is not null)
            await sampling.ConfigureAwait(false);

        _cancellation?.Dispose();
    }

    private void Unsubscribe(Subscription subscription)
    {
        lock (_lock)
        {
            if (_subscriptions.Remove(subscription))
                Restart();
        }
    }

    /// <summary>
    /// Runs the sampling at the shortest subscribed interval, or stops it without subscribers.
    /// </summary>
    private void Restart()
    {
        var interval = _subscriptions.Count == 0 ? TimeSpan.Zero : _subscriptions.Min(s => s.Interval);
        if (interval == _interval && (_sampling is not null) == (interval > TimeSpan.Zero))
            return;

        _ = Stop();
        _interval = interval;
        if (interval <= TimeSpan.Zero)
            return;

        var cancellation = new CancellationTokenSource();
        _cancellation = cancellation;

        // Keeps the caller's culture and context, such as a UI circuit's, out of the loop.
        using (ExecutionContext.SuppressFlow())
            _sampling = Task.Run(() => Sample(interval, cancellation.Token));
    }

    private Task? Stop()
    {
        var sampling = _sampling;
        var cancellation = _cancellation;
        _sampling = null;
        _cancellation = null;
        _interval = TimeSpan.Zero;

        if (cancellation is null)
            return sampling;

        cancellation.Cancel();
        return sampling?.ContinueWith(_ => cancellation.Dispose(), TaskScheduler.Default);
    }

    /// <summary>
    /// Runs until the last subscription is disposed; a failed read is logged once and skips that tick only.
    /// </summary>
    private async Task Sample(TimeSpan interval, CancellationToken cancellationToken)
    {
        var readFailed = false;

        async Task<List<Metric>?> Read()
        {
            try
            {
                return await source.CollectLiveMetrics(cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                if (!readFailed)
                    logger.LogLiveReadFailed(ex);

                readFailed = true;
                return null;
            }
        }

        try
        {
            // The first read only moves the live baselines to now.
            await Read();

            using var timer = new PeriodicTimer(interval, _timeProvider);
            HashSet<Subscription> failedSubscribers = [];
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                if (await Read() is { } metrics)
                    Publish(new(metrics, _timeProvider.GetUtcNow()), failedSubscribers);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The last subscription was disposed.
        }
    }

    /// <summary>
    /// Hands the sample to each subscriber on its own; a failing subscriber is logged once and stops neither the others nor the loop.
    /// </summary>
    private void Publish(LiveSample sample, HashSet<Subscription> failedSubscribers)
    {
        Subscription[] subscribers;
        lock (_lock)
            subscribers = [.. _subscriptions];

        foreach (var subscriber in subscribers)
        {
            try
            {
                subscriber.Receive(sample);
            }
            catch (Exception ex)
            {
                if (failedSubscribers.Add(subscriber))
                    logger.LogSubscriberFailed(ex);
            }
        }
    }

    private sealed class Subscription(LiveMonitoringService service, TimeSpan interval, Action<LiveSample> onSample) : IDisposable
    {
        private int _disposed;

        public TimeSpan Interval { get; } = interval;

        /// <summary>
        /// Skips a sample that was taken before the handle was disposed but handed over after.
        /// </summary>
        public void Receive(LiveSample sample)
        {
            if (Volatile.Read(ref _disposed) == 0)
                onSample(sample);
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                service.Unsubscribe(this);
        }
    }
}
