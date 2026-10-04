using RecordingXRay.Services;

namespace RecordingXRay.Tests;

public class NumberFormatTests
{
    [Fact]
    public void Raw_keeps_the_short_float_text()
    {
        Assert.Equal("-0.19611156", NumberFormat.Raw(-0.19611156f));
        Assert.Equal("0.9674238236590125", NumberFormat.Raw(0.9674238236590125));
    }

    [Theory]
    [InlineData(0.9674238236590125, 4, "55.4293°")]
    [InlineData(0.231041585664082, 4, "13.2377°")]
    [InlineData(-0.19611156, 2, "-11.24°")]
    [InlineData(-0.00005, 2, "0.00°")]
    [InlineData(0, 2, "0.00°")]
    public void Degrees_converts_radians(double radians, int decimals, string expected) =>
        Assert.Equal(expected, NumberFormat.Degrees(radians, decimals));

    [Theory]
    [InlineData(2.874476, "164.70°")]
    [InlineData(-1.0, "302.70°")]
    [InlineData(7.0, "41.07°")]
    public void Heading_wraps_into_0_to_360(double radians, string expected) =>
        Assert.Equal(expected, NumberFormat.Heading(radians));

    [Theory]
    [InlineData(-1471, true, -0.0898)]
    [InlineData(16384, true, 1.0)]
    [InlineData(-32768, true, -1.0)]
    [InlineData(-5, false, 0.0)]
    [InlineData(8192, false, 0.5)]
    public void ControlFraction_scales_by_16384_and_clamps(short raw, bool centered, double expected) =>
        Assert.Equal(expected, NumberFormat.ControlFraction(raw, centered), precision: 3);
}

public class TimeParseTests
{
    [Theory]
    [InlineData("12.5", 12.5)]
    [InlineData("12.5 s", 12.5)]
    [InlineData("  3  ", 3)]
    [InlineData("1:02.5", 62.5)]
    [InlineData("11:51.165", 711.165)]
    [InlineData("0:00.026", 0.026)]
    public void Parses_seconds_and_minutes_seconds(string text, double expected)
    {
        Assert.True(TimeParse.TryParse(text, out double seconds));
        Assert.Equal(expected, seconds, precision: 6);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("-3")]
    [InlineData("abc")]
    [InlineData("Float")]
    [InlineData("1:2:3")]
    [InlineData("1,5")]
    [InlineData("-1:30")]
    public void Rejects_other_text(string text) => Assert.False(TimeParse.TryParse(text, out _));
}

public class FrameFormatterTests
{
    [Fact]
    public void FormatFrame_lists_every_position_field_with_invariant_numbers()
    {
        string text = FrameFormatter.FormatFrame(TestFrames.Position(0.026), _ => "x");

        Assert.StartsWith("Type: AircraftPosition" + Environment.NewLine + "Time: 0.026" + Environment.NewLine, text);
        Assert.Contains("Latitude: 0.9674238236590125", text);
        Assert.Contains("Pitch: -0.19611156", text);
        Assert.Contains("ElevatorRaw: 712", text);
        Assert.Contains("Elevation: 44.945507", text);
        Assert.Contains("Ground: True", text);
        Assert.Contains("ElevationCorrection: False", text);
    }

    [Fact]
    public void FormatFrame_names_variables_through_the_resolver()
    {
        string text = FrameFormatter.FormatFrame(TestFrames.Integers(0.154, (42, 5)), id => id == 42 ? "gear" : "unknown");

        Assert.Contains("Variables:", text);
        Assert.Contains("  42 (gear) = 5", text);
    }

    [Fact]
    public void FormatFrame_says_none_for_an_empty_variables_frame() =>
        Assert.Contains("(none)", FrameFormatter.FormatFrame(TestFrames.Floats(1), _ => "?"));

    [Fact]
    public void FormatAircraft_includes_the_type_role_text_and_skips_empty_optional_lines()
    {
        RecordedAircraft aircraft = TestFrames.Aircraft("YR-SCD");

        string text = FrameFormatter.FormatAircraft(aircraft);

        Assert.Contains("TypeRole: 1 (SingleProp)", text);
        Assert.DoesNotContain("Livery", text);
    }
}
