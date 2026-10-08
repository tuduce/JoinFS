using System.Buffers.Binary;
using System.Net;
using JoinFS.Net;
using JoinFS.Net.Legacy;

namespace JoinFS.Tests.Net
{
    /// <summary>
    /// <see cref="NetworkCore.Deliver{T}"/> with mesh messages: one addressed to another node goes on
    /// to it through the translation path, like an application message; one for this node, or for no
    /// node in particular, is the local mesh's. Today no plugin hands the core a mesh message for
    /// another node (legacy relays them itself, JFP2 carries none); a JFP2 mesh would.
    /// </summary>
    public class NetworkCoreDeliverTests
    {
        /// <summary>Carries everything and only records what it is asked to send.</summary>
        sealed class RecordingPlugin : IProtocolPlugin
        {
            public readonly List<(MessageMeta Meta, object Message, NodeId[] Recipients)> Sent = [];

            public string Name => "Recording";
            public int Preference => 100;
            public void Attach(IProtocolHost host) { }
            public bool Accepts(ReadOnlySpan<byte> datagram) => false;
            public void OnDatagram(IPEndPoint from, ReadOnlySpan<byte> datagram) { }
            public void Tick() { }
            public bool CanCarry(NodeId peer, MessageKind kind) => true;
            public void Send<T>(in MessageMeta meta, in T message, ReadOnlySpan<NodeId> recipients) where T : struct, IMessage =>
                Sent.Add((meta, message, recipients.ToArray()));
            public void OnPeerRemoved(Peer peer) { }
            public void OnSessionReset() { }
        }

        static readonly IPEndPoint OriginEndPoint = new(IPAddress.Parse("198.51.100.2"), 6112);
        static readonly NodeId Origin = new(OriginEndPoint, 2);
        static readonly NodeId Other = new(IPAddress.Parse("192.0.2.3"), 6112, 3);

        static (TestNode Node, RecordingPlugin Plugin) InSession()
        {
            var plugin = new RecordingPlugin();
            TestNode node = new TestMesh().Add("203.0.113.1", 6112, plugin);
            node.Core.Mesh.Create(false, 0, false, "");
            return (node, plugin);
        }

        static Pulse PulseOf(TestNode node) => new() { Suid = node.Core.Mesh.Suid, Time = 42 };

        [Fact]
        public void MeshMessageForAnotherNode_IsSentOnToIt_NotHandledHere()
        {
            var (node, plugin) = InSession();

            node.Core.Deliver(new MessageMeta { Sender = Origin, Recipient = Other, EndPoint = OriginEndPoint, Guaranteed = true }, PulseOf(node));

            var (meta, message, recipients) = Assert.Single(plugin.Sent);
            Assert.Equal(42, Assert.IsType<Pulse>(message).Time);
            Assert.Equal([Other], recipients);
            Assert.Equal(Origin, meta.Sender); // on the origin's behalf
            Assert.Null(meta.EndPoint);
            Assert.True(meta.Guaranteed);
            Assert.False(node.Core.Peers.Contains(Origin)); // the local mesh never saw it
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void MeshMessageForThisNodeOrUnaddressed_IsTheLocalMeshs(bool addressedHere)
        {
            var (node, plugin) = InSession();

            node.Core.Deliver(new MessageMeta { Sender = Origin, Recipient = addressedHere ? node.Id : default, EndPoint = OriginEndPoint }, PulseOf(node));

            Assert.True(node.Core.Peers.Contains(Origin)); // registered by the mesh, which answers it
            Assert.Contains(plugin.Sent, s => s.Message is PulseResponse { Time: 42 } && OriginEndPoint.Equals(s.Meta.EndPoint));
            Assert.DoesNotContain(plugin.Sent, s => s.Message is Pulse);
        }

        [Fact]
        public void MeshMessageBeforeThisNodeKnowsItsId_IsTheLocalMeshs()
        {
            var (node, plugin) = InSession();
            node.Core.Identity.Set(default);

            node.Core.Deliver(new MessageMeta { Sender = Origin, Recipient = Other, EndPoint = OriginEndPoint }, new JoinFail { Result = JoinResult.PasswordRequired });

            Assert.Empty(plugin.Sent);
            Assert.Equal(JoinResult.PasswordRequired, node.Core.Mesh.ActiveJoinResult);
        }

        /// <summary>Through legacy, the re-sent message is shaped as a legacy relay: the origin in the header, Forward set.</summary>
        [Fact]
        public void MeshMessageForAnotherNode_GoesOutOverLegacyAsARelay()
        {
            var mesh = new TestMesh();
            TestNode hub = mesh.Add("203.0.113.1");
            TestNode a = mesh.Add("198.51.100.2");
            TestNode b = mesh.Add("192.0.2.3");
            hub.Core.Mesh.Create(false, 0, false, "");
            a.Core.Mesh.Join(hub.EndPoint, 0);
            b.Core.Mesh.Join(hub.EndPoint, 0);
            mesh.Run(5);
            mesh.Network.Log.Clear();

            hub.Core.Deliver(new MessageMeta { Sender = a.Id, Recipient = b.Id, EndPoint = a.EndPoint }, PulseOf(hub));

            var (_, to, data) = Assert.Single(mesh.Network.Log);
            Assert.Equal(b.EndPoint, to);
            Assert.Equal(LegacyWire.FlagInternal | LegacyWire.FlagForward, data[LegacyWire.FlagsOffset]);
            Assert.Equal(a.Id, NodeId.Read(data.AsSpan(LegacyWire.SenderOffset)));
            Assert.Equal(b.Id, NodeId.Read(data.AsSpan(LegacyWire.RecipientOffset)));
            Assert.Equal((short)LegacyWire.InternalId.Pulse, BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(LegacyWire.DataOffset)));
        }
    }
}
