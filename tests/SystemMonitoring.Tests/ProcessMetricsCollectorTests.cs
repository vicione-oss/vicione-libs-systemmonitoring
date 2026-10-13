using System.Diagnostics.CodeAnalysis;
using AwesomeAssertions;
using ViciOne.SystemMonitoring.Collectors;

namespace ViciOne.SystemMonitoring;

[SuppressMessage("Interoperability", "CA1416:Validate platform compatibility")]
public class ProcessMetricsCollector_SplitStatFields
{
    [Theory]
    [InlineData("213 (hostmanagement) S 1 213 213 0 -1 4194560 1468 109255 85 151 6 8 1416 216 20 0 6 0 751 278053830656 4073", "(hostmanagement)")]
    [InlineData("4711 (Web Content) S 1 213 213 0 -1 4194560 1468 109255 85 151 6 8 1416 216 20 0 6 0 751 278053830656 4073", "(Web Content)")]
    [InlineData("4711 (a) b (c)) S 1 213 213 0 -1 4194560 1468 109255 85 151 6 8 1416 216 20 0 6 0 751 278053830656 4073\n", "(a) b (c))")]
    public void Keeps_the_fields_in_place_whatever_the_name(string stat, string name)
    {
        var fields = ProcessMetricsCollector.SplitStatFields(stat);

        fields[1].Should().Be(name);
        fields[2].Should().Be("S");
        fields[13].Should().Be("6", "utime");
        fields[14].Should().Be("8", "stime");
        fields[21].Should().Be("751", "starttime");
        fields.Should().HaveCount(24);
    }
}

[SuppressMessage("Interoperability", "CA1416:Validate platform compatibility")]
public class ProcessMetricsCollector_CpuShare
{
    [Fact]
    public void Takes_the_share_of_all_cores_since_the_previous_reading()
        => ProcessMetricsCollector.CpuShare(processTicks: 1_300, previousProcessTicks: 1_000, totalCoreTicksDelta: 2_400, runningSeconds: 600, clockTicks: 100, cores: 4)
            .Should().Be(12.5);

    [Fact]
    public void Takes_the_average_share_of_all_cores_since_the_start_on_the_first_reading()
        => ProcessMetricsCollector.CpuShare(processTicks: 75_600, previousProcessTicks: null, totalCoreTicksDelta: 0, runningSeconds: 600, clockTicks: 100, cores: 4)
            .Should().Be(31.5, "1.26 cores over 600 s are 31.5 % of four cores");

    [Fact]
    public void Keeps_the_fraction_of_a_second_on_the_first_reading()
        => ProcessMetricsCollector.CpuShare(processTicks: 150, previousProcessTicks: null, totalCoreTicksDelta: 0, runningSeconds: 10, clockTicks: 100, cores: 1)
            .Should().Be(15);

    [Fact]
    public void Returns_zero_without_elapsed_time()
        => ProcessMetricsCollector.CpuShare(processTicks: 150, previousProcessTicks: 150, totalCoreTicksDelta: 0, runningSeconds: 0, clockTicks: 100, cores: 4)
            .Should().Be(0);
}

[SuppressMessage("Interoperability", "CA1416:Validate platform compatibility")]
public class ProcessMetricsCollector_ParseCoreTimes
{
    [Fact]
    public void Counts_the_cores_listed_in_proc_stat()
    {
        string[] stat =
        [
            "cpu  100 0 50 1000 10 0 5 0 0 0",
            "cpu0 50 0 25 500 5 0 3 0 0 0",
            "cpu2 50 0 25 500 5 0 2 0 0 0",
            "intr 12345 0 0",
            "ctxt 67890",
        ];

        ProcessMetricsCollector.ParseCoreTimes(stat).Should().Be((1165L, 2), "an offline core has no line");
    }
}
