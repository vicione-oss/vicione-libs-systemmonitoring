using ViciOne.SystemMonitoring.Metrics;

namespace ViciOne.SystemMonitoring;

public interface IOutput
{
    Task Connect(CancellationToken cancellationToken);
    Task Disconnect(CancellationToken cancellationToken);
    Task Publish(IReadOnlyCollection<Metric> metrics, DateTimeOffset timestamp, CancellationToken cancellationToken);
}
