using AwesomeAssertions;
using NSubstitute;
using ViciOne.SystemMonitoring.Collectors;

namespace ViciOne.SystemMonitoring;

public class LiveWindow_Baseline
{
    [Fact]
    public void Returns_the_same_baseline_on_every_call_of_a_collector()
    {
        var collector = Substitute.For<ILiveMetricsCollector>();
        LiveWindow window = new();

        var first = window.Baseline<Dictionary<string, double>>(collector);
        var second = window.Baseline<Dictionary<string, double>>(collector);

        second.Should().BeSameAs(first);
    }

    [Fact]
    public void Keeps_a_baseline_per_collector()
    {
        LiveWindow window = new();

        var first = window.Baseline<Dictionary<string, double>>(Substitute.For<ILiveMetricsCollector>());
        var second = window.Baseline<Dictionary<string, double>>(Substitute.For<ILiveMetricsCollector>());

        second.Should().NotBeSameAs(first);
    }

    [Fact]
    public void Keeps_a_baseline_per_window()
    {
        var collector = Substitute.For<ILiveMetricsCollector>();

        var first = new LiveWindow().Baseline<Dictionary<string, double>>(collector);
        var second = new LiveWindow().Baseline<Dictionary<string, double>>(collector);

        second.Should().NotBeSameAs(first);
    }

    [Fact]
    public void Rejects_a_second_baseline_type_for_the_same_collector()
    {
        var collector = Substitute.For<ILiveMetricsCollector>();
        collector.Name.Returns("cpu");
        LiveWindow window = new();
        window.Baseline<Dictionary<string, double>>(collector);

        var baseline = () => window.Baseline<List<double>>(collector);

        baseline.Should().Throw<InvalidOperationException>().WithMessage("*cpu*");
    }

    [Fact]
    public void Rejects_a_missing_collector()
    {
        var baseline = () => new LiveWindow().Baseline<List<double>>(null!);

        baseline.Should().Throw<ArgumentNullException>();
    }
}
