using AwesomeAssertions;
using ViciOne.SystemMonitoring.Metrics;

namespace ViciOne.SystemMonitoring;

public class MetricsHelperTests_AggregateBuffer
{
    [Fact]
    public void Aggregates_values()
    {
        List<List<LastMetricValue>> buffer =
            [
                [
                    new("metric1", 1d, string.Empty),
                    new("metric2", 2d, string.Empty)
                ],
                [
                    new("metric1", 3d, string.Empty),
                    new("metric2", 4d, string.Empty)
                ],
                [
                    new("metric1", 5d, string.Empty),
                    new("metric2", 6d, string.Empty)
                ],
            ];

        var result = buffer.AggregateBuffer();

        result.Should().HaveCount(2);
        result.Should().SatisfyRespectively(
            e =>
            {
                e.Name.Should().Be("metric1");
                e.Min.Should().Be(1);
                e.Max.Should().Be(5);
                e.Avg.Should().Be(3);
            },
            e =>
            {
                e.Name.Should().Be("metric2");
                e.Min.Should().Be(2);
                e.Max.Should().Be(6);
                e.Avg.Should().Be(4);
            });
    }

    [Fact]
    public void Throws_if_value_is_not_double()
    {
        List<List<LastMetricValue>> buffer =
            [
                [
                    new("metric1", 1, string.Empty),
                    new("metric2", 2d, string.Empty)
                ],
            ];

        Func<AggregatedMetricValue[]> act = () => [.. buffer.AggregateBuffer()];

        act.Should().Throw<InvalidOperationException>();
    }
}
