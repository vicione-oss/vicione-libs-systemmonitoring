using System.Diagnostics.CodeAnalysis;
using AwesomeAssertions;
using ViciOne.SystemMonitoring.Collectors;

namespace ViciOne.SystemMonitoring;

[SuppressMessage("Interoperability", "CA1416:Plattformkompatibilität überprüfen")]
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
