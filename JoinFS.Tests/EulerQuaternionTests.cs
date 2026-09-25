namespace JoinFS.Tests;

/// <summary>
/// Exercises Sim.EulerToQuat / Sim.QuatToEuler (internal, exposed via InternalsVisibleTo) directly -
/// coverage for the orientation-jitter fix in UpdateSimObjectVelocity, where a recorded/network aircraft's
/// pitch/heading/bank used to be sent to SimConnect via a direct Euler assignment every tick. That has a
/// discontinuity at the Euler singularities (near +/-90 degrees pitch, or +/-90 degrees bank) and, more
/// importantly for a normal turn, gives no way to blend smoothly between the current and target orientation -
/// see the "yaw hopping while circling" issue these helpers were added for.
/// </summary>
public class EulerQuaternionTests
{
    const double Tolerance = 1e-4;

    static void AssertAnglesClose(Vector expected, Vector actual, double tolerance = Tolerance)
    {
        Assert.True(Math.Abs(expected.x - actual.x) < tolerance, $"pitch: expected {expected.x}, got {actual.x}");
        Assert.True(Math.Abs(expected.y - actual.y) < tolerance, $"heading: expected {expected.y}, got {actual.y}");
        Assert.True(Math.Abs(expected.z - actual.z) < tolerance, $"bank: expected {expected.z}, got {actual.z}");
    }

    [Theory]
    [InlineData(0.0, 0.0, 0.0)]
    [InlineData(0.0, Math.PI / 2, 0.0)]
    [InlineData(0.0, 3.0, 0.0)]                        // close to, but not exactly on, the +/-PI wrap boundary
    [InlineData(0.0, -Math.PI / 2, 0.0)]
    [InlineData(0.05, 1.9, -0.61)]                    // representative of a shallow-climb, mid-turn attitude
    [InlineData(-0.02, 2.4, -0.79)]                    // ~-45 degrees bank, close to what a thermalling glider uses
    public void RoundTrip_PreservesEulerAngles(double pitch, double heading, double bank)
    {
        var angles = new Vector(pitch, heading, bank);

        var result = Sim.QuatToEuler(Sim.EulerToQuat(angles));

        AssertAnglesClose(angles, result);
    }

    [Fact]
    public void Slerp_AtT0_ReturnsTheStartingOrientation()
    {
        var from = new Vector(0.02, 0.5, -0.61);
        var to = new Vector(-0.01, 0.9, -0.70);

        var qFrom = Sim.EulerToQuat(from);
        var qTo = Sim.EulerToQuat(to);
        var result = Sim.QuatToEuler(System.Numerics.Quaternion.Slerp(qFrom, qTo, 0.0f));

        AssertAnglesClose(from, result);
    }

    [Fact]
    public void Slerp_AtT1_ReturnsTheTargetOrientation()
    {
        var from = new Vector(0.02, 0.5, -0.61);
        var to = new Vector(-0.01, 0.9, -0.70);

        var qFrom = Sim.EulerToQuat(from);
        var qTo = Sim.EulerToQuat(to);
        var result = Sim.QuatToEuler(System.Numerics.Quaternion.Slerp(qFrom, qTo, 1.0f));

        AssertAnglesClose(to, result);
    }

    /// <summary>
    /// The actual circling scenario: a steady ~-40 degree bank with heading advancing a couple of degrees
    /// between two recorder keyframes (5 Hz output, a fast thermal turn). SLERPing between them at t=0.5
    /// should land close to the midpoint of both pitch/heading/bank, not swing wildly on one axis just
    /// because the other two are simultaneously non-zero - which is the failure mode quaternion SLERP of a
    /// combined-axis rotation can have if the two orientations were far apart. For steps this small it must
    /// stay close to independent per-axis interpolation.
    /// </summary>
    [Fact]
    public void Slerp_DuringASmallSteadyTurnStep_StaysCloseToLinearPerAxisInterpolation()
    {
        double bank = -40.0 * Math.PI / 180.0;
        var from = new Vector(0.03, 10.0 * Math.PI / 180.0, bank);
        var to = new Vector(0.03, 12.5 * Math.PI / 180.0, bank);

        var qFrom = Sim.EulerToQuat(from);
        var qTo = Sim.EulerToQuat(to);
        var mid = Sim.QuatToEuler(System.Numerics.Quaternion.Slerp(qFrom, qTo, 0.5f));

        var expected = new Vector(0.03, 11.25 * Math.PI / 180.0, bank);
        AssertAnglesClose(expected, mid, tolerance: 0.005);
    }
}
