using System.Buffers.Binary;
using JoinFS.Net;
using JoinFS.Net.Jfp2;
using JoinFS.Net.Jfp2.Codecs;
using JoinFS.Net.Legacy;

namespace JoinFS.Tests.Net
{
    /// <summary>
    /// Builds that speak different JFP2 versions, side by side: each node gets its own profile
    /// (<see cref="Jfp2Profile"/>), as an older or a newer build would have.
    /// </summary>
    public class Jfp2VersionSkewTests
    {
        /// <summary>
        /// A Position version 2 for tests only: version 1's layout and a marker byte, so the size of a
        /// datagram tells which version travelled, and decoding checks it got the version it expects.
        /// </summary>
        sealed class PositionV2TestCodec : ICodec<PositionUpdate>
        {
            public const byte Marker = 0xB2;
            public const int Size = PositionV1Codec.Size + 1;
            static readonly PositionV1Codec V1 = new();

            public byte MessageClass => MessageClasses.Position;
            public byte SchemaVersion => 2;

            public int Encode(in PositionUpdate value, Span<byte> dest)
            {
                int length = V1.Encode(value, dest);
                dest[length] = Marker;
                return length + 1;
            }

            public PositionUpdate Decode(ReadOnlySpan<byte> src)
            {
                if (src.Length != Size || src[Size - 1] != Marker) throw new InvalidOperationException("not a test Position v2");
                return V1.Decode(src);
            }
        }

        /// <summary>A newer build: this build's classes, with Position at versions 1 and 2.</summary>
        static readonly Jfp2Profile PositionV2 = Jfp2Profile.Default.With(
            Jfp2Profile.Default.ForKind<PositionUpdate>(MessageKind.Position).WithCodecs(new PositionV1Codec(), new PositionV2TestCodec()));

        /// <summary>An older build: one that has no Notes class yet.</summary>
        static readonly Jfp2Profile NoNotes = Jfp2Profile.Default.Without(MessageClasses.Notes);

        static TestNode Node(TestMesh mesh, string ip, Jfp2Profile profile) => mesh.Add(ip, 6112, new LegacyPlugin(), new Jfp2Plugin(profile: profile));

        static Jfp2Plugin Jfp2Of(TestNode node) => node.Core.Plugins.OfType<Jfp2Plugin>().Single();

        static void SendPosition(TestNode from, TestNode to, uint id, double latitude)
        {
            from.Core.Objects.SetIdentity(from.Id, new IdentityUpdate
            {
                ObjectId = id, IsAircraft = true, IsPlane = true, Callsign = "SK" + id, Model = "M", Livery = "", IcaoType = "C172",
                IcaoAirline = "", Registration = "", FlightNumber = "", ClassCode = "", Wtc = "", TypeRole = 1,
            });
            from.Core.SendTo(to.Id, new PositionUpdate { ObjectId = id, NetTime = 1, Latitude = latitude, StateFlags = PositionStateFlags.UserControlled }, false);
        }

        static NotesBundle Note(string text) => new()
        {
            Scope = CommsScope.Single,
            Users = [new NotesUser { Guid = Guid.NewGuid(), Nickname = "N", Callsign = "C", Notes = [new CommsNote { NoteId = 1, Channel = 1, Text = text }] }],
        };

        /// <summary>Payload sizes of the JFP2 Position datagrams one node sent another.</summary>
        static List<int> PositionPayloads(TestMesh mesh, TestNode from, TestNode to) =>
            mesh.Network.Log.Where(d => d.From.Equals(from.EndPoint) && d.To.Equals(to.EndPoint) && d.Data[0] == Envelope.Magic)
                .Select(d => (Envelope: Envelope.ReadFrom(d.Data, out int consumed), Payload: d.Data.Length - consumed))
                .Where(d => !d.Envelope.IsInternal && d.Envelope.RawMessageClass == MessageClasses.Position)
                .Select(d => d.Payload).ToList();

        [Fact]
        public void Profile_OffersAreItsClasses()
        {
            Assert.Equal(PositionV2.Offers.Count, Jfp2Profile.Default.Offers.Count);
            SchemaOffer position = PositionV2.Offers.Single(o => o.MessageClass == MessageClasses.Position);
            Assert.Equal((1, 2), (position.MinVersion, position.MaxVersion));
            Assert.Equal(Jfp2Profile.Default.Offers.Select(o => o.MessageClass), PositionV2.Offers.Select(o => o.MessageClass)); // replaced in place
            Assert.DoesNotContain(NoNotes.Offers, o => o.MessageClass == MessageClasses.Notes);
            Assert.Null(NoNotes.ForKind(MessageKind.Notes));
        }

        [Fact]
        public void OlderAndNewerBuilds_AgreeWhatEachPairSpeaks_AndAClassOneLacksGoesOverLegacyForThatPairOnly()
        {
            var mesh = new TestMesh();
            TestNode hub = Node(mesh, "203.0.113.1", PositionV2);
            TestNode older = Node(mesh, "198.51.100.2", NoNotes);
            TestNode newer = Node(mesh, "192.0.2.3", PositionV2);
            hub.Core.Mesh.Create(false, 0, false, "");
            older.Core.Mesh.Join(hub.EndPoint, 0);
            newer.Core.Mesh.Join(hub.EndPoint, 0);
            mesh.Run(25);

            // the highest version both sides of each pair speak
            Assert.Equal(1, Jfp2Of(hub).VersionFor(older.Id, MessageKind.Position));
            Assert.Equal(1, Jfp2Of(older).VersionFor(hub.Id, MessageKind.Position));
            Assert.Equal(2, Jfp2Of(hub).VersionFor(newer.Id, MessageKind.Position));
            Assert.Equal(2, Jfp2Of(newer).VersionFor(hub.Id, MessageKind.Position));
            Assert.Equal(1, Jfp2Of(newer).VersionFor(older.Id, MessageKind.Position));

            // Notes: not with the older build, in either direction; still JFP2 between the others
            Assert.Equal(0, Jfp2Of(hub).VersionFor(older.Id, MessageKind.Notes));
            Assert.Equal("Legacy", hub.Core.Route(older.Id, MessageKind.Notes)!.Name);
            Assert.Equal("Legacy", older.Core.Route(hub.Id, MessageKind.Notes)!.Name);
            Assert.Equal("Legacy", newer.Core.Route(older.Id, MessageKind.Notes)!.Name);
            Assert.Equal("JFP2", hub.Core.Route(newer.Id, MessageKind.Notes)!.Name);
            Assert.Equal("JFP2", newer.Core.Route(hub.Id, MessageKind.Notes)!.Name);
            Assert.Equal("JFP2", hub.Core.Route(older.Id, MessageKind.Position)!.Name);

            mesh.Network.Log.Clear();
            hub.Core.SendTo(older.Id, Note("to older"), true);
            hub.Core.SendTo(newer.Id, Note("to newer"), true);
            SendPosition(hub, older, 1, 10);
            SendPosition(hub, newer, 1, 20);
            mesh.Run(1);

            Assert.Equal("to older", Assert.Single(older.Messages<NotesBundle>()).Users[0].Notes[0].Text);
            Assert.Equal("to newer", Assert.Single(newer.Messages<NotesBundle>()).Users[0].Notes[0].Text);
            Assert.Equal(10, Assert.Single(older.Messages<PositionUpdate>()).Latitude);
            Assert.Equal(20, Assert.Single(newer.Messages<PositionUpdate>()).Latitude);
            Assert.Equal([PositionV1Codec.Size], PositionPayloads(mesh, hub, older));
            Assert.Equal([PositionV2TestCodec.Size], PositionPayloads(mesh, hub, newer));
        }

        /// <summary>
        /// The capabilities a build advertises are its profile's (§5.4): none in this build, but builds
        /// that differ in them agree, per pair, the bits both advertise, and keep every class agreement.
        /// </summary>
        [Fact]
        public void BuildsWithOtherCapabilities_AgreeTheBitsBothAdvertise()
        {
            const ulong x = 1UL << 0, y = 1UL << 1;
            Assert.Equal(0UL, Jfp2Profile.Default.Capabilities);
            Assert.Equal(x | y, NoNotes.WithCapabilities(x | y).Without(MessageClasses.Event).Capabilities); // kept by With/Without
            var mesh = new TestMesh();
            TestNode hub = Node(mesh, "203.0.113.1", Jfp2Profile.Default.WithCapabilities(x | y));
            TestNode a = Node(mesh, "198.51.100.2", Jfp2Profile.Default.WithCapabilities(x));
            TestNode b = Node(mesh, "192.0.2.3", Jfp2Profile.Default);
            hub.Core.Mesh.Create(false, 0, false, "");
            a.Core.Mesh.Join(hub.EndPoint, 0);
            b.Core.Mesh.Join(hub.EndPoint, 0);
            mesh.Run(25);

            Assert.Equal(x, Jfp2Of(hub).AgreedCapabilitiesWith(a.Id));
            Assert.Equal(x, Jfp2Of(a).AgreedCapabilitiesWith(hub.Id));
            Assert.Equal(0UL, Jfp2Of(hub).AgreedCapabilitiesWith(b.Id));
            Assert.Equal(0UL, Jfp2Of(a).AgreedCapabilitiesWith(b.Id));
            Assert.Equal(1, Jfp2Of(a).VersionFor(hub.Id, MessageKind.Position));
            Assert.Equal(1, Jfp2Of(b).VersionFor(hub.Id, MessageKind.Notes));
        }

        /// <summary>
        /// Spec §4.5 and §7.7: the relay decodes with the version the sender's hop agreed, and the core
        /// re-sends it encoded in the version the target agreed.
        /// </summary>
        [Fact]
        public void RelayBetweenVersions_DecodesTheSendersVersionAndReencodesTheTargets()
        {
            var mesh = new TestMesh();
            TestNode hub = Node(mesh, "203.0.113.1", PositionV2);
            TestNode a = Node(mesh, "198.51.100.2", PositionV2);
            TestNode b = Node(mesh, "192.0.2.3", Jfp2Profile.Default);
            mesh.Partition(a, b); // they can only reach each other through the hub
            hub.Core.Mesh.Create(false, 0, false, "");
            a.Core.Mesh.Join(hub.EndPoint, 0);
            b.Core.Mesh.Join(hub.EndPoint, 0);
            mesh.Run(25);

            Assert.Equal(hub.Id, Jfp2Of(a).NextHopNode(b.Id));
            Assert.Equal(2, Jfp2Of(a).VersionFor(b.Id, MessageKind.Position)); // the hop's version
            Assert.Equal(1, Jfp2Of(hub).VersionFor(b.Id, MessageKind.Position));

            mesh.Network.Log.Clear();
            SendPosition(a, b, 7, 33.25);
            mesh.Run(0.5);

            Assert.Equal([PositionV2TestCodec.Size], PositionPayloads(mesh, a, hub));
            Assert.Equal([PositionV1Codec.Size], PositionPayloads(mesh, hub, b));
            var (meta, position) = Assert.Single(b.MessagesWithMeta<PositionUpdate>());
            Assert.Equal(a.Id, meta.Sender);
            Assert.Equal(33.25, position.Latitude);
            Assert.Equal(7u, position.ObjectId);
            Assert.Equal("SK7", Assert.Single(b.Messages<IdentityUpdate>()).Callsign);
            Assert.Empty(hub.Messages<PositionUpdate>());
        }
    }
}
