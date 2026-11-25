using System;
using Xunit;

namespace JoinFS.Tests
{
    /// <summary>
    /// Ground placement of remote aircraft: local ground on the ground, the sender's altitude in the air,
    /// a time-based ramp between them, and a floor at the local ground
    /// </summary>
    public class GroundPlacementTests
    {
        const double Ft = 0.3048;
        // Swift's wiki: P3D at EDRY, ground 312.00 ft
        const double Ground = 312.0 * Ft;
        const double C172Cg = 3.77 * Ft;
        const double B737Cg = 9.67 * Ft;

        [Theory]
        [InlineData(C172Cg)]
        [InlineData(B737Cg)]
        public void On_the_ground_it_sits_at_the_local_ground_plus_its_own_cg(double cg)
        {
            // the sender's altitude is half a metre off: it does not count on the ground
            var result = GroundPlacement.Place(Ground + cg + 0.5, 1.0, Ground, cg, GroundPlacement.CgSource.Sim, 0.0, platform: false);

            Assert.Equal(Ground + cg, result.Altitude, 6);
            Assert.Equal(GroundPlacement.State.Ground, result.State);
        }

        [Fact]
        public void The_same_local_model_gets_the_same_ground_height_from_every_sender()
        {
            const double localCg = B737Cg;
            const double adjustment = -0.12;
            // an X-Plane sender: its reference point at its own measured height, or unknown (NaN does not reach Place)
            double xplane = GroundPlacement.Place(Ground + 2.1, 1.0, Ground, localCg, GroundPlacement.CgSource.Sim, adjustment, false).Altitude;
            // an MSFS sender: its STATIC CG TO GROUND a few cm off its real height
            double msfs = GroundPlacement.Place(Ground + B737Cg + 0.04, 1.0, Ground, localCg, GroundPlacement.CgSource.Sim, adjustment, false).Altitude;
            // a sender on another mesh, a metre higher
            double otherMesh = GroundPlacement.Place(Ground + 1.0 + B737Cg, 1.0, Ground, localCg, GroundPlacement.CgSource.Sim, adjustment, false).Altitude;

            Assert.Equal(xplane, msfs, 9);
            Assert.Equal(xplane, otherMesh, 9);
            Assert.Equal(Ground + localCg + adjustment, xplane, 9);
        }

        [Fact]
        public void In_the_air_it_flies_at_the_senders_altitude_and_the_adjustment_does_not_apply()
        {
            var result = GroundPlacement.Place(Ground + 300.0, 0.0, Ground, C172Cg, GroundPlacement.CgSource.Sim, -0.5, false);

            Assert.Equal(Ground + 300.0, result.Altitude, 9);
            Assert.Equal(GroundPlacement.State.Airborne, result.State);
        }

        [Fact]
        public void It_never_goes_below_the_local_ground()
        {
            // the sender's mesh is lower: on short final its altitude would put the model into the local ground
            var result = GroundPlacement.Place(Ground - 1.0, 0.0, Ground, C172Cg, GroundPlacement.CgSource.Sim, 0.0, false);

            Assert.Equal(Ground + C172Cg, result.Altitude, 9);
            Assert.Equal(GroundPlacement.State.Underflow, result.State);
        }

        [Fact]
        public void A_negative_adjustment_lowers_a_hovering_model_on_the_ground()
        {
            var result = GroundPlacement.Place(Ground + B737Cg, 1.0, Ground, B737Cg, GroundPlacement.CgSource.Sim, -0.3, false);

            Assert.Equal(Ground + B737Cg - 0.3, result.Altitude, 9);
            Assert.Equal(GroundPlacement.State.Ground, result.State);
        }

        [Fact]
        public void On_an_elevated_platform_the_senders_altitude_is_kept()
        {
            double deck = Ground + 25.0 + 1.2;
            var result = GroundPlacement.Place(deck, 1.0, Ground, 1.2, GroundPlacement.CgSource.Sim, -0.3, platform: true);

            Assert.Equal(deck, result.Altitude, 9);
            Assert.Equal(GroundPlacement.State.Platform, result.State);
        }

        [Fact]
        public void Without_a_local_ground_the_senders_altitude_is_used()
        {
            var result = GroundPlacement.Place(Ground + 2.0, 1.0, double.NaN, 2.0, GroundPlacement.CgSource.Sim, 0.0, false);

            Assert.Equal(Ground + 2.0, result.Altitude, 9);
            Assert.Equal(GroundPlacement.State.NoElevation, result.State);
        }

        [Fact]
        public void Halfway_through_the_ramp_it_is_between_ground_and_airborne_height()
        {
            double airborne = Ground + C172Cg + 10.0;
            var result = GroundPlacement.Place(airborne, 0.5, Ground, C172Cg, GroundPlacement.CgSource.Sim, 0.0, false);

            Assert.Equal(Ground + C172Cg + 5.0, result.Altitude, 9);
            Assert.Equal(GroundPlacement.State.Ramp, result.State);
        }

        [Theory]
        [InlineData(30)]
        [InlineData(120)]
        public void The_ramp_reaches_the_ground_in_two_seconds_and_the_air_in_ten_at_any_frame_rate(int fps)
        {
            double dt = 1.0 / fps;
            double weight = 0.0;
            int frames = 0;
            while (weight < 1.0)
            {
                weight = GroundPlacement.Ramp(weight, true, dt);
                frames++;
            }
            Assert.InRange(frames * dt, 2.0 - dt, 2.0 + dt);

            frames = 0;
            while (weight > 0.0)
            {
                weight = GroundPlacement.Ramp(weight, false, dt);
                frames++;
            }
            Assert.InRange(frames * dt, 10.0 - dt, 10.0 + dt);
        }

        [Fact]
        public void The_first_update_goes_straight_to_its_phase()
        {
            Assert.Equal(1.0, GroundPlacement.Ramp(double.NaN, true, 0.0));
            Assert.Equal(0.0, GroundPlacement.Ramp(double.NaN, false, 0.0));
        }

        [Fact]
        public void A_long_gap_does_not_overshoot_and_a_clock_going_back_does_not_move_it()
        {
            Assert.Equal(1.0, GroundPlacement.Ramp(0.2, true, 60.0));
            Assert.Equal(0.0, GroundPlacement.Ramp(0.8, false, 60.0));
            Assert.Equal(0.4, GroundPlacement.Ramp(0.4, true, -1.0));
        }

        [Theory]
        [InlineData(double.NaN)]
        [InlineData(0.0)]
        [InlineData(0.03)]
        public void A_cg_reading_that_has_not_arrived_is_guessed_not_taken_as_zero(double reading)
        {
            double cg = GroundPlacement.CgHeight(reading, "L2J", out GroundPlacement.CgSource source);

            Assert.Equal(GroundPlacement.CgSource.Guess, source);
            Assert.Equal(2.5, cg);
        }

        [Fact]
        public void A_cg_the_sim_reports_is_used()
        {
            double cg = GroundPlacement.CgHeight(B737Cg, "L2J", out GroundPlacement.CgSource source);

            Assert.Equal(GroundPlacement.CgSource.Sim, source);
            Assert.Equal(B737Cg, cg);
        }

        [Theory]
        [InlineData("L1P", 1.5)]
        [InlineData("L2P", 2.0)]
        [InlineData("L2T", 2.0)]
        [InlineData("L2J", 2.5)]
        [InlineData("L4T", 4.0)]
        [InlineData("L4J", 6.0)]
        [InlineData("H1T", 1.5)]
        [InlineData("", 2.5)]
        [InlineData(null, 2.5)]
        [InlineData("LCJ", 2.5)]
        public void The_cg_is_guessed_by_aircraft_class(string classCode, double expected)
        {
            Assert.Equal(expected, GroundPlacement.GuessCgHeight(classCode));
        }
    }
}
