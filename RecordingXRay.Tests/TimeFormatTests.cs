using RecordingXRay.Services;

namespace RecordingXRay.Tests;

public class TimeFormatTests
{
    [Theory]
    [InlineData(711.165, "11:51.165")]
    [InlineData(0.026, "0:00.026")]
    [InlineData(0, "0:00.000")]
    [InlineData(59.9996, "1:00.000")]
    [InlineData(3725.5, "62:05.500")]
    public void Clock_formats_minutes_seconds_and_milliseconds(double seconds, string expected) =>
        Assert.Equal(expected, TimeFormat.Clock(seconds));

    [Fact]
    public void Clock_can_pad_the_minutes() =>
        Assert.Equal("00:00.026", TimeFormat.Clock(0.026, padMinutes: true));

    [Theory]
    [InlineData(-5)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Clock_treats_invalid_times_as_zero(double seconds) =>
        Assert.Equal("0:00.000", TimeFormat.Clock(seconds));

    [Fact]
    public void Seconds_uses_three_decimals_and_a_unit() =>
        Assert.Equal("711.165 s", TimeFormat.Seconds(711.165));
}
