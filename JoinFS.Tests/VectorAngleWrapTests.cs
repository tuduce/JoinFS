namespace JoinFS.Tests;

/// <summary>
/// Coverage for the primitive Recorder.InterpolateAngles' fix relies on: Vector.AngleDelta(0, x) wraps any
/// magnitude into (-PI, PI]. Recorder.InterpolateAngles itself can't be unit-tested directly (it is an
/// instance method on Recorder, which needs a live Main), so this exercises the same accumulation pattern it
/// uses - repeatedly adding a small per-tick delta via AngleDelta, exactly as InterpolateAngles' continuity
/// unwrapping does - to show the unbounded-growth bug (playbackAngles accumulating without limit across many
/// turns, eventually losing float32 precision when it reaches SimConnect/EulerToQuat) and that wrapping the
/// accumulator after every step, as the fix now does, keeps it bounded without changing what it tracks.
/// </summary>
public class VectorAngleWrapTests
{
    [Theory]
    [InlineData(0.0)]
    [InlineData(1.0)]
    [InlineData(-1.0)]
    [InlineData(100.0)]                        // ~16 revolutions
    [InlineData(-100.0)]
    [InlineData(10_000.0)]                     // ~1592 revolutions - the magnitude a long thermalling flight can reach
    [InlineData(1_000_000.0)]
    public void AngleDelta_FromZero_WrapsAnyMagnitudeIntoPiRange(double x)
    {
        double wrapped = Vector.AngleDelta(0, x);

        Assert.True(wrapped > -Math.PI - 1e-9 && wrapped <= Math.PI + 1e-9, $"wrapped={wrapped}");
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(100.0)]
    [InlineData(10_000.0)]
    public void AngleDelta_FromZero_PreservesTheAngleModuloTwoPi(double x)
    {
        double wrapped = Vector.AngleDelta(0, x);

        // same physical direction: sin/cos must match (this is what SimConnect/EulerToQuat actually consume)
        Assert.True(Math.Abs(Math.Sin(x) - Math.Sin(wrapped)) < 1e-6, $"sin mismatch: {Math.Sin(x)} vs {Math.Sin(wrapped)}");
        Assert.True(Math.Abs(Math.Cos(x) - Math.Cos(wrapped)) < 1e-6, $"cos mismatch: {Math.Cos(x)} vs {Math.Cos(wrapped)}");
    }

    /// <summary>
    /// Mirrors the shape of the bug: many small per-tick turns accumulated the way InterpolateAngles does
    /// (running total += AngleDelta(total, freshTarget)) grow without bound if never wrapped back down - a
    /// thermalling glider doing dozens of tight circles reaches a magnitude where ObjectEuler's float cast
    /// and EulerToQuat's CreateFromYawPitchRoll (both float, both needing their own mod-2*PI argument
    /// reduction to compute sin/cos) lose real precision. Wrapping the running total after every step keeps
    /// it bounded while the tracked direction - verified via sin/cos, not the raw number - stays identical.
    /// </summary>
    [Fact]
    public void RepeatedSmallTurns_StayBoundedWhenWrappedEachStep_ButGrowUnboundedWithoutIt()
    {
        const double perTickTurnRad = -0.25;               // ~-14 deg/s at 5 Hz, matching a tight thermal turn
        const int ticks = 5 * 60 * 20;                       // 20 minutes at 5 Hz

        double unbounded = 0.0;
        double bounded = 0.0;
        for (int i = 1; i <= ticks; i++)
        {
            // the "freshly computed, always small/wrapped" target InterpolateAngles gets from SlerpEuler
            // each tick - independent of either accumulator's own running state, like a real keyframe is
            double target = Vector.AngleDelta(0, i * perTickTurnRad);

            unbounded += Vector.AngleDelta(unbounded, target);
            bounded += Vector.AngleDelta(bounded, target);
            bounded = Vector.AngleDelta(0, bounded);        // the fix: re-wrap after every step
        }

        // ~20 minutes of a ~14 deg/s turn is roughly 280 revolutions - unmistakably outside (-PI, PI] if
        // nothing ever wraps it back down.
        Assert.True(Math.Abs(unbounded) > 100 * Math.PI, $"expected the unwrapped accumulator to have grown large, got {unbounded}");

        // the fixed accumulator must stay in range at every step, not just at the end
        Assert.True(bounded > -Math.PI - 1e-6 && bounded <= Math.PI + 1e-6, $"bounded accumulator escaped its range: {bounded}");

        // and it must still track the same physical direction as the unbounded one would have
        Assert.True(Math.Abs(Math.Sin(unbounded) - Math.Sin(bounded)) < 1e-6);
        Assert.True(Math.Abs(Math.Cos(unbounded) - Math.Cos(bounded)) < 1e-6);
    }
}
