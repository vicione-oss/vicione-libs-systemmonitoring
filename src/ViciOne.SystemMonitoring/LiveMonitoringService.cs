using Microsoft.Extensions.Logging;
using ViciOne.SystemMonitoring.Collectors;
using ViciOne.SystemMonitoring.Metrics;

namespace ViciOne.SystemMonitoring;

internal sealed class LiveMonitoringService(ILiveMetricsSource source, ILogger<LiveMonitoringService> logger, TimeProvider? timeProvider = null)
    : ILiveMonitoring, IAsyncDisposable
{
    /// <summary>
    /// The shortest interval <see cref="Subscribe"/> accepts.
    /// </summary>
    private static readonly TimeSpan s_minimumInterval = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// The longest interval <see cref="Subscribe"/> accepts.
    /// </summary>
    private static readonly TimeSpan s_maximumInterval = TimeSpan.FromDays(1);

    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private readonly Lock _lock = new();
    private readonly List<Group> _groups = [];

    public IDisposable Subscribe(TimeSpan interval, Action<LiveSample> onSample)
        => Subscribe(interval, null, onSample);

    public IDisposable Subscribe(TimeSpan interval, IReadOnlyCollection<string> collectors, Action<LiveSample> onSample)
    {
        ArgumentNullException.ThrowIfNull(collectors);
        if (collectors.Count == 0)
            throw new ArgumentException("At least one collector is required.", nameof(collectors));

        return Subscribe(interval, collectors.ToHashSet(StringComparer.OrdinalIgnoreCase), onSample);
    }

    private Subscription Subscribe(TimeSpan interval, HashSet<string>? collectors, Action<LiveSample> onSample)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(interval, s_minimumInterval);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(interval, s_maximumInterval);
        ArgumentNullException.ThrowIfNull(onSample);

        lock (_lock)
        {
            var group = _groups.Find(g => g.Reads(interval, collectors));
            if (group is null)
            {
                group = new(interval, collectors);
                _groups.Add(group);
                Start(group);
            }

            Subscription subscription = new(this, group, onSample);
            group.Subscriptions.Add(subscription);
            return subscription;
        }
    }

    public async ValueTask DisposeAsync()
    {
        Task[] stopped;
        lock (_lock)
        {
            // A read still in flight then hands its sample to nobody.
            foreach (var group in _groups)
                group.Subscriptions.Clear();

            stopped = [.. _groups.Select(Stop)];
            _groups.Clear();
        }

        await Task.WhenAll(stopped).ConfigureAwait(false);
    }

    private void Unsubscribe(Subscription subscription)
    {
        lock (_lock)
        {
            var group = subscription.Group;
            if (!group.Subscriptions.Remove(subscription) || group.Subscriptions.Count > 0 || !_groups.Remove(group))
                return;

            _ = Stop(group);
        }
    }

    private void Start(Group group)
    {
        // Keeps the caller's culture and context, such as a UI circuit's, out of the loop.
        using (ExecutionContext.SuppressFlow())
            group.Sampling = Task.Run(() => Sample(group, group.Cancellation.Token));
    }

    private static Task Stop(Group group)
    {
        group.Cancellation.Cancel();
        return group.Sampling.ContinueWith(_ => group.Cancellation.Dispose(), TaskScheduler.Default);
    }

    /// <summary>
    /// Runs until the last subscription of the group is disposed; a failed read is logged once and skips that tick only.
    /// </summary>
    private async Task Sample(Group group, CancellationToken cancellationToken)
    {
        var readFailed = false;

        async Task<List<Metric>?> Read()
        {
            try
            {
                return await source.CollectLiveMetrics(group.Window, group.Collectors, cancellationToken);
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
            // The first read only sets the baselines of the window and returns nothing.
            await Read();

            using var timer = new PeriodicTimer(group.Interval, _timeProvider);
            HashSet<Subscription> failedSubscribers = [];
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                if (await Read() is { } metrics)
                    Publish(group, new(metrics, _timeProvider.GetUtcNow()), failedSubscribers);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The last subscription of the group was disposed.
        }
    }

    /// <summary>
    /// Hands the sample to each subscriber on its own; a failing subscriber is logged once and stops neither the others nor the loop.
    /// </summary>
    private void Publish(Group group, LiveSample sample, HashSet<Subscription> failedSubscribers)
    {
        Subscription[] subscribers;
        lock (_lock)
            subscribers = [.. group.Subscriptions];

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

    /// <summary>
    /// The subscriptions that share one interval and one set of collectors, and with them one sampling loop and one
    /// <see cref="LiveWindow"/>.
    /// </summary>
    private sealed class Group(TimeSpan interval, HashSet<string>? collectors)
    {
        public TimeSpan Interval { get; } = interval;

        /// <summary>
        /// The collectors to read, or null for all enabled ones.
        /// </summary>
        public HashSet<string>? Collectors { get; } = collectors;

        public LiveWindow Window { get; } = new();

        public List<Subscription> Subscriptions { get; } = [];

        public CancellationTokenSource Cancellation { get; } = new();

        public Task Sampling { get; set; } = Task.CompletedTask;

        public bool Reads(TimeSpan interval, HashSet<string>? collectors)
            => Interval == interval && (Collectors is null ? collectors is null : collectors is not null && Collectors.SetEquals(collectors));
    }

    private sealed class Subscription(LiveMonitoringService service, Group group, Action<LiveSample> onSample) : IDisposable
    {
        private int _disposed;

        public Group Group { get; } = group;

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
