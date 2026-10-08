using JoinFS.Estimation;

namespace JoinFS.Tests.Estimation
{
    public class SteeringLawTests
    {
        const double MetresPerRadian = 6371009.0;

        /// <summary>An object <paramref name="below"/> metres under a target that has no velocity, both level</summary>
        static Sim.ObjectVelocity VelocityFor(ISteeringLaw law, double below, double east = 0.0)
        {
            var net = new Sim.Pos(new Vector(0.1 + east / (MetresPerRadian * Math.Cos(0.8)), 1500.0, 0.8), new Vector(0.0, 1.0, 0.0), 0.0, 0);
            var sim = new Sim.Pos(new Vector(0.1, 1500.0 - below, 0.8), new Vector(0.0, 1.0, 0.0), 0.0, 0);
            var state = new KinematicState(net, new Sim.Vel(new Vector(0.0, 0.0, 0.0), new Vector(0.0, 0.0, 0.0), new Vector(0.0, 0.0, 0.0)));
            return law.Steer(state, state, sim, 0.0).Velocity;
        }

        [Theory]
        [InlineData("Classic", 1.5)]
        [InlineData("Gain4", 4.0)]
        [InlineData("Gain8", 8.0)]
        [InlineData("Gain16", 16.0)]
        public void TheCatchUpRate_IsTheGainOfTheLaw(string name, double gain)
        {
            ISteeringLaw law = EstimationRegistry.CreateSteering(name, setAttitudeEveryFrame: true, groundAltitudeLimit: 0.2);
            Assert.Equal(gain * 2.0, VelocityFor(law, below: 2.0).velocityY, 1e-4);
            // sideways too (the heading is 1 rad, so the offset is not along one axis: the speed is what counts)
            var v = VelocityFor(law, below: 0.0, east: 3.0);
            Assert.Equal(gain * 3.0, Math.Sqrt(v.velocityX * v.velocityX + v.velocityZ * v.velocityZ), 1e-3);
        }

        [Fact]
        public void TheDefault_IsGain4_AndClassicKeepsTheOriginalRate()
        {
            var law = new ClassicSteering(true, 0.2);
            Assert.Equal(ClassicSteering.CatchUpRate * 2.0, VelocityFor(law, 2.0).velocityY, 1e-4);
            Assert.Equal("Gain4", EstimationRegistry.DefaultSteering);
            Assert.Equal("Gain4", EstimationRegistry.SelectedSteering);
        }

        [Fact]
        public void Registry_KnowsTheLaws_AndTakesAlternate()
        {
            Assert.Contains("Classic", EstimationRegistry.SteeringNames);
            Assert.Contains("Gain4", EstimationRegistry.SteeringNames);
            Assert.All(EstimationRegistry.AlternatedSteering, name => Assert.Contains(name, EstimationRegistry.SteeringNames));
            string selected = EstimationRegistry.SelectedSteering;
            try
            {
                Assert.True(EstimationRegistry.SelectSteering("Gain4"));
                Assert.Equal("Gain4", EstimationRegistry.SelectedSteering);
                Assert.True(EstimationRegistry.SelectSteering(SteeringSchedule.Alternate));
                Assert.False(EstimationRegistry.SelectSteering("NoSuchLaw"));
                Assert.False(EstimationRegistry.SelectSteering(null));
                Assert.Equal(SteeringSchedule.Alternate, EstimationRegistry.SelectedSteering);
            }
            finally
            {
                EstimationRegistry.SelectSteering(selected);
            }
            // an unknown name falls back to the default one
            Assert.Equal(4.0 * 2.0, VelocityFor(EstimationRegistry.CreateSteering("NoSuchLaw", true, 0.2), 2.0).velocityY, 1e-4);
        }

        [Fact]
        public void ASingleLaw_IsInForceAlways()
        {
            var schedule = new SteeringSchedule(name => EstimationRegistry.CreateSteering(name, true, 0.2), "Gain4");
            ISteeringLaw first = schedule.At(0.0, out string name);
            Assert.Equal("Gain4", name);
            Assert.Same(first, schedule.At(100000.0, out name));
            Assert.Equal("Gain4", name);
        }

        [Fact]
        public void Alternating_CyclesThroughEveryLaw_APeriodEach()
        {
            string[] names = EstimationRegistry.AlternatedSteering;
            var schedule = new SteeringSchedule(name => EstimationRegistry.CreateSteering(name, true, 0.2), SteeringSchedule.Alternate, period: 60.0);
            Assert.True(names.Length > 1);
            for (int cycle = 0; cycle < 2; cycle++)
            {
                for (int i = 0; i < names.Length; i++)
                {
                    double start = (cycle * names.Length + i) * 60.0;
                    schedule.At(start + 0.1, out string first);
                    schedule.At(start + 59.9, out string last);
                    Assert.Equal(names[i], first);
                    Assert.Equal(names[i], last);
                }
            }
            // before the clock starts: the first law, not a crash
            schedule.At(-5.0, out string early);
            Assert.Equal(names[0], early);
        }
    }
}
