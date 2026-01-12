using FluentAssertions;
using ViciOne.SystemMonitoring.Collectors;

namespace ViciOne.SystemMonitoring;

public class NetworkStatsFactory_CalculateDifferenceCountingInWrapAround
{
    [Fact]
    public void Can_handle_increasing_counter()
        => NetworkStatsFactory.CalculateDifferenceCountingInWrapAround(100, 90).Should().Be(10);

    [Theory]
    [InlineData(ulong.MaxValue)]
    [InlineData(uint.MaxValue)]
    public void Can_handle_wrap_around(ulong value)
        => NetworkStatsFactory.CalculateDifferenceCountingInWrapAround(10, value - 10).Should().Be(21);

    [Fact]
    public void Can_handle_big_values()
        => NetworkStatsFactory.CalculateDifferenceCountingInWrapAround(ulong.MaxValue - 1000, ulong.MaxValue - 1100).Should().Be(100);
}
