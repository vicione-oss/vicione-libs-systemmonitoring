namespace ViciOne.SystemMonitoring.Collectors;

/// <summary>
/// A sequence of live reads at one interval; it keeps the baselines of the collectors, so that values computed from counters
/// cover the time since the previous read of the same window.
/// </summary>
public sealed class LiveWindow
{
    private readonly Dictionary<ILiveMetricsCollector, object> _baselines = [];
    private readonly HashSet<ILiveMetricsCollector> _read = [];
    private readonly Lock _lock = new();

    /// <summary>
    /// The baseline <paramref name="collector"/> keeps in this window, empty on its first read.
    /// </summary>
    /// <exception cref="InvalidOperationException">The collector asked for a different type before.</exception>
    public T Baseline<T>(ILiveMetricsCollector collector) where T : class, new()
    {
        ArgumentNullException.ThrowIfNull(collector);

        lock (_lock)
        {
            if (!_baselines.TryGetValue(collector, out var baseline))
                _baselines[collector] = baseline = new T();

            return baseline as T
                ?? throw new InvalidOperationException($"Collector '{collector.Name}' keeps a {baseline.GetType().Name} as its baseline, not a {typeof(T).Name}.");
        }
    }

    /// <summary>
    /// Records a successful read of <paramref name="collector"/>; false for the first one, whose values cover no interval yet.
    /// </summary>
    internal bool Read(ILiveMetricsCollector collector)
    {
        lock (_lock)
            return !_read.Add(collector);
    }

    /// <summary>
    /// Drops the baseline of <paramref name="collector"/>, so that its next read starts over instead of covering the time
    /// it was not read.
    /// </summary>
    internal void Forget(ILiveMetricsCollector collector)
    {
        lock (_lock)
        {
            _baselines.Remove(collector);
            _read.Remove(collector);
        }
    }
}
