using JoinFS.Estimation;

namespace JoinFS.Tests.Estimation
{
    public class MinOffsetClockTests
    {
        const double Delay = 0.020;
        static readonly PeerTiming Link = new(true, 0.040f);

        /// <summary>Feed 20 Hz samples for <paramref name="seconds"/>; the sender's clock is <paramref name="offset"/> behind ours</summary>
        static double Run(MinOffsetClock clock, double start, double seconds, double offset, Func<int, double> extraDelay = null)
        {
            double arrival = start;
            for (int i = 0; i < seconds * 20; i++)
            {
                double netTime = start + i * 0.05 - offset;
                arrival = start + i * 0.05 + Delay + (extraDelay?.Invoke(i) ?? 0.0);
                clock.OnSample(netTime, arrival, arrival);
                // the frame loop asks every frame, which also feeds the round-trip window
                clock.SampleAge(arrival, Link);
            }
            return arrival;
        }

        [Fact]
        public void SteadyStream_AgeIsTheTimeSinceArrivalPlusHalfTheRoundTrip()
        {
            var clock = new MinOffsetClock();
            double last = Run(clock, 100.0, 30.0, offset: 7.3);
            Assert.True(clock.Started);
            Assert.Equal(0.020, clock.SampleAge(last, Link), 6);
            Assert.Equal(0.020 + 0.016, clock.SampleAge(last + 0.016, Link), 6);
        }

        [Fact]
        public void ALateSample_ReportsItsExtraDelayOnce()
        {
            var clock = new MinOffsetClock();
            double last = Run(clock, 100.0, 20.0, 0.0);
            // the next sample takes 6 ms longer than the fastest
            double arrival = last + 0.05 + 0.006;
            clock.OnSample(last + 0.05 - Delay, arrival, arrival);
            Assert.Equal(0.020 + 0.006, clock.SampleAge(arrival, Link), 6);
            // and the one after is back to normal
            double next = arrival + 0.05 - 0.006;
            clock.OnSample(last + 0.10 - Delay, next, next);
            Assert.Equal(0.020, clock.SampleAge(next, Link), 6);
        }

        [Fact]
        public void TheOffsetIsLearnedFromTheFastestSamples_NotTheAverage()
        {
            var clock = new MinOffsetClock();
            // every fifth sample is on time, the others 4 ms late
            double last = Run(clock, 100.0, 20.0, 0.0, i => i % 5 == 0 ? 0.0 : 0.004);
            int n = (int)(20.0 * 20) - 1;
            double expected = 0.020 + (n % 5 == 0 ? 0.0 : 0.004);
            Assert.Equal(expected, clock.SampleAge(last, Link), 6);
        }

        [Fact]
        public void ADelayThatStaysIsForgottenAfterTheWindow()
        {
            var clock = new MinOffsetClock();
            double last = Run(clock, 100.0, 20.0, 0.0);
            // the path gets 40 ms slower for good (the round trip too, but that is read over a minute)
            last = Run(clock, last + 0.05, 15.0, 0.0, _ => 0.040);
            Assert.Equal(0.020, clock.SampleAge(last, Link), 4);
        }

        [Fact]
        public void ASteppedSenderClock_RestartsTheWindow()
        {
            var clock = new MinOffsetClock();
            double last = Run(clock, 100.0, 20.0, 0.0);
            // the sender's clock restarts 5 s behind: arrival minus stamp jumps up by 5 s
            for (int i = 1; i <= MinOffsetClock.RestartCount + 1; i++)
            {
                double arrival = last + i * 0.05;
                clock.OnSample(arrival - Delay - 5.0, arrival, arrival);
            }
            double end = last + (MinOffsetClock.RestartCount + 1) * 0.05;
            Assert.Equal(0.020, clock.SampleAge(end, Link), 6);
        }

        [Fact]
        public void ARoundTripThatIsNotKnown_IsIgnored()
        {
            var clock = new MinOffsetClock();
            clock.OnSample(10.0, 10.02, 10.02);
            Assert.Equal(0.0, clock.SampleAge(10.02, new PeerTiming(true, 0.0f)), 9);
            Assert.Equal(0.0, clock.SampleAge(10.02, new PeerTiming(true, 9999.0f)), 9);
            Assert.Equal(0.0, clock.SampleAge(10.02, PeerTiming.None), 9);
            // once one is known the smallest of the last minute is used
            clock.SampleAge(10.02, new PeerTiming(true, 0.060f));
            clock.SampleAge(11.02, new PeerTiming(true, 0.044f));
            clock.SampleAge(12.02, new PeerTiming(true, 0.090f));
            Assert.Equal(0.022 + 2.0, clock.SampleAge(12.02, new PeerTiming(true, 0.090f)), 6);
            // the minute passes
            clock.SampleAge(80.0, new PeerTiming(true, 0.090f));
            Assert.Equal(0.045 + (80.0 - 10.02), clock.SampleAge(80.0, new PeerTiming(true, 0.090f)), 6);
        }

        [Fact]
        public void SamplesNotOffTheNetwork_AgeFromWhenTheyWereHandled()
        {
            var clock = new MinOffsetClock();
            // recorder playback: no arrival time, no link
            clock.OnSample(5.0, 0.0, 20.0);
            clock.OnSample(5.05, 0.0, 20.05);
            Assert.Equal(0.03, clock.SampleAge(20.08, PeerTiming.None), 9);
        }

        [Fact]
        public void Reset_ForgetsTheSender_ButNotTheRoundTrip()
        {
            var clock = new MinOffsetClock();
            Run(clock, 100.0, 5.0, 0.0);
            clock.Reset();
            Assert.False(clock.Started);
            clock.OnSample(500.0, 200.0, 200.0);
            // a new sender clock: the first sample is the fastest, and the round trip is still known
            Assert.Equal(0.020, clock.SampleAge(200.0, Link), 6);
        }

        [Fact]
        public void CopyFrom_TakesOverTheSendersClock_AndStaysApart()
        {
            var a = new MinOffsetClock();
            double last = Run(a, 100.0, 12.0, 1.0);
            var b = new MinOffsetClock();
            b.CopyFrom(a);
            Assert.True(b.Started);
            Assert.Equal(a.SampleAge(last + 0.01, Link), b.SampleAge(last + 0.01, Link), 9);
            // a later sample on one does not reach the other
            a.OnSample(last + 0.05 - Delay - 1.0, last + 0.15, last + 0.15);
            Assert.NotEqual(a.SampleAge(last + 0.15, Link), b.SampleAge(last + 0.15, Link));
            // a clock of another model has nothing to take over
            var other = new MinOffsetClock();
            other.CopyFrom(new RttHalfClock());
            Assert.False(other.Started);
        }

        [Fact]
        public void Registry_CreatesItByName_AndKeepsTheDefault()
        {
            Assert.IsType<MinOffsetClock>(EstimationRegistry.CreateClock(MinOffsetClock.Name));
            Assert.Contains(MinOffsetClock.Name, EstimationRegistry.ClockNames);
            string selected = EstimationRegistry.SelectedClock;
            Assert.False(EstimationRegistry.SelectClock("NoSuchClock"));
            Assert.False(EstimationRegistry.SelectClock(null));
            Assert.Equal(selected, EstimationRegistry.SelectedClock);
            Assert.IsType<RttHalfClock>(EstimationRegistry.CreateClock());
        }
    }
}
