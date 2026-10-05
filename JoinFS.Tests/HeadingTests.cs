namespace JoinFS.Tests;

/// <summary>
/// Reported headings are compass 0-359. Playback keeps an unwrapped running heading (Recorder.InterpolateAngles),
/// which the websocket used to report raw: after the first clockwise crossing of north it read 361 and the
/// plausibility check dropped the whole aircraft until the recording was jumped.
/// </summary>
public class HeadingTests
{
    static double Rad(double degrees) => degrees * Math.PI / 180.0;

    [Theory]
    [InlineData(0.0, 0)]
    [InlineData(90.0, 90)]
    [InlineData(180.0, 180)]
    [InlineData(270.0, 270)]
    [InlineData(359.0, 359)]
    [InlineData(359.6, 0)]
    [InlineData(360.0, 0)]            // 360 reads as 0
    [InlineData(361.3, 1)]            // the incident: the running heading just past north
    [InlineData(540.0, 180)]
    [InlineData(720.0, 0)]
    [InlineData(829.0, 109)]          // what this flight's running heading reached
    [InlineData(1910.0, 110)]
    [InlineData(-90.0, 270)]          // signed values wrap to compass
    [InlineData(-0.5, 0)]       // 359.5 rounds up to 360, which reads as 0
    [InlineData(-0.6, 359)]
    [InlineData(-360.0, 0)]
    [InlineData(-450.0, 270)]
    public void HeadingDegrees_WrapsToCompass(double degrees, int expected)
    {
        Assert.Equal(expected, Vector.HeadingDegrees(Rad(degrees)));
    }

    [Fact]
    public void HeadingDegrees_NeverReports360_EvenForTheRoundingEdge()
    {
        // 359.99999999999994 degrees and tiny negatives are the cases where a naive wrap yields 360
        foreach (double degrees in new[] { 359.99999999999994, 359.9999999, -1e-12, -1e-9, 720.0 - 1e-9, -360.0 - 1e-12 })
        {
            int heading = Vector.HeadingDegrees(Rad(degrees));
            Assert.InRange(heading, 0, 359);
        }
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void HeadingDegrees_NonFiniteInputReportsZero(double radians)
    {
        Assert.Equal(0, Vector.HeadingDegrees(radians));
    }

    [Fact]
    public void HeadingDegrees_RoundsToTheNearestDegree()
    {
        Assert.Equal(270, Vector.HeadingDegrees(Rad(269.9)));
        Assert.Equal(270, Vector.HeadingDegrees(Rad(270.4)));
        Assert.Equal(271, Vector.HeadingDegrees(Rad(270.5)));
        Assert.Equal(0, Vector.HeadingDegrees(Rad(359.6)));      // rounds up to 360, which reads as 0
        Assert.Equal(359, Vector.HeadingDegrees(Rad(359.4)));
    }

    [Theory]
    [InlineData(1)] [InlineData(59)] [InlineData(254)] [InlineData(358)] [InlineData(359)]
    public void HeadingDegrees_WholeDegreesSurviveTheRadianRoundTrip(int degrees)
    {
        // truncation reported 359 degrees as 358 (359 * pi/180 * 180/pi = 358.99999999999994)
        Assert.Equal(degrees, Vector.HeadingDegrees(Rad(degrees)));
    }

    /// <summary>
    /// The incident, replayed: a right turn from north-west through north, accumulated the way the recorder does
    /// (previous + AngleDelta). The old raw cast leaves the websocket's accepted range; the reported value never does.
    /// </summary>
    [Fact]
    public void ClockwiseTurnThroughNorth_StaysReportableAndContinuous()
    {
        double running = Rad(300.0);          // playback's running heading starts at the file's first heading
        int previousReported = Vector.HeadingDegrees(running);
        int rawOutOfRange = 0;

        for (double heading = 300.0; heading <= 300.0 + 780.0; heading += 0.5)   // 2+ turns, 0.5 degree steps
        {
            double fileHeading = Rad(heading % 360.0);                // the file stores 0..360
            running += Vector.AngleDelta(running, fileHeading);       // Recorder.InterpolateAngles accumulation

            int raw = (int)(running * 180.0 / Math.PI);               // what the websocket used to report
            if (!Vector.IsPlausibleHeading(raw))
            {
                rawOutOfRange++;
            }

            int reported = Vector.HeadingDegrees(running);
            Assert.True(Vector.IsPlausibleHeading(reported), $"reported {reported} for running {running * 180.0 / Math.PI:F1}");
            Assert.InRange(reported, 0, 359);

            // continuous: moving 0.5 degrees never jumps the reported heading by more than 1 (or wraps 359 -> 0)
            int step = Math.Abs(reported - previousReported);
            Assert.True(step <= 1 || step >= 359, $"jump {previousReported} -> {reported}");
            previousReported = reported;
        }

        Assert.True(rawOutOfRange > 0, "the unwrapped heading must leave the plausible range, or this test no longer covers the incident");
    }

    [Theory]
    [InlineData(-360, true)]
    [InlineData(0, true)]
    [InlineData(360, true)]
    [InlineData(361, false)]
    [InlineData(-361, false)]
    [InlineData(int.MinValue, false)]   // an undecoded field must still be rejected
    public void PlausibleHeading_StillRejectsGarbage(int heading, bool expected)
    {
        Assert.Equal(expected, Vector.IsPlausibleHeading(heading));
    }
}
