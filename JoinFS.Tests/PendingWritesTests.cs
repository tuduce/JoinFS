using Xunit;
using Pending = JoinFS.VariableMgr.PendingWrites<int>;

namespace JoinFS.Tests
{
    /// <summary>
    /// A write to the simulator waits for its echo: the echo must not count as a change made in
    /// the simulator, and the same value must not be written twice while it is on its way.
    /// </summary>
    public class PendingWritesTests
    {
        const uint Vuid = 42;

        static Pending Create() => new((vuid, a, b) => a == b);

        [Fact]
        public void LatestWrite_IsInFlight_UntilTimeout()
        {
            Pending pending = Create();
            pending.Written(Vuid, 1, 10.0);

            Assert.True(pending.InFlight(Vuid, 1, 10.5));
            Assert.False(pending.InFlight(Vuid, 1, 10.0 + Pending.TIMEOUT + 0.1));
        }

        [Fact]
        public void DifferentValue_IsNotInFlight()
        {
            Pending pending = Create();
            pending.Written(Vuid, 1, 10.0);

            Assert.False(pending.InFlight(Vuid, 0, 10.5));
        }

        [Fact]
        public void Echo_IsRecognisedOnce_AndEndsTheWrite()
        {
            Pending pending = Create();
            pending.Written(Vuid, 1, 10.0);

            Assert.True(pending.IsEcho(Vuid, 1));
            Assert.False(pending.IsEcho(Vuid, 1));
            Assert.False(pending.InFlight(Vuid, 1, 10.5));
        }

        [Fact]
        public void OtherValue_IsNotAnEcho()
        {
            Pending pending = Create();
            pending.Written(Vuid, 1, 10.0);

            Assert.False(pending.IsEcho(Vuid, 7));
            Assert.True(pending.InFlight(Vuid, 1, 10.5));
        }

        [Fact]
        public void MovingLever_EchoOfAnEarlierWrite_KeepsTheLaterOnesPending()
        {
            Pending pending = Create();
            pending.Written(Vuid, 10, 10.0);
            pending.Written(Vuid, 20, 10.2);
            pending.Written(Vuid, 30, 10.4);

            Assert.True(pending.IsEcho(Vuid, 20));
            Assert.False(pending.IsEcho(Vuid, 10));
            Assert.True(pending.InFlight(Vuid, 30, 10.5));
            Assert.True(pending.IsEcho(Vuid, 30));
        }

        [Fact]
        public void OnlyTheLatestWrite_IsInFlight()
        {
            // the network went back to an earlier value: the sim will end on the latest write, so write again
            Pending pending = Create();
            pending.Written(Vuid, 10, 10.0);
            pending.Written(Vuid, 20, 10.2);

            Assert.False(pending.InFlight(Vuid, 10, 10.3));
        }

        [Fact]
        public void LateEcho_StillCounts()
        {
            Pending pending = Create();
            pending.Written(Vuid, 1, 10.0);

            Assert.True(pending.IsEcho(Vuid, 1));
        }
    }
}
