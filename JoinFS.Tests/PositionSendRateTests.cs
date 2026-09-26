using JoinFS.Net;

namespace JoinFS.Tests
{
    /// <summary>
    /// With SimConnect feeds (docs/sim-thread-architecture.md, Phase 3) positions can arrive every
    /// frame, but the network and the recorder must keep getting them at Sim.PositionSendInterval.
    /// </summary>
    public class PositionSendRateTests
    {
        static int SendsOver(double seconds, double frameInterval, double start = 100.0)
        {
            var plane = new Sim.Plane(new NodeId(0x0A000001, 6112, 1), 1);
            int sends = 0;
            int frames = (int)Math.Round(seconds / frameInterval);
            for (int frame = 0; frame < frames; frame++)
            {
                if (Sim.SendDue(plane, start + frame * frameInterval)) sends++;
            }
            return sends;
        }

        [Theory]
        [InlineData(1.0 / 144)]
        [InlineData(1.0 / 60)]
        [InlineData(1.0 / 30)]
        public void FastFeeds_AreSentAtAbout20Hz(double frameInterval)
        {
            int sends = SendsOver(10.0, frameInterval);
            // 20 Hz on average; a frame can land just after each 50 ms boundary, never earlier
            Assert.InRange(sends, 190, 201);
        }

        [Fact]
        public void SlowerFeeds_AreSentWhenTheyArrive()
        {
            // the X-Plane plugin reports at 10 Hz: every report goes out
            Assert.Equal(100, SendsOver(10.0, 0.1));
        }

        [Fact]
        public void AfterAGap_SendingRestartsWithoutABurst()
        {
            var plane = new Sim.Plane(new NodeId(0x0A000001, 6112, 1), 1);
            Assert.True(Sim.SendDue(plane, 100.0));
            // nothing for 5 s (paused, loading), then frames again
            Assert.True(Sim.SendDue(plane, 105.0));
            Assert.False(Sim.SendDue(plane, 105.016));
            Assert.False(Sim.SendDue(plane, 105.033));
            Assert.True(Sim.SendDue(plane, 105.050));
        }
    }
}
