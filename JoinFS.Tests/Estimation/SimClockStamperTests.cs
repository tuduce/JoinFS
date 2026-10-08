using JoinFS.Estimation;

namespace JoinFS.Tests.Estimation
{
    public class SimClockStamperTests
    {
        /// <summary>
        /// A simulator running at 60 frames/s whose clock starts at <paramref name="simStart"/>,
        /// with each frame's message handled 2 to 16 ms later on our clock (which reads 1000 at the
        /// first frame)
        /// </summary>
        sealed class Feed(double simStart = 300.0, int seed = 1)
        {
            readonly Random random = new(seed);
            public double Sim = simStart;
            /// <summary>Our clock at the frame</summary>
            public double Taken = 1000.0;
            public double Handled;

            public void Next(double frame = 1.0 / 60.0, double simAdvance = double.NaN)
            {
                Taken += frame;
                Sim += double.IsNaN(simAdvance) ? frame : simAdvance;
            }

            public double Stamp(SimClockStamper stamper)
            {
                Handled = Taken + 0.002 + 0.014 * random.NextDouble();
                return stamper.Stamp(Sim, Handled);
            }
        }

        [Fact]
        public void WithoutASimulatorClock_StampsTheHandlingTime()
        {
            var stamper = new SimClockStamper();
            Assert.Equal(10.0, stamper.Stamp(double.NaN, 10.0));
            Assert.Equal(10.02, stamper.Stamp(double.NaN, 10.02));
            // the X-Plane link re-sends an old sample with its old time: passed on as it is
            Assert.Equal(9.5, stamper.Stamp(double.NaN, 9.5));
        }

        [Fact]
        public void RemovesTheHandlingJitter()
        {
            var stamper = new SimClockStamper();
            var feed = new Feed();
            var errors = new List<double>();
            for (int frame = 0; frame < 600; frame++)
            {
                double stamp = feed.Stamp(stamper);
                Assert.True(stamp <= feed.Handled);
                // once the window has seen enough frames
                if (frame > 60)
                {
                    errors.Add(stamp - feed.Taken);
                }
                feed.Next();
            }
            // the handling time spreads over 14 ms; the stamps keep the least delay, nearly constant
            Assert.InRange(errors.Max() - errors.Min(), 0.0, 0.002);
            Assert.InRange(errors.Min(), 0.002, 0.004);
        }

        [Fact]
        public void Stamps_OnlyMoveForward()
        {
            var stamper = new SimClockStamper();
            var feed = new Feed(seed: 7);
            double last = double.NegativeInfinity;
            for (int frame = 0; frame < 2000; frame++)
            {
                // with paused stretches, frames the simulator clock repeats, and a hitch
                double simAdvance = frame % 300 < 30 ? 0.0 : frame % 97 == 0 ? 0.0 : frame == 1000 ? 0.2 : double.NaN;
                feed.Next(frame == 1000 ? 0.5 : 1.0 / 60.0, simAdvance);
                double stamp = feed.Stamp(stamper);
                Assert.True(stamp > last, $"frame {frame}: {stamp} after {last}");
                last = stamp;
            }
        }

        [Fact]
        public void WhenTheSimulatorClockStandsStill_StampsTheHandlingTime_ThenStartsOver()
        {
            var stamper = new SimClockStamper();
            var feed = new Feed();
            for (int frame = 0; frame < 120; frame++)
            {
                feed.Stamp(stamper);
                feed.Next();
            }
            feed.Stamp(stamper);
            // paused for two seconds: the simulator's clock stands still
            for (int frame = 0; frame < 120; frame++)
            {
                feed.Next(simAdvance: 0.0);
                double stamp = feed.Stamp(stamper);
                Assert.Equal(feed.Handled, stamp, 1e-12);
            }
            feed.Next();
            // running again: the clock is now two seconds behind ours, which must not age the samples
            for (int frame = 0; frame < 120; frame++)
            {
                double stamp = feed.Stamp(stamper);
                Assert.InRange(feed.Handled - stamp, 0.0, 0.016);
                Assert.InRange(stamp - feed.Taken, 0.0, 0.016);
                feed.Next();
            }
        }

        [Fact]
        public void WhenTheSimulatorClockFallsBehind_FollowsAtOnce()
        {
            // a 300 ms hitch the simulator only counts as 100 ms of simulated time
            var stamper = new SimClockStamper();
            var feed = new Feed();
            for (int frame = 0; frame < 120; frame++)
            {
                feed.Stamp(stamper);
                feed.Next();
            }
            feed.Next(0.3, 0.1);
            for (int frame = 0; frame < 60; frame++)
            {
                double stamp = feed.Stamp(stamper);
                Assert.InRange(stamp - feed.Taken, 0.0, 0.016);
                feed.Next();
            }
        }

        [Fact]
        public void WhenTheSimulatorClockJumpsBack_StartsOver()
        {
            // a flight reloaded: the simulator's clock restarts near zero
            var stamper = new SimClockStamper();
            var feed = new Feed();
            for (int frame = 0; frame < 120; frame++)
            {
                feed.Stamp(stamper);
                feed.Next();
            }
            feed.Sim = 1.0;
            double reloaded = feed.Stamp(stamper);
            Assert.Equal(feed.Handled, reloaded, 1e-12);
            for (int frame = 0; frame < 60; frame++)
            {
                feed.Next();
                double stamp = feed.Stamp(stamper);
                Assert.InRange(stamp - feed.Taken, 0.0, 0.016);
            }
        }
    }
}
