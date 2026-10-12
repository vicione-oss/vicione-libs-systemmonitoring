using ViciOne.SystemMonitoring.Metrics;

namespace ViciOne.SystemMonitoring;

/// <summary>
/// Reads the live collectors at a short interval while someone watches, besides the regular intervals.
/// </summary>
/// <remarks>
/// Live samples go only to the subscribers, never to an <see cref="IOutput"/>; the regular intervals report the same
/// values as without live sampling.
/// </remarks>
public interface ILiveMonitoring
{
    /// <summary>
    /// Hands a live sample to <paramref name="onSample"/> every <paramref name="interval"/> until the returned handle is disposed;
    /// shares and rates in it cover that interval, whatever other subscriptions ask for.
    /// </summary>
    /// <remarks>
    /// Subscriptions with the same interval share one read per sample.
    /// </remarks>
    /// <param name="interval">The time between two samples, from 100 ms to 1 day.</param>
    /// <param name="onSample">Called on the sampling thread; an exception it throws is logged and affects no other subscriber.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="interval"/> is shorter than 100 ms or longer than 1 day.</exception>
    IDisposable Subscribe(TimeSpan interval, Action<LiveSample> onSample);
}

public sealed record LiveSample(IReadOnlyList<Metric> Metrics, DateTimeOffset Timestamp);
