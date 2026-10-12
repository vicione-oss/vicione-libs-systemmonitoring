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
    /// Hands each live sample to <paramref name="onSample"/> until the returned handle is disposed; with several handles the
    /// shortest interval wins and every subscriber receives every sample.
    /// </summary>
    /// <param name="interval">The time between two samples, at least 100 ms.</param>
    /// <param name="onSample">Called on the sampling thread; an exception it throws is logged and affects no other subscriber.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="interval"/> is shorter than 100 ms.</exception>
    IDisposable Subscribe(TimeSpan interval, Action<LiveSample> onSample);
}

public sealed record LiveSample(IReadOnlyList<Metric> Metrics, DateTimeOffset Timestamp);
