namespace JoinFS.Tests
{
    /// <summary>
    /// ElevatedPlatformTrust: when to trust a remote aircraft's raw altitude over the local terrain.
    /// </summary>
    public class ElevatedPlatformTrustTests
    {
        const double Threshold = 50.0; // cm, the default
        const double SpitfireCg = 1.8; // metres from CG to the ground, tail down

        static bool Decide(bool previous, bool onGround, double altitude, double senderElevation, double senderCg, double localElevation) =>
            ElevatedPlatformTrust.Decide(previous, onGround, altitude, senderElevation, senderCg, localElevation, Threshold, out _, out _);

        [Fact]
        public void Taildragger_on_a_runway_whose_terrain_differs_between_installs_is_not_trusted()
        {
            // sender's runway at 84.0 m, ours 0.7 m higher: before, the CG height alone (1.8 m)
            // passed the elevation test, trust engaged and the correction was switched off
            Assert.False(Decide(false, true, 84.0 + SpitfireCg, 84.0, SpitfireCg, 84.7));
        }

        [Fact]
        public void A_trusted_aircraft_that_is_on_ordinary_ground_is_released()
        {
            Assert.False(Decide(true, true, 84.0 + SpitfireCg, 84.0, SpitfireCg, 84.4));
        }

        [Fact]
        public void Helicopter_on_a_rooftop_helipad_is_trusted()
        {
            // pad 20 m above the sender's terrain; our terrain reads 1 m lower, so the correction
            // would drag the helicopter off the pad
            Assert.True(Decide(false, true, 100.0 + 20.0 + 1.2, 100.0, 1.2, 99.0));
        }

        [Fact]
        public void Aircraft_on_final_to_a_platform_is_trusted_before_touchdown()
        {
            Assert.True(Decide(false, false, 100.0 + 30.0, 100.0, 1.2, 99.0));
        }

        [Fact]
        public void Airborne_aircraft_far_from_the_ground_is_not_trusted()
        {
            Assert.False(Decide(true, false, 100.0 + 300.0, 100.0, 1.2, 100.0));
        }

        [Fact]
        public void Peer_that_does_not_send_its_clearance_keeps_the_cg_height_test()
        {
            // no clearance: the CG height is all there is, so the old decision stands
            Assert.True(Decide(false, true, 84.0 + SpitfireCg, 84.0, double.NaN, 84.7));
        }

        [Fact]
        public void Mismatch_inside_the_hysteresis_band_keeps_the_previous_decision()
        {
            // on a 2 m platform, with a 0.4 m mismatch: between release (25 cm) and engage (50 cm)
            Assert.True(Decide(true, true, 100.0 + 2.0 + 1.2, 100.0, 1.2, 99.6));
            Assert.False(Decide(false, true, 100.0 + 2.0 + 1.2, 100.0, 1.2, 99.6));
        }

        [Fact]
        public void Reports_the_mismatch_and_the_gear_height()
        {
            ElevatedPlatformTrust.Decide(false, true, 84.0 + SpitfireCg, 84.0, SpitfireCg, 84.7, Threshold, out double mismatchCm, out double gearHeight);
            Assert.Equal(70.0, mismatchCm, 6);
            Assert.Equal(0.0, gearHeight, 6);
        }
    }
}
