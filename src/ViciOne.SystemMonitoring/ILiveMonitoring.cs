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

    /// <summary>
    /// Like <see cref="Subscribe(TimeSpan, Action{LiveSample})"/>, but reads only the named collectors.
    /// </summary>
    /// <remarks>
    /// Subscriptions with the same interval and the same collectors share one read per sample. A name that is not registered,
    /// not enabled or not live adds nothing to the sample.
    /// </remarks>
    /// <param name="interval">The time between two samples, at least 100 ms.</param>
    /// <param name="collectors">The names of the collectors to read, such as <c>cpu</c> or <c>loadavg</c>; case is ignored.</param>
    /// <param name="onSample">Called on the sampling thread; an exception it throws is logged and affects no other subscriber.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="interval"/> is shorter than 100 ms.</exception>
    /// <exception cref="ArgumentException"><paramref name="collectors"/> is empty.</exception>
    IDisposable Subscribe(TimeSpan interval, IReadOnlyCollection<string> collectors, Action<LiveSample> onSample);
}

public sealed record LiveSample(IReadOnlyList<Metric> Metrics, DateTimeOffset Timestamp);
