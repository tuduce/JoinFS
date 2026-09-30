using JoinFS.Net;
using JoinFS.Tests.Session;

namespace JoinFS.Tests.Net
{
    /// <summary>
    /// Every inbound message carries the time its datagram arrived (MessageMeta.ReceivedAt), and
    /// SimIngest hands it to the simulator with each position, so time spent in queues before a
    /// position is applied doesn't distort extrapolation (docs/sim-thread-architecture.md §2.7).
    /// </summary>
    public class ReceiveTimestampTests
    {
        static (TestMesh Mesh, TestNode A, TestNode B) Pair()
        {
            var mesh = new TestMesh();
            TestNode a = mesh.Add("198.51.100.2");
            TestNode b = mesh.Add("192.0.2.3");
            a.Core.Mesh.Create(false, 0, false, "");
            b.Core.Mesh.Join(a.EndPoint, 0);
            mesh.Run(3);
            return (mesh, a, b);
        }

        static IdentityUpdate Identity() => new()
        {
            ObjectId = 5, IsAircraft = true, IsPlane = true, Callsign = "N1", Model = "C172", Livery = "", IcaoType = "C172",
            IcaoAirline = "", Registration = "", FlightNumber = "", ClassCode = "", Wtc = "", TypeRole = 1,
        };

        /// <summary>Send a position from a to b (legacy carries it together with the object's identity).</summary>
        static void SendPosition(TestNode a, TestNode b)
        {
            a.Core.Objects.SetIdentity(a.Id, Identity());
            a.Core.SendTo(b.Id, Position(), false);
        }

        static PositionUpdate Position() => new() { ObjectId = 5, NetTime = 10, Latitude = 50, StateFlags = PositionStateFlags.UserControlled };

        [Fact]
        public void DeliveredMessage_WithoutExplicitTime_IsStampedWithTheClockWhenDecoded()
        {
            var (mesh, a, b) = Pair();
            double before = mesh.Clock.Now;
            SendPosition(a, b);
            mesh.Run(0.1);

            var (meta, _) = b.MessagesWithMeta<PositionUpdate>().Single();
            Assert.InRange(meta.ReceivedAt, before, mesh.Clock.Now);
        }

        [Fact]
        public void DeliveredMessage_CarriesTheArrivalTimeGivenWithTheDatagram()
        {
            var (mesh, a, b) = Pair();
            b.ReceiveTime = () => 42.5;
            SendPosition(a, b);
            mesh.Run(0.1);

            var (meta, _) = b.MessagesWithMeta<PositionUpdate>().Single();
            Assert.Equal(42.5, meta.ReceivedAt);
        }

        [Fact]
        public void SimIngest_PassesTheArrivalTimeWithEachPosition()
        {
            NodeId peer = new(0x0A000001, 6112, 1);
            var rig = new SessionRig();
            rig.Peers.OnPeerJoined(peer, peer.ToEndPoint(peer.port));
            MessageMeta meta = SessionRig.From(peer);
            meta.ReceivedAt = 12.25;
            rig.Ingest.Handle(meta, Identity());
            rig.Ingest.Handle(meta, Position());

            Assert.Single(rig.Sim.Aircraft);
            Assert.Equal(12.25, Assert.Single(rig.Sim.PositionReceivedAt));
        }
    }
}
