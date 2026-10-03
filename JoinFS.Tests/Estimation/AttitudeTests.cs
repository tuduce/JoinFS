using JoinFS.Estimation;

namespace JoinFS.Tests.Estimation
{
    /// <summary>
    /// Attitude.Integrate against cases with known answers. Angles are (x pitch, y heading,
    /// z bank); body rates (x pitch, y yaw, z roll).
    /// </summary>
    public class AttitudeTests
    {
        static double Rad(double degrees) => degrees * Math.PI / 180.0;

        static void Near(Vector expected, Vector actual, double tolerance = 1e-9)
        {
            Assert.Equal(expected.x, actual.x, tolerance);
            Assert.Equal(expected.y, actual.y, tolerance);
            Assert.Equal(expected.z, actual.z, tolerance);
        }

        [Fact]
        public void NoTime_NoChange()
        {
            var angles = new Vector(Rad(5), Rad(123), Rad(-40));
            Near(angles, Attitude.Integrate(angles, new Vector(0.1, 0.2, 0.3), 0.0));
        }

        [Theory]
        [InlineData(30.0)]
        [InlineData(-60.0)]
        [InlineData(75.0)]
        public void CoordinatedTurn_TurnsTheHeadingOnly(double bankDegrees)
        {
            // a level turn at 3 deg/s: the body sees the turn rate split between yaw and pitch
            double bank = Rad(bankDegrees), turnRate = Rad(3.0);
            var rates = new Vector(turnRate * Math.Sin(bank), turnRate * Math.Cos(bank), 0.0);
            var angles = new Vector(0.0, Rad(90), bank);
            foreach (double t in new[] { 0.05, 0.2, 2.0 })
            {
                Near(new Vector(0.0, Rad(90) + turnRate * t, bank), Attitude.Integrate(angles, rates, t));
            }
        }

        [Fact]
        public void Classic_UnderTurnsInABankedTurn()
        {
            // what F4 is about: adding the yaw rate to the heading turns only cos(bank) as far
            double bank = Rad(60), turnRate = Rad(3.0), t = 0.2;
            var rates = new Vector(turnRate * Math.Sin(bank), turnRate * Math.Cos(bank), 0.0);
            var angles = new Vector(0.0, Rad(90), bank);
            double classic = (angles + rates * t).y - angles.y;
            double integrated = Attitude.Integrate(angles, rates, t).y - angles.y;
            Assert.Equal(turnRate * t * 0.5, classic, 1e-12);
            Assert.Equal(turnRate * t, integrated, 1e-9);
        }

        [Fact]
        public void WingsLevel_RollAndPitchRatesMoveBankAndPitch()
        {
            var level = new Vector(0.0, Rad(200), 0.0);
            Near(new Vector(0.0, Rad(200), Rad(9)), Attitude.Integrate(level, new Vector(0.0, 0.0, Rad(30)), 0.3));
            Near(new Vector(Rad(6), Rad(200), 0.0), Attitude.Integrate(level, new Vector(Rad(20), 0.0, 0.0), 0.3));
            Near(new Vector(0.0, Rad(206), 0.0), Attitude.Integrate(level, new Vector(0.0, Rad(20), 0.0), 0.3));
        }

        [Fact]
        public void SmallSteps_MatchTheEulerRateFormulas()
        {
            // pitch rate = q cos(bank) - r sin(bank); heading rate = (q sin(bank) + r cos(bank)) / cos(pitch);
            // bank rate = p + (q sin(bank) + r cos(bank)) tan(pitch)
            var angles = new Vector(Rad(12), Rad(45), Rad(-35));
            var rates = new Vector(0.05, 0.08, -0.12);
            double t = 1e-4;
            double q = rates.x, r = rates.y, p = rates.z, pitch = angles.x, bank = angles.z;
            var expected = new Vector(
                q * Math.Cos(bank) - r * Math.Sin(bank),
                (q * Math.Sin(bank) + r * Math.Cos(bank)) / Math.Cos(pitch),
                p + (q * Math.Sin(bank) + r * Math.Cos(bank)) * Math.Tan(pitch));
            Vector actual = (Attitude.Integrate(angles, rates, t) - angles) * (1.0 / t);
            Near(expected, actual, 1e-5);
        }

        [Fact]
        public void HeadingAndBank_DoNotJumpAtTheWrapLine()
        {
            // turning right through north, and rolling past inverted
            Vector heading = Attitude.Integrate(new Vector(0.0, Rad(359), 0.0), new Vector(0.0, Rad(10), 0.0), 0.2);
            Assert.Equal(Rad(361), heading.y, 1e-9);
            Vector bank = Attitude.Integrate(new Vector(0.0, Rad(10), Rad(179)), new Vector(0.0, 0.0, Rad(10)), 0.2);
            Assert.Equal(Rad(181), bank.z, 1e-9);
        }

        [Fact]
        public void NearVertical_StaysFinite()
        {
            Vector angles = Attitude.Integrate(new Vector(Rad(89.9), Rad(10), Rad(5)), new Vector(Rad(30), Rad(5), Rad(40)), 0.1);
            Assert.True(double.IsFinite(angles.x) && double.IsFinite(angles.y) && double.IsFinite(angles.z));
            Assert.InRange(angles.x, -Math.PI / 2, Math.PI / 2);
        }
    }
}
