using System.Buffers.Binary;
using System.Net;
using System.Text;
using JoinFS.Net;
using JoinFS.Net.Jfp2;
using JoinFS.Net.Jfp2.Codecs;
using JoinFS.Net.Legacy;
using JoinFS.Tests.Jfp2;

namespace JoinFS.Tests.Net
{
    /// <summary>
    /// The JFP2 plugin next to the legacy plugin in the new stack: negotiation, per-(peer, kind)
    /// routing, fallback to legacy, and translation of relayed JFP2 for legacy-only targets.
    /// </summary>
    public class Jfp2PluginTests
    {
        static TestNode Jfp2Node(TestMesh mesh, string ip) => mesh.Add(ip, 6112, new LegacyPlugin(), new Jfp2Plugin());
        static TestNode LegacyNode(TestMesh mesh, string ip) => mesh.Add(ip, 6112, new LegacyPlugin());

        static IdentityUpdate Identity(uint id, string callsign) => new()
        {
            ObjectId = id, IsAircraft = true, IsPlane = true, Callsign = callsign, Model = "M", Livery = "", IcaoType = "C172",
            IcaoAirline = "", Registration = "", FlightNumber = "", ClassCode = "", Wtc = "", TypeRole = 1,
        };

        static void SendPosition(TestNode from, TestNode to, uint id, string callsign, double latitude)
        {
            from.Core.Objects.SetIdentity(from.Id, Identity(id, callsign));
            from.Core.SendTo(to.Id, new PositionUpdate { ObjectId = id, NetTime = 1, Latitude = latitude, StateFlags = PositionStateFlags.UserControlled }, false);
        }

        static int Jfp2DatagramsBetween(TestMesh mesh, TestNode from, TestNode to) =>
            mesh.Network.Log.Count(d => d.From.Equals(from.EndPoint) && d.To.Equals(to.EndPoint) && d.Data[0] == Envelope.Magic);

        static Jfp2Plugin Jfp2Of(TestNode node) => node.Core.Plugins.OfType<Jfp2Plugin>().Single();

        [Fact]
        public void TwoJfp2Nodes_NegotiateAndSendPositionsOverJfp2()
        {
            var mesh = new TestMesh();
            TestNode hub = Jfp2Node(mesh, "203.0.113.1");
            TestNode a = Jfp2Node(mesh, "198.51.100.2");
            hub.Core.Mesh.Create(false, 0, false, "");
            a.Core.Mesh.Join(hub.EndPoint, 0);
            mesh.Run(3);

            Assert.True(Jfp2Of(a).IsNegotiated(hub.Id));
            Assert.Equal("JFP2", a.Core.Route(hub.Id, MessageKind.Position)!.Name);
            Assert.Equal("Legacy", a.Core.Route(hub.Id, MessageKind.RemoveObject)!.Name);

            mesh.Network.Log.Clear();
            SendPosition(a, hub, 3, "JF1", 12.5);
            mesh.Run(0.1);

            Assert.Equal(12.5, Assert.Single(hub.Messages<PositionUpdate>()).Latitude);
            Assert.Equal("JF1", Assert.Single(hub.Messages<IdentityUpdate>()).Callsign);
            Assert.Equal(2, Jfp2DatagramsBetween(mesh, a, hub)); // Identity, then Position

            // identity is only resent on change or heartbeat
            mesh.Network.Log.Clear();
            SendPosition(a, hub, 3, "JF1", 12.6);
            mesh.Run(0.1);
            Assert.Equal(1, Jfp2DatagramsBetween(mesh, a, hub));
        }

        [Fact]
        public void Jfp2Node_FallsBackToLegacyWithLegacyOnlyPeer()
        {
            var mesh = new TestMesh();
            TestNode hub = LegacyNode(mesh, "203.0.113.1");
            TestNode a = Jfp2Node(mesh, "198.51.100.2");
            hub.Core.Mesh.Create(false, 0, false, "");
            a.Core.Mesh.Join(hub.EndPoint, 0);
            mesh.Run(12);

            Assert.False(Jfp2Of(a).IsNegotiated(hub.Id));
            Assert.Equal("Legacy", a.Core.Route(hub.Id, MessageKind.Position)!.Name);
            Assert.Equal(PeerLinkState.Legacy, Jfp2Of(a).DescribeLink(a.Core.Peers.All.Single()));

            SendPosition(a, hub, 3, "LG1", 7);
            mesh.Run(0.1);
            Assert.Equal(7, Assert.Single(hub.Messages<PositionUpdate>()).Latitude);
            Assert.Equal("LG1", Assert.Single(hub.Messages<IdentityUpdate>()).Callsign);
        }

        [Fact]
        public void Broadcast_ReachesEachPeerInItsOwnProtocol()
        {
            var mesh = new TestMesh();
            TestNode hub = Jfp2Node(mesh, "203.0.113.1");
            TestNode modern = Jfp2Node(mesh, "198.51.100.2");
            TestNode old = LegacyNode(mesh, "192.0.2.3");
            hub.Core.Mesh.Create(false, 0, false, "");
            modern.Core.Mesh.Join(hub.EndPoint, 0);
            old.Core.Mesh.Join(hub.EndPoint, 0);
            mesh.Run(12);

            mesh.Network.Log.Clear();
            hub.Core.Broadcast(new EventUpdate { ObjectId = 1, EventId = 99, Data = 5 }, true);
            mesh.Run(0.5);

            Assert.Equal(99u, Assert.Single(modern.Messages<EventUpdate>()).EventId);
            Assert.Equal(99u, Assert.Single(old.Messages<EventUpdate>()).EventId);
            Assert.True(Jfp2DatagramsBetween(mesh, hub, modern) >= 1);
            Assert.Equal(0, Jfp2DatagramsBetween(mesh, hub, old));
        }

        /// <summary>A hub and a node A, both JFP2, with their session negotiated and verified.</summary>
        static (TestMesh Mesh, TestNode Hub, TestNode A) TwoNegotiated()
        {
            var mesh = new TestMesh();
            TestNode hub = Jfp2Node(mesh, "203.0.113.1");
            TestNode a = Jfp2Node(mesh, "198.51.100.2");
            hub.Core.Mesh.Create(false, 0, false, "");
            a.Core.Mesh.Join(hub.EndPoint, 0);
            mesh.Run(3);
            Assert.True(Jfp2Of(a).IsNegotiated(hub.Id));
            return (mesh, hub, a);
        }

        /// <summary>A hand-built, unforwarded JFP2 datagram from <paramref name="from"/> to its neighbor <paramref name="to"/>, in their session.</summary>
        static void SendRaw(TestNode from, TestNode to, EnvelopeFlags flags, byte messageClass, ReadOnlySpan<byte> payload, ushort guaranteedId = 0)
        {
            Assert.True(Jfp2Of(from).TryGetHopIds(to.Id, out ushort local, out ushort remote));
            bool guaranteed = (flags & EnvelopeFlags.Guaranteed) != 0;
            var envelope = new Envelope(flags, local, remote, messageClass, guaranteedId, 0, (byte)(guaranteed ? 1 : 0));
            byte[] datagram = new byte[envelope.WireSize + payload.Length];
            int header = envelope.WriteTo(datagram);
            payload.CopyTo(datagram.AsSpan(header));
            from.Core.Transport.Send(to.EndPoint, datagram);
        }

        [Fact]
        public void Jfp2Guaranteed_SurvivesLoss()
        {
            var (mesh, hub, a) = TwoNegotiated();

            int dropped = 0;
            mesh.Network.Filter = (from, to, data) =>
                !(from.Equals(a.EndPoint) && IsGuaranteedApplication(data) && dropped++ < 2);
            a.Core.SendTo(hub.Id, Note("hi"), true);
            mesh.Run(8);

            Assert.Equal(3, dropped); // two attempts lost, the third let through
            Assert.Equal("hi", Assert.Single(hub.Messages<NotesBundle>()).Users[0].Notes[0].Text);
        }

        /// <summary>
        /// A WeatherReply v1 for tests only that writes its Metar whole, with no limit: a codec that
        /// breaks its field limits, the one way a payload can exceed the ceiling.
        /// </summary>
        sealed class UnlimitedWeatherReplyTestCodec : ICodec<WeatherReply>
        {
            public byte MessageClass => MessageClasses.WeatherReply;
            public byte SchemaVersion => 1;

            public int Encode(in WeatherReply value, Span<byte> dest)
            {
                int length = Encoding.UTF8.GetBytes(value.Metar, dest[2..]);
                BinaryPrimitives.WriteUInt16LittleEndian(dest, (ushort)length);
                return 2 + length;
            }

            public WeatherReply Decode(ReadOnlySpan<byte> src) =>
                new() { Metar = Encoding.UTF8.GetString(src.Slice(2, BinaryPrimitives.ReadUInt16LittleEndian(src))) };
        }

        /// <summary>
        /// Pins a known limit (docs/jfp2/protocol.md §7.5): a guaranteed payload over the
        /// 1,100-byte ceiling, which the field limits (§6.7) leave only to a codec bug, still goes out
        /// as a single datagram, and is logged. When segmentation is implemented this should expect
        /// ceil(size / GuaranteedSegmentSize) segments instead, as
        /// LegacyPluginGoldenTests.GuaranteedSegmentation does for legacy.
        /// </summary>
        [Fact]
        public void Jfp2Guaranteed_LongerThanOneSegment_IsSentUnsegmented()
        {
            Jfp2Profile unlimited = Jfp2Profile.Default.With(
                Jfp2Profile.Default.ForKind<WeatherReply>(MessageKind.WeatherReply).WithCodecs(new UnlimitedWeatherReplyTestCodec()));
            var mesh = new TestMesh();
            TestNode hub = mesh.Add("203.0.113.1", 6112, new LegacyPlugin(), new Jfp2Plugin(profile: unlimited));
            TestNode a = mesh.Add("198.51.100.2", 6112, new LegacyPlugin(), new Jfp2Plugin(profile: unlimited));
            hub.Core.Mesh.Create(false, 0, false, "");
            a.Core.Mesh.Join(hub.EndPoint, 0);
            mesh.Run(3);
            Assert.True(Jfp2Of(a).IsNegotiated(hub.Id));

            string metar = new('x', 1500);
            mesh.Network.Log.Clear();
            a.Core.SendTo(hub.Id, new WeatherReply { Metar = metar }, true);
            mesh.Run(1);

            Assert.Equal(metar, Assert.Single(hub.Messages<WeatherReply>()).Metar);
            byte[] datagram = Assert.Single(mesh.Network.Log, d => d.From.Equals(a.EndPoint) && IsGuaranteedApplication(d.Data)).Data;
            Envelope envelope = Envelope.ReadFrom(datagram, out int header);
            Assert.Equal(0, envelope.GuaranteedIndex);
            Assert.Equal(1, envelope.GuaranteedCount);
            Assert.True(datagram.Length - header > Jfp2Reliability.GuaranteedSegmentSize);
            Assert.Contains(a.Logs, l => l.Contains("1502 bytes is over the " + Envelope.MaxPayloadSize + "-byte ceiling"));
        }

        /// <summary>
        /// A WeatherReply at its limit, the largest guaranteed v1 payload (1,026 bytes), is within the
        /// ceiling, and not logged as over it.
        /// </summary>
        [Fact]
        public void Jfp2Guaranteed_LargestV1Payload_IsWithinTheCeiling()
        {
            var (mesh, hub, a) = TwoNegotiated();

            a.Core.SendTo(hub.Id, new WeatherReply { Metar = new('x', WeatherReplyV1Codec.MetarLimit) }, true);
            mesh.Run(1);

            Assert.Single(hub.Messages<WeatherReply>());
            Assert.True(WeatherReplyV1Codec.MaxSize <= Jfp2Reliability.GuaranteedSegmentSize);
            Assert.DoesNotContain(a.Logs, l => l.Contains("ceiling"));
        }

        /// <summary>
        /// The field limits are JFP2's (docs/jfp2/protocol.md §9.7): a note longer than the JFP2
        /// limit is cut over JFP2 and arrives whole over legacy, which has no such limit.
        /// </summary>
        [Fact]
        public void LongNote_IsCutOverJfp2_AndWholeOverLegacy()
        {
            var mesh = new TestMesh();
            TestNode hub = Jfp2Node(mesh, "203.0.113.1");
            TestNode modern = Jfp2Node(mesh, "198.51.100.2");
            TestNode old = LegacyNode(mesh, "192.0.2.3");
            hub.Core.Mesh.Create(false, 0, false, "");
            modern.Core.Mesh.Join(hub.EndPoint, 0);
            old.Core.Mesh.Join(hub.EndPoint, 0);
            mesh.Run(12);

            string text = new('x', 2500);
            hub.Core.SendTo(modern.Id, Note(text), true);
            hub.Core.SendTo(old.Id, Note(text), true);
            mesh.Run(1);

            Assert.Equal(text[..NotesV1Codec.TextLimit], Assert.Single(modern.Messages<NotesBundle>()).Users[0].Notes[0].Text);
            Assert.Equal(text, Assert.Single(old.Messages<NotesBundle>()).Users[0].Notes[0].Text);
        }

        /// <summary>
        /// Longer than the session timeout: the session is demoted to legacy meanwhile, and the message
        /// waits for JFP2 to reach the hub again instead of being given up (or handed to legacy).
        /// </summary>
        [Fact]
        public void Jfp2Guaranteed_SurvivesOutageLongerThanTheSessionTimeout()
        {
            var (mesh, hub, a) = TwoNegotiated();

            bool down = true;
            mesh.Network.Filter = (from, to, data) => !(down && from.Equals(a.EndPoint));
            a.Core.SendTo(hub.Id, Note("hi"), true);
            mesh.Run(25);
            Assert.Contains(a.Logs, l => l.Contains("no longer verified"));
            Assert.Empty(hub.Messages<NotesBundle>());

            // unanswered Hellos meanwhile put the hub on the 30 s legacy-only cooldown: JFP2 comes back after it
            down = false;
            mesh.Run(45);

            Assert.True(Jfp2Of(a).IsNegotiated(hub.Id));
            Assert.Equal("hi", Assert.Single(hub.Messages<NotesBundle>()).Users[0].Notes[0].Text);
            Assert.Equal(0, Jfp2Of(a).GuaranteedPendingCount);
        }

        /// <summary>
        /// A peer that restarts with a build offering fewer classes: routes cached for the old agreement
        /// are dropped, or messages of a class it no longer takes would be routed to JFP2 and go nowhere.
        /// </summary>
        [Fact]
        public void Handshake_WithFewerOffers_RefreshesCachedRoutes()
        {
            var (mesh, hub, a) = TwoNegotiated();
            Assert.Equal("JFP2", hub.Core.Route(a.Id, MessageKind.Notes)!.Name);

            Assert.True(Jfp2Of(a).TryGetHopIds(hub.Id, out ushort aId, out _));
            var hello = new HandshakeMessage
            {
                ProtoMajorMin = Envelope.ProtoMajor, ProtoMajorMax = Envelope.ProtoMajor, SelfAssignedId = aId,
                Names = [NodeName.FromLegacy(a.Id)],
                Offers = [new SchemaOffer(false, MessageClasses.Position, 1, 1)],
            };
            SendRaw(a, hub, EnvelopeFlags.Internal, MessageClasses.Hello, hello.Serialize());
            mesh.Run(0.1);

            Assert.Equal("Legacy", hub.Core.Route(a.Id, MessageKind.Notes)!.Name);
            Assert.Equal("JFP2", hub.Core.Route(a.Id, MessageKind.Position)!.Name);
        }

        /// <summary>A peer must not take a guaranteed message we cannot read (a class never agreed) as delivered.</summary>
        [Fact]
        public void Jfp2Guaranteed_ClassNeverAgreed_IsNotAcknowledged()
        {
            var (mesh, hub, a) = TwoNegotiated();
            const byte unknownClass = 200; // offered by no build

            mesh.Network.Log.Clear();
            SendRaw(a, hub, EnvelopeFlags.Guaranteed, unknownClass, [1, 2, 3, 4], guaranteedId: 77);
            mesh.Run(1);

            Assert.DoesNotContain(mesh.Network.Log, d => d.From.Equals(hub.EndPoint) && IsGuaranteedDone(d.Data));
        }

        /// <summary>Variables go out in datagrams that fit a safe UDP MTU, like legacy's, however many an object has.</summary>
        [Fact]
        public void VariableSync_ManyVariables_SplitIntoDatagramsUnderTheMtu()
        {
            var (mesh, hub, a) = TwoNegotiated();
            var entries = new List<VariableEntry>();
            for (uint vuid = 1; vuid <= 300; vuid++) entries.Add(new VariableEntry { Vuid = vuid, Kind = VariableKind.Int32, IntValue = (int)vuid });

            mesh.Network.Log.Clear();
            a.Core.SendTo(hub.Id, new VariableSyncUpdate { ObjectId = 3, Entries = entries }, false);
            mesh.Run(0.1);

            var datagrams = mesh.Network.Log.Where(d => d.From.Equals(a.EndPoint) && d.Data[0] == Envelope.Magic
                && (d.Data[2] & (byte)EnvelopeFlags.Internal) == 0 && d.Data[7] == MessageClasses.VariableSync).ToList();
            Assert.True(datagrams.Count > 1);
            Assert.All(datagrams, d => Assert.True(d.Data.Length <= 1024, d.Data.Length + " bytes"));
            Assert.Equal(300, hub.Messages<VariableSyncUpdate>().Sum(m => m.Entries.Count));
        }

        /// <summary>
        /// Objects come and go; what was sent about one that is no longer sent is forgotten, and if it
        /// comes back its identity goes out first again.
        /// </summary>
        [Fact]
        public void Identity_OfAnObjectNoLongerSent_IsForgottenAndResentFirst()
        {
            var (mesh, hub, a) = TwoNegotiated();
            SendPosition(a, hub, 3, "ID1", 1);
            mesh.Run(0.1);
            Assert.Equal(1, Jfp2Of(a).IdentitySentCount);

            mesh.Run(61);
            Assert.Equal(0, Jfp2Of(a).IdentitySentCount);

            hub.Received.Clear();
            SendPosition(a, hub, 3, "ID1", 2);
            mesh.Run(0.1);
            Assert.Equal("ID1", Assert.Single(hub.Messages<IdentityUpdate>()).Callsign);
            Assert.Equal(2, Assert.Single(hub.Messages<PositionUpdate>()).Latitude);
        }

        // ------------------------------------------------ datagrams from later builds (docs/jfp2/protocol.md §3.1, §3.2)

        /// <summary>A Position datagram from a to the hub in their session, with bytes 1 (ProtoMajor) and 2 (Flags) as given.</summary>
        static void SendPositionWith(TestNode a, TestNode hub, byte protoMajor, byte flags)
        {
            Assert.True(Jfp2Of(a).TryGetHopIds(hub.Id, out ushort local, out ushort remote));
            byte[] payload = new byte[PositionV1Codec.Size];
            int length = new PositionV1Codec().Encode(new PositionUpdate { ObjectId = 4, NetTime = 1, Latitude = 51 }, payload);
            byte[] datagram = new byte[Envelope.FixedSize + length];
            new Envelope(EnvelopeFlags.None, local, remote, MessageClasses.Position).WriteTo(datagram);
            payload.AsSpan(0, length).CopyTo(datagram.AsSpan(Envelope.FixedSize));
            datagram[1] = protoMajor;
            datagram[2] = flags;
            a.Core.Transport.Send(hub.EndPoint, datagram);
        }

        /// <summary>
        /// A later major version's datagram is dropped, and not through the error path: a peer running
        /// it would otherwise put an error in the log for every datagram.
        /// </summary>
        [Fact]
        public void Datagram_OfAnotherProtoMajor_IsDroppedQuietly()
        {
            var (mesh, hub, a) = TwoNegotiated();

            SendPositionWith(a, hub, Envelope.ProtoMajor, 0);
            mesh.Run(0.1);
            Assert.Single(hub.Messages<PositionUpdate>()); // the same datagram in our version gets through

            hub.Received.Clear();
            SendPositionWith(a, hub, 3, 0);
            mesh.Run(0.1);

            Assert.Empty(hub.Messages<PositionUpdate>());
            Assert.Contains(hub.Logs, l => l.Contains("ProtoMajor 3") && l.Contains("dropped"));
            Assert.DoesNotContain(hub.Logs, l => l.Contains("ERROR"));
            Assert.DoesNotContain(hub.EventLogs, l => l.Contains("dropped"));
            Assert.True(Jfp2Of(hub).IsNegotiated(a.Id));
        }

        /// <summary>A flag this build cannot read may shift the payload: the datagram is dropped, not misparsed.</summary>
        [Theory]
        [InlineData((byte)EnvelopeFlags.Coalesced)]
        [InlineData((byte)(1 << 4))]
        [InlineData((byte)(1 << 7))]
        public void Datagram_WithAFlagThisBuildCannotRead_IsDroppedQuietly(byte flag)
        {
            var (mesh, hub, a) = TwoNegotiated();

            SendPositionWith(a, hub, Envelope.ProtoMajor, flag);
            mesh.Run(0.1);

            Assert.Empty(hub.Messages<PositionUpdate>());
            Assert.Contains(hub.Logs, l => l.Contains("flag bits") && l.Contains("dropped"));
            Assert.DoesNotContain(hub.Logs, l => l.Contains("ERROR"));
            Assert.DoesNotContain(hub.EventLogs, l => l.Contains("dropped"));
        }

        /// <summary>
        /// The other half of the flag rule: this build never sets a bit a peer could need a capability
        /// to read, and always sends ProtoMajor 2 - handshake, application, guaranteed and acks alike.
        /// </summary>
        [Fact]
        public void EveryJfp2DatagramSent_IsOneEveryJfp2BuildCanRead()
        {
            // a and b reach each other only through the hub, so their traffic is relayed (Forwarded)
            var mesh = new TestMesh();
            TestNode hub = Jfp2Node(mesh, "203.0.113.1");
            TestNode a = Jfp2Node(mesh, "198.51.100.2");
            TestNode b = Jfp2Node(mesh, "192.0.2.3");
            mesh.Partition(a, b);
            hub.Core.Mesh.Create(false, 0, false, "");
            a.Core.Mesh.Join(hub.EndPoint, 0);
            b.Core.Mesh.Join(hub.EndPoint, 0);
            mesh.Run(25);
            SendPosition(a, b, 3, "JF1", 1);
            a.Core.SendTo(b.Id, Note("hi"), true);
            SendPosition(a, hub, 4, "JF2", 2);
            a.Core.SendTo(hub.Id, Note("hi"), true);
            mesh.Run(12); // keepalives too
            Assert.Single(b.Messages<NotesBundle>());

            var sent = mesh.Network.Log.Where(d => d.Data[0] == Envelope.Magic).ToList();
            Assert.Contains(sent, d => (d.Data[2] & (byte)EnvelopeFlags.Guaranteed) != 0);
            Assert.Contains(sent, d => (d.Data[2] & (byte)EnvelopeFlags.Forwarded) != 0);
            Assert.All(sent, d =>
            {
                Assert.Equal(2, d.Data[1]);
                Assert.Equal(0, d.Data[2] & ~(byte)Envelope.SupportedFlags);
            });
        }

        /// <summary>
        /// Every class, every string longer than its field's limit (docs/jfp2/protocol.md §9.7), so
        /// that each message is the largest of its class.
        /// </summary>
        static void SendEveryClassAtItsLimits(TestNode from, TestNode to)
        {
            static string Text(int limit) => LongText.Ascii(limit);
            from.Core.Objects.SetIdentity(from.Id, new IdentityUpdate
            {
                ObjectId = 3, IsAircraft = true, IsPlane = true, TypeRole = 1,
                Callsign = Text(IdentityV1Codec.CallsignLimit), Model = Text(IdentityV1Codec.ModelLimit),
                Livery = Text(IdentityV1Codec.LiveryLimit), IcaoType = Text(IdentityV1Codec.IcaoTypeLimit),
                IcaoAirline = Text(IdentityV1Codec.IcaoAirlineLimit), Registration = Text(IdentityV1Codec.RegistrationLimit),
                FlightNumber = Text(IdentityV1Codec.FlightNumberLimit), ClassCode = Text(IdentityV1Codec.ClassCodeLimit),
                Wtc = Text(IdentityV1Codec.WtcLimit),
            });
            from.Core.SendTo(to.Id, new PositionUpdate { ObjectId = 3, NetTime = 1, Latitude = 1 }, false);
            var entries = new List<VariableEntry>();
            for (uint vuid = 1; vuid <= 10; vuid++)
            {
                entries.Add(new VariableEntry { Vuid = vuid, Kind = VariableKind.String8, StringValue = Text(VariableSyncV1Codec.StringValueLimit) });
            }
            for (uint vuid = 11; vuid <= 400; vuid++)
            {
                entries.Add(new VariableEntry { Vuid = vuid, Kind = VariableKind.Float32, FloatValue = vuid });
            }
            from.Core.SendTo(to.Id, new VariableSyncUpdate { ObjectId = 3, Entries = entries }, false);
            from.Core.SendTo(to.Id, new EventUpdate { ObjectId = 3, EventId = 99, Data = 5 }, true);
            from.Core.SendTo(to.Id, new FlightPlanUpdate
            {
                ObjectId = 3,
                IcaoType = Text(FlightPlanV1Codec.IcaoTypeLimit), Departure = Text(FlightPlanV1Codec.DepartureLimit),
                Destination = Text(FlightPlanV1Codec.DestinationLimit), Rules = Text(FlightPlanV1Codec.RulesLimit),
                Route = Text(FlightPlanV1Codec.RouteLimit), Remarks = Text(FlightPlanV1Codec.RemarksLimit),
                Alternate = Text(FlightPlanV1Codec.AlternateLimit), Speed = Text(FlightPlanV1Codec.SpeedLimit),
                Altitude = Text(FlightPlanV1Codec.AltitudeLimit), Callsign = Text(FlightPlanV1Codec.CallsignLimit),
                Registration = Text(FlightPlanV1Codec.RegistrationLimit), IcaoAirline = Text(FlightPlanV1Codec.IcaoAirlineLimit),
                FlightNumber = Text(FlightPlanV1Codec.FlightNumberLimit),
            }, false);
            from.Core.SendTo(to.Id, new NotesBundle
            {
                Scope = CommsScope.Single,
                Users =
                [
                    new NotesUser
                    {
                        Guid = Guid.NewGuid(), Nickname = Text(NotesV1Codec.NicknameLimit), Callsign = Text(NotesV1Codec.CallsignLimit),
                        Notes = [new CommsNote { NoteId = 5, Channel = 1, Text = Text(NotesV1Codec.TextLimit) }],
                    },
                ],
            }, true);
            from.Core.SendTo(to.Id, new WeatherUpdate { Metar = Text(WeatherUpdateV1Codec.MetarLimit) }, false);
            from.Core.SendTo(to.Id, new StatusUpdate
            {
                Guid = Guid.NewGuid(), AppVersion = Text(StatusV1Codec.AppVersionLimit), AtcAirport = Text(StatusV1Codec.AtcAirportLimit),
                HubEnabled = true, Address = Text(StatusV1Codec.AddressLimit), Name = Text(StatusV1Codec.NameLimit),
                About = Text(StatusV1Codec.AboutLimit), Voip = Text(StatusV1Codec.VoipLimit), NextEvent = Text(StatusV1Codec.NextEventLimit),
                Airport = Text(StatusV1Codec.AirportLimit),
            }, false);
            from.Core.SendTo(to.Id, new StatusRequestUpdate { HubEnabled = true, HubListRequested = true, Uuid = 1 }, false);
            from.Core.SendTo(to.Id, new WeatherReply { Metar = Text(WeatherReplyV1Codec.MetarLimit) }, true);
        }

        /// <summary>
        /// No JFP2 datagram is larger than 1,200 bytes (docs/jfp2/protocol.md §3.6): every class with
        /// maximum-length strings in every field, guaranteed where the class is, relayed so that the
        /// Forwarded extension is included, and sent directly; with the handshakes, keepalives and acks
        /// that go with them.
        /// </summary>
        [Fact]
        public void EveryJfp2DatagramSent_IsAtMost1200Bytes()
        {
            // a and b reach each other only through the hub, so their traffic is relayed (Forwarded)
            var mesh = new TestMesh();
            TestNode hub = Jfp2Node(mesh, "203.0.113.1");
            TestNode a = Jfp2Node(mesh, "198.51.100.2");
            TestNode b = Jfp2Node(mesh, "192.0.2.3");
            mesh.Partition(a, b);
            hub.Core.Mesh.Create(false, 0, false, "");
            a.Core.Mesh.Join(hub.EndPoint, 0);
            b.Core.Mesh.Join(hub.EndPoint, 0);
            mesh.Run(25);
            Assert.Equal(hub.Id, Jfp2Of(a).NextHopNode(b.Id));

            mesh.Network.Log.Clear();
            SendEveryClassAtItsLimits(a, b);
            SendEveryClassAtItsLimits(a, hub);
            mesh.Run(12); // keepalives too

            var sent = mesh.Network.Log.Where(d => d.Data[0] == Envelope.Magic).ToList();
            Assert.All(sent, d => Assert.True(d.Data.Length <= Envelope.MaxDatagramSize, d.Data.Length + " bytes, class " + d.Data[7]));
            Assert.Contains(sent, d => (d.Data[2] & (byte)EnvelopeFlags.Internal) != 0 && d.Data[7] == MessageClasses.HelloAck
                && HandshakeMessage.Deserialize(d.Data.AsSpan(Envelope.FixedSize)).ObservedEndPoint != null);

            // every class went through the relay to b, guaranteed where the class is, at its largest
            var relayed = sent.Where(d => d.From.Equals(hub.EndPoint) && d.To.Equals(b.EndPoint)
                && (d.Data[2] & (byte)EnvelopeFlags.Internal) == 0 && (d.Data[2] & (byte)EnvelopeFlags.Forwarded) != 0).ToList();
            var largest = new Dictionary<byte, int>
            {
                [MessageClasses.Position] = PositionV1Codec.Size,
                [MessageClasses.Identity] = IdentityV1Codec.MaxSize,
                [MessageClasses.Event] = 12,
                [MessageClasses.FlightPlan] = FlightPlanV1Codec.MaxSize,
                [MessageClasses.Notes] = NotesV1Codec.MaxSize,
                [MessageClasses.Weather] = WeatherUpdateV1Codec.MaxSize,
                [MessageClasses.Status] = StatusV1Codec.MaxSize,
                [MessageClasses.StatusRequest] = StatusRequestV1Codec.Size,
                [MessageClasses.WeatherReply] = WeatherReplyV1Codec.MaxSize,
            };
            foreach (ClassDescriptor c in Jfp2Profile.Default.Classes)
            {
                byte[] datagram = relayed.First(d => d.Data[7] == c.MessageClass).Data;
                int header = Envelope.FixedSize + Envelope.ForwardedExtraSize + (c.Guaranteed ? Envelope.GuaranteedExtraSize : 0);
                Assert.Equal(c.Guaranteed, (datagram[2] & (byte)EnvelopeFlags.Guaranteed) != 0);
                if (largest.TryGetValue(c.MessageClass, out int payload))
                {
                    Assert.Equal(header + payload, datagram.Length);
                }
            }
            Assert.Equal(Envelope.FixedSize + Envelope.ForwardedExtraSize + 1058, relayed.Max(d => d.Data.Length)); // Status
            Assert.True(relayed.Count(d => d.Data[7] == MessageClasses.VariableSync) > 1);

            // and what was cut still arrived
            Assert.Equal(StatusV1Codec.AboutLimit, b.Messages<StatusUpdate>().Single().About.Length);
            Assert.Equal(NotesV1Codec.TextLimit, b.Messages<NotesBundle>().Single().Users[0].Notes[0].Text.Length);
            Assert.Equal(400, b.Messages<VariableSyncUpdate>().Sum(m => m.Entries.Count));
        }

        // ------------------------------------------------ the handshake as the permanent entry point (§5.2)

        /// <summary>A hand-made Hello from a to the hub in their session, as a later build might send it.</summary>
        static void SendHello(TestNode a, TestNode hub, byte protoMajorMin, byte protoMajorMax, Action<HandshakeMessage>? extend = null)
        {
            Assert.True(Jfp2Of(a).TryGetHopIds(hub.Id, out ushort aId, out _));
            var hello = new HandshakeMessage
            {
                ProtoMajorMin = protoMajorMin, ProtoMajorMax = protoMajorMax, SelfAssignedId = aId,
                Names = [NodeName.FromLegacy(a.Id)],
                Offers = [new SchemaOffer(false, MessageClasses.Position, 1, 1)],
            };
            extend?.Invoke(hello);
            SendRaw(a, hub, EnvelopeFlags.Internal, MessageClasses.Hello, hello.Serialize());
        }

        /// <summary>The HelloAcks the hub sent to a, read back.</summary>
        static List<HandshakeMessage> HelloAcksTo(TestMesh mesh, TestNode hub, TestNode a) =>
            mesh.Network.Log
                .Where(d => d.From.Equals(hub.EndPoint) && d.To.Equals(a.EndPoint) && d.Data[0] == Envelope.Magic
                    && (d.Data[2] & (byte)EnvelopeFlags.Internal) != 0 && d.Data[7] == MessageClasses.HelloAck)
                .Select(d =>
                {
                    Envelope.ReadFrom(d.Data, out int header);
                    return HandshakeMessage.Deserialize(d.Data.AsSpan(header));
                })
                .ToList();

        /// <summary>A later build offers ProtoMajor 2..3 and an extension this build has never heard of: it is accepted.</summary>
        [Fact]
        public void Hello_FromALaterBuild_IsAnsweredWithAccepted()
        {
            var (mesh, hub, a) = TwoNegotiated();

            mesh.Network.Log.Clear();
            SendHello(a, hub, 2, 3, hello => hello.Extensions[0x7FFF] = [1, 2, 3, 4, 5]);
            mesh.Run(0.1);

            HandshakeMessage ack = Assert.Single(HelloAcksTo(mesh, hub, a));
            Assert.Equal(0, ack.Result);
            Assert.Equal(2, ack.ProtoMajorMin);
            Assert.Equal(2, ack.ProtoMajorMax);
            Assert.Equal("JFP2", hub.Core.Route(a.Id, MessageKind.Position)!.Name);
        }

        /// <summary>A build that no longer speaks ProtoMajor 2 still says so in a ProtoMajor-2 Hello, and is told no.</summary>
        [Fact]
        public void Hello_WithoutProtoMajor2InItsRange_IsAnsweredWithNoCompatibleProtoMajor()
        {
            var (mesh, hub, a) = TwoNegotiated();

            mesh.Network.Log.Clear();
            SendHello(a, hub, 3, 3);
            mesh.Run(0.1);

            Assert.Equal(1, Assert.Single(HelloAcksTo(mesh, hub, a)).Result);
        }

        /// <summary>
        /// What this build actually sends as a handshake is what HandshakeGoldenTests pins: ProtoMajor 2,
        /// Internal only, Names and Build - the build cleaned before it goes out - and, in a HelloAck
        /// only, the ObservedEndPoint last: the Hello's source.
        /// </summary>
        [Fact]
        public void Handshake_GoesOutInTheFrozenEnvelope()
        {
            var (mesh, hub, a) = TwoBuilds("26.6.0 JoinFS-FS2024\r\n", "26.6.0 JoinFS-CONSOLE\u200B");

            var handshakes = mesh.Network.Log.Where(d => d.Data[0] == Envelope.Magic && d.Data[7] <= MessageClasses.HelloAck
                && (d.Data[2] & (byte)EnvelopeFlags.Internal) != 0).ToList();
            Assert.Contains(handshakes, d => d.Data[7] == MessageClasses.Hello);
            Assert.Contains(handshakes, d => d.Data[7] == MessageClasses.HelloAck);
            Assert.All(handshakes, d =>
            {
                Assert.Equal(Envelope.HandshakeProtoMajor, d.Data[1]);
                Assert.Equal((byte)EnvelopeFlags.Internal, d.Data[2]);
                HandshakeMessage message = HandshakeMessage.Deserialize(d.Data.AsSpan(Envelope.FixedSize));
                bool fromHub = d.From.Equals(hub.EndPoint);
                Assert.True(Assert.Single(message.Names).TryGetLegacy(out NodeId named));
                Assert.Equal(fromHub ? hub.Id : a.Id, named);
                string build = fromHub ? "26.6.0 JoinFS-FS2024" : "26.6.0 JoinFS-CONSOLE";
                Assert.Equal(build, message.Build);
                bool ack = d.Data[7] == MessageClasses.HelloAck;
                Assert.Equal(ack ? d.To : null, message.ObservedEndPoint); // where the Hello came from
                byte[] observed = ack ? [3, 0, 7, 0, .. WireEndPoint.ToBytes(d.To)] : [];
                Assert.True(d.Data.AsSpan().EndsWith([.. Encoding.ASCII.GetBytes(build), .. observed])); // the last extensions, as sent
            });
        }

        // ------------------------------------------------ observed endpoint (docs/jfp2/protocol.md §5.4)

        static List<(NodeId Reporter, IPEndPoint EndPoint)> Observations(TestNode node) =>
            node.Events.Where(e => e.Kind == NetworkEventKind.EndPointObserved).Select(e => (e.Node, e.EndPoint)).ToList();

        static readonly IPEndPoint Planted = new(IPAddress.Parse("192.0.2.200"), 4444);

        /// <summary>
        /// Every HelloAck says where the Hello it answers came from, and the asker reports it to the app:
        /// once, and again only when it changes, not with every keepalive.
        /// </summary>
        [Fact]
        public void HelloAck_TellsTheAskerItsSourceEndPoint()
        {
            var (mesh, hub, a) = TwoNegotiated();
            mesh.Run(12); // keepalives

            Assert.Equal([(hub.Id, a.EndPoint)], Observations(a));
            Assert.Equal([(a.Id, hub.EndPoint)], Observations(hub));
        }

        /// <summary>
        /// An observation counts only from the endpoint our Hello went to: the session id it is addressed
        /// to is 16 bits and guessable off the path, so a HelloAck from elsewhere could otherwise plant
        /// any address. Nor from a node that is not in the mesh, or from a refusal that does not answer us.
        /// </summary>
        [Fact]
        public void ObservationFromAnotherEndPoint_IsIgnored()
        {
            var (mesh, hub, a) = TwoNegotiated();
            TestNode spoofer = mesh.Add("192.0.2.66", 6112, new LegacyPlugin(), new Jfp2Plugin());
            TestNode stranger = mesh.Add("192.0.2.77", 6112, new LegacyPlugin(), new Jfp2Plugin());
            a.Events.Clear();
            a.Logs.Clear();

            SendHelloAck(hub, a, HandshakeMessage.ResultAccepted, "spoof", from: spoofer, observed: Planted);
            SendHelloAck(hub, a, HandshakeMessage.ResultNotAdmitted, "spoof", from: spoofer, observed: Planted);
            SendHelloAck(hub, a, HandshakeMessage.ResultAccepted, "stranger", names: [NodeName.FromLegacy(stranger.Id)], observed: Planted);
            mesh.Run(0.1);

            Assert.Empty(Observations(a));
            Assert.Contains(a.Logs, l => l.Contains("its observed endpoint is ignored"));
            Assert.True(Jfp2Of(a).IsNegotiated(hub.Id));
        }

        /// <summary>
        /// A neighbor that leaves and comes back is reported again even at the same endpoint: the app
        /// forgot it when it left, and the plugin reports only changes.
        /// </summary>
        [Fact]
        public void NeighborThatLeavesAndReturns_IsReportedAgain()
        {
            var (mesh, hub, a) = TwoNegotiated();
            Assert.Equal([(hub.Id, a.EndPoint)], Observations(a));

            a.Core.Mesh.Leave();
            mesh.Run(1);
            a.Events.Clear();
            a.Core.Mesh.Join(hub.EndPoint, 0);
            mesh.Run(12);

            Assert.Equal([(hub.Id, a.EndPoint)], Observations(a));
        }

        /// <summary>A refusal from the peer asked, at the endpoint asked, still carries its observation (§4.2: its extensions are read).</summary>
        [Fact]
        public void Refusal_StillTellsTheObservedEndPoint()
        {
            var (mesh, hub, a) = TwoNegotiated();
            a.Events.Clear();

            SendHelloAck(hub, a, HandshakeMessage.ResultNotAdmitted, "refusing", observed: Planted);
            mesh.Run(0.1);

            Assert.Equal([(hub.Id, Planted)], Observations(a));
        }

        /// <summary>The largest HelloAck - every offer, the longest Build, an IPv6 observed endpoint - fits the 1,200-byte ceiling.</summary>
        [Fact]
        public void LargestHelloAck_FitsTheCeiling()
        {
            var ack = new HandshakeMessage
            {
                ProtoMajorMin = 2, ProtoMajorMax = 2, Result = HandshakeMessage.ResultAccepted,
                Offers = [.. Jfp2Profile.Default.Offers],
                Names = [NodeName.FromLegacy(new NodeId(0xCB007101, 6112, 1))],
                Build = new string('x', HandshakeMessage.BuildMaxBytes),
                ObservedEndPoint = new IPEndPoint(IPAddress.Parse("2001:db8::1"), 65535),
            };

            int size = Envelope.FixedSize + ack.Serialize().Length;

            Assert.Equal(Envelope.FixedSize + 15 + 4 * Jfp2Profile.Default.Offers.Count + (4 + 8) + (4 + 64) + (4 + WireEndPoint.IPv6Size), size);
            Assert.True(size <= Envelope.MaxDatagramSize);
        }

        // ------------------------------------------------ node names (docs/jfp2/protocol.md §4.3)

        /// <summary>A name of kind 1 (a random key): a later build's, which this build cannot resolve.</summary>
        static readonly NodeName KeyName = NodeName.ReadFrom(new byte[] { 1, 0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF, 0x11 });

        /// <summary>A hand-built Forwarded datagram from <paramref name="from"/> to its neighbor <paramref name="to"/>, in their session.</summary>
        static void SendForwarded(TestNode from, TestNode to, EnvelopeFlags flags, byte messageClass, ReadOnlySpan<byte> payload, ushort guaranteedId,
            NodeName origin, NodeName target)
        {
            Assert.True(Jfp2Of(from).TryGetHopIds(to.Id, out ushort local, out ushort remote));
            bool guaranteed = (flags & EnvelopeFlags.Guaranteed) != 0;
            var envelope = new Envelope(flags | EnvelopeFlags.Forwarded, local, remote, messageClass, guaranteedId, 0, (byte)(guaranteed ? 1 : 0), origin, target);
            byte[] datagram = new byte[envelope.WireSize + payload.Length];
            int header = envelope.WriteTo(datagram);
            payload.CopyTo(datagram.AsSpan(header));
            from.Core.Transport.Send(to.EndPoint, datagram);
        }

        /// <summary>
        /// This build resolves kind-0 names only (§4.9): a Forwarded datagram naming another kind, as
        /// its origin or as a target that is not this node, is dropped and, guaranteed, not
        /// acknowledged, so its sender keeps retrying instead of taking it as delivered.
        /// </summary>
        [Fact]
        public void ForwardedWithAnUnknownNameKind_IsDroppedAndNotAcknowledged()
        {
            var (mesh, hub, a) = TwoNegotiated();
            byte[] payload = new byte[64];
            int n = new EventV1Codec().Encode(new EventUpdate { ObjectId = 8, EventId = 1234, Data = 1 }, payload);

            mesh.Network.Log.Clear();
            hub.Logs.Clear();
            // from an origin named by a key, for the hub
            SendForwarded(a, hub, EnvelopeFlags.Guaranteed, MessageClasses.Event, payload.AsSpan(0, n), 41, KeyName, NodeName.FromLegacy(hub.Id));
            // from a, for a node named by a key (the hub would relay it)
            SendForwarded(a, hub, EnvelopeFlags.Guaranteed, MessageClasses.Event, payload.AsSpan(0, n), 42, NodeName.FromLegacy(a.Id), KeyName);
            mesh.Run(1);

            Assert.Empty(hub.Messages<EventUpdate>());
            Assert.DoesNotContain(mesh.Network.Log, d => d.From.Equals(hub.EndPoint) && IsGuaranteedDone(d.Data));
            Assert.Equal(2, hub.Logs.Count(l => l.Contains("cannot resolve") && l.Contains("dropped")));
            Assert.DoesNotContain(hub.Logs, l => l.Contains("ERROR"));

            // the same datagram naming both by their legacy ids is delivered and acknowledged
            SendForwarded(a, hub, EnvelopeFlags.Guaranteed, MessageClasses.Event, payload.AsSpan(0, n), 43, NodeName.FromLegacy(a.Id), NodeName.FromLegacy(hub.Id));
            mesh.Run(1);
            var (meta, evt) = Assert.Single(hub.MessagesWithMeta<EventUpdate>());
            Assert.Equal(a.Id, meta.Sender);
            Assert.Equal(1234u, evt.EventId);
            Assert.Contains(mesh.Network.Log, d => d.From.Equals(hub.EndPoint) && IsGuaranteedDone(d.Data));
        }

        /// <summary>
        /// A kind-0 name with ip 0 (here all zero, "no node") holds no valid node id; resolved, it would
        /// read as this node in the app. A Forwarded datagram from it is dropped and not acknowledged.
        /// </summary>
        [Fact]
        public void ForwardedFromAZeroName_IsDroppedAndNotAcknowledged()
        {
            var (mesh, hub, a) = TwoNegotiated();
            byte[] payload = new byte[64];
            int n = new EventV1Codec().Encode(new EventUpdate { ObjectId = 8, EventId = 1234, Data = 1 }, payload);

            mesh.Network.Log.Clear();
            hub.Logs.Clear();
            SendForwarded(a, hub, EnvelopeFlags.Guaranteed, MessageClasses.Event, payload.AsSpan(0, n), 44, default, NodeName.FromLegacy(hub.Id));
            mesh.Run(1);

            Assert.Empty(hub.Messages<EventUpdate>());
            Assert.DoesNotContain(mesh.Network.Log, d => d.From.Equals(hub.EndPoint) && IsGuaranteedDone(d.Data));
            Assert.Contains(hub.Logs, l => l.Contains("cannot resolve") && l.Contains("dropped"));
        }

        /// <summary>
        /// At a relay: the target is the hub's direct neighbor b and resolves, but the origin is named
        /// by a key. The hub cannot place who it relays for, so nothing goes to b and nothing is
        /// acknowledged to a. The same datagram from a's legacy id is relayed (the control).
        /// </summary>
        [Fact]
        public void RelayedFromAnUnresolvableOrigin_IsNotForwardedNorAcknowledged()
        {
            var mesh = new TestMesh();
            TestNode hub = Jfp2Node(mesh, "203.0.113.1");
            TestNode a = Jfp2Node(mesh, "198.51.100.2");
            TestNode b = Jfp2Node(mesh, "192.0.2.3");
            mesh.Partition(a, b);
            hub.Core.Mesh.Create(false, 0, false, "");
            a.Core.Mesh.Join(hub.EndPoint, 0);
            b.Core.Mesh.Join(hub.EndPoint, 0);
            mesh.Run(25);
            Assert.True(Jfp2Of(hub).IsNegotiated(b.Id));
            byte[] payload = new byte[64];
            int n = new EventV1Codec().Encode(new EventUpdate { ObjectId = 8, EventId = 1234, Data = 1 }, payload);

            mesh.Network.Log.Clear();
            hub.Logs.Clear();
            SendForwarded(a, hub, EnvelopeFlags.Guaranteed, MessageClasses.Event, payload.AsSpan(0, n), 45, KeyName, NodeName.FromLegacy(b.Id));
            mesh.Run(1);

            Assert.Empty(b.Messages<EventUpdate>());
            Assert.DoesNotContain(mesh.Network.Log, d => d.From.Equals(hub.EndPoint) && d.To.Equals(b.EndPoint) && d.Data[0] == Envelope.Magic
                && (d.Data[2] & (byte)EnvelopeFlags.Internal) == 0);
            Assert.DoesNotContain(mesh.Network.Log, d => d.From.Equals(hub.EndPoint) && IsGuaranteedDone(d.Data));
            Assert.Contains(hub.Logs, l => l.Contains("cannot resolve") && l.Contains("dropped"));

            SendForwarded(a, hub, EnvelopeFlags.Guaranteed, MessageClasses.Event, payload.AsSpan(0, n), 46, NodeName.FromLegacy(a.Id), NodeName.FromLegacy(b.Id));
            mesh.Run(1);
            var (meta, evt) = Assert.Single(b.MessagesWithMeta<EventUpdate>());
            Assert.Equal(a.Id, meta.Sender);
            Assert.Equal(1234u, evt.EventId);
        }

        /// <summary>
        /// A later build lists a key beside its legacy id (stage 11 of docs/jfp2/roadmap.md): this
        /// build skips the name it cannot resolve and binds the session by the legacy one. A Hello that
        /// names only a key is from no node this build can place, and is ignored.
        /// </summary>
        [Fact]
        public void HelloWithANameOfAnUnknownKindBesideALegacyName_BindsByTheLegacyName()
        {
            var (mesh, hub, a) = TwoNegotiated();
            Assert.Equal("JFP2", hub.Core.Route(a.Id, MessageKind.Notes)!.Name);

            // the hand-made Hello offers Position only: the hub applying it to a's session shows it was bound to a
            mesh.Network.Log.Clear();
            hub.Logs.Clear();
            SendHello(a, hub, 2, 2, hello => hello.Names = [KeyName, NodeName.FromLegacy(a.Id)]);
            mesh.Run(0.1);

            Assert.Equal(0, Assert.Single(HelloAcksTo(mesh, hub, a)).Result);
            Assert.Contains(hub.Logs, l => l.Contains("Hello from " + a.Id + " at") && l.Contains("answered"));
            Assert.Equal("Legacy", hub.Core.Route(a.Id, MessageKind.Notes)!.Name);
            Assert.Equal("JFP2", hub.Core.Route(a.Id, MessageKind.Position)!.Name);

            mesh.Network.Log.Clear();
            hub.Logs.Clear();
            SendHello(a, hub, 2, 2, hello => hello.Names = [KeyName]);
            mesh.Run(0.1);

            Assert.Empty(HelloAcksTo(mesh, hub, a));
            Assert.Contains(hub.Logs, l => l.Contains("does not say who it is"));
        }

        // ------------------------------------------------ handshake and envelope rules (docs/jfp2/protocol.md §7, §5.1)

        /// <summary>
        /// A hand-made HelloAck in the hub's session with a, as a later build might send it: from the hub
        /// naming itself, unless another sender (a spoofer, with the ids guessed) or other names are given.
        /// </summary>
        static void SendHelloAck(TestNode hub, TestNode a, byte result, string build, TestNode? from = null, List<NodeName>? names = null,
            IPEndPoint? observed = null)
        {
            Assert.True(Jfp2Of(hub).TryGetHopIds(a.Id, out ushort hubId, out ushort aId));
            var ack = new HandshakeMessage
            {
                ProtoMajorMin = Envelope.ProtoMajor, ProtoMajorMax = Envelope.ProtoMajor, SelfAssignedId = hubId, Result = result,
                Names = names ?? [NodeName.FromLegacy(hub.Id)], Build = build, ObservedEndPoint = observed,
            };
            byte[] payload = ack.Serialize();
            var envelope = new Envelope(EnvelopeFlags.Internal, hubId, aId, MessageClasses.HelloAck);
            byte[] datagram = new byte[envelope.WireSize + payload.Length];
            payload.CopyTo(datagram, envelope.WriteTo(datagram));
            (from ?? hub).Core.Transport.Send(a.EndPoint, datagram);
        }

        /// <summary>
        /// A refusal takes a working link off JFP2 for 30 s, so it counts only when it answers our Hello:
        /// from the endpoint the Hello went to, naming the peer asked. A spoofed one from elsewhere (the
        /// session ids guessed), one naming another node, and one naming none are logged and ignored.
        /// </summary>
        [Fact]
        public void Refusal_NotFromThePeerAskedAtItsEndPoint_IsIgnored()
        {
            var (mesh, hub, a) = TwoNegotiated();
            TestNode spoofer = mesh.Add("192.0.2.66", 6112, new LegacyPlugin(), new Jfp2Plugin());
            TestNode other = mesh.Add("192.0.2.77", 6112, new LegacyPlugin(), new Jfp2Plugin());

            a.Logs.Clear();
            SendHelloAck(hub, a, HandshakeMessage.ResultNotAdmitted, "spoof", from: spoofer);
            SendHelloAck(hub, a, HandshakeMessage.ResultNotAdmitted, "other", names: [NodeName.FromLegacy(other.Id)]);
            SendHelloAck(hub, a, HandshakeMessage.ResultNotAdmitted, "none", names: []);
            mesh.Run(0.1);

            Assert.True(Jfp2Of(a).IsNegotiated(hub.Id));
            Assert.Equal("JFP2", a.Core.Route(hub.Id, MessageKind.Position)!.Name);
            Assert.Equal(3, a.Logs.Count(l => l.Contains("Result 2") && l.Contains("ignored")));
            Assert.DoesNotContain(a.Logs, l => l.Contains("no session now") || l.Contains("no longer verified"));
            Assert.NotEqual("spoof", Jfp2Of(a).BuildOf(hub.Id));
            Assert.NotEqual("other", Jfp2Of(a).BuildOf(hub.Id));

            // and it stays up: the next keepalives go through, with no cooldown
            mesh.Run(12);
            Assert.True(Jfp2Of(a).IsNegotiated(hub.Id));
        }

        /// <summary>
        /// Any Result but 0 means "no session now" (§5.2): a later build may refuse for a reason this
        /// one does not know, which does not make it legacy-only. Legacy carries the peer meanwhile, the
        /// value is what is logged, the answer's extensions are still read, and the peer is asked
        /// again after the 30 s cooldown, not before.
        /// </summary>
        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(7)]
        [InlineData(255)]
        public void HelloAckWithAnUnknownResult_MeansNoSessionNow_NotLegacyOnly(byte result)
        {
            var (mesh, hub, a) = TwoNegotiated();
            Assert.Equal("JFP2", a.Core.Route(hub.Id, MessageKind.Position)!.Name);

            a.Logs.Clear();
            SendHelloAck(hub, a, result, "26.7.0 JoinFS-Later");
            mesh.Run(0.1);

            Assert.False(Jfp2Of(a).IsNegotiated(hub.Id));
            Assert.Equal("Legacy", a.Core.Route(hub.Id, MessageKind.Position)!.Name);
            Assert.Equal(PeerLinkState.Negotiating, Jfp2Of(a).DescribeLink(a.Core.Peers.All.Single()));
            Assert.Contains(a.Logs, l => l.Contains("Result " + result) && l.Contains("no session now"));
            Assert.DoesNotContain(a.Logs, l => l.Contains("legacy-only"));
            Assert.Equal("26.7.0 JoinFS-Later", Jfp2Of(a).BuildOf(hub.Id));

            mesh.Run(25);
            Assert.False(Jfp2Of(a).IsNegotiated(hub.Id));
            mesh.Run(10);
            Assert.True(Jfp2Of(a).IsNegotiated(hub.Id));
            Assert.Equal("JFP2", a.Core.Route(hub.Id, MessageKind.Position)!.Name);
        }

        /// <summary>
        /// An offer whose partition byte is neither 0 (application) nor 1 (internal) belongs to a class
        /// space this build does not know: it is skipped, and the offers around it still count (§5.2).
        /// Read as an application offer, it would have agreed Notes.
        /// </summary>
        [Fact]
        public void OfferOfAnUnknownPartition_IsSkipped()
        {
            var (mesh, hub, a) = TwoNegotiated();
            Assert.Equal("JFP2", hub.Core.Route(a.Id, MessageKind.Notes)!.Name);

            Assert.True(Jfp2Of(a).TryGetHopIds(hub.Id, out ushort aId, out _));
            byte[] hello = new HandshakeMessage
            {
                ProtoMajorMin = Envelope.ProtoMajor, ProtoMajorMax = Envelope.ProtoMajor, SelfAssignedId = aId,
                Names = [NodeName.FromLegacy(a.Id)],
                Offers = [new SchemaOffer(false, MessageClasses.Notes, 1, 1), new SchemaOffer(false, MessageClasses.Position, 1, 1)],
            }.Serialize();
            hello[15] = 2; // the first offer's partition byte, after the 15 bytes of fixed fields
            mesh.Network.Log.Clear();
            SendRaw(a, hub, EnvelopeFlags.Internal, MessageClasses.Hello, hello);
            mesh.Run(0.1);

            Assert.Equal(0, Assert.Single(HelloAcksTo(mesh, hub, a)).Result);
            Assert.Equal("Legacy", hub.Core.Route(a.Id, MessageKind.Notes)!.Name);
            Assert.Equal("JFP2", hub.Core.Route(a.Id, MessageKind.Position)!.Name);
        }

        /// <summary>
        /// GuaranteedDone is always 3 bytes, id and segment index (§4.4); the 2-byte form only dev
        /// builds sent acknowledges nothing, so the message stays pending until a full ack arrives.
        /// </summary>
        [Fact]
        public void TwoByteGuaranteedDone_IsIgnored()
        {
            var (mesh, hub, a) = TwoNegotiated();
            // the hub never gets the message, so only our hand-made acks answer it
            mesh.Network.Filter = (from, to, data) => !(from.Equals(a.EndPoint) && IsGuaranteedApplication(data));
            mesh.Network.Log.Clear();
            a.Core.SendTo(hub.Id, Note("hi"), true);
            mesh.Run(0.1);
            Assert.Equal(1, Jfp2Of(a).GuaranteedPendingCount);
            byte[] sent = Assert.Single(mesh.Network.Log, d => d.From.Equals(a.EndPoint) && IsGuaranteedApplication(d.Data)).Data;
            ushort id = Envelope.ReadFrom(sent, out _).GuaranteedId;

            byte[] ack = new byte[3];
            BinaryPrimitives.WriteUInt16LittleEndian(ack, id);
            SendRaw(hub, a, EnvelopeFlags.Internal, MessageClasses.GuaranteedDone, ack.AsSpan(0, 2));
            mesh.Run(0.1);
            Assert.Equal(1, Jfp2Of(a).GuaranteedPendingCount);
            Assert.DoesNotContain(a.Logs, l => l.Contains("ERROR"));

            SendRaw(hub, a, EnvelopeFlags.Internal, MessageClasses.GuaranteedDone, ack);
            mesh.Run(0.1);
            Assert.Equal(0, Jfp2Of(a).GuaranteedPendingCount);
        }

        /// <summary>
        /// A segment of a guaranteed message of several (count > 1) is something this build cannot
        /// reassemble: it is dropped and not acknowledged, so the sender does not take a message that
        /// was never delivered as delivered (§4.4). Count 1 is the control.
        /// </summary>
        [Fact]
        public void GuaranteedSegmentOfSeveral_IsDroppedWithoutAck()
        {
            var (mesh, hub, a) = TwoNegotiated();
            Assert.True(Jfp2Of(a).TryGetHopIds(hub.Id, out ushort local, out ushort remote));
            byte[] payload = new byte[EventV1Codec.Size];
            new EventV1Codec().Encode(new EventUpdate { ObjectId = 8, EventId = 99 }, payload);
            void Send(ushort id, byte count)
            {
                var envelope = new Envelope(EnvelopeFlags.Guaranteed, local, remote, MessageClasses.Event, id, 0, count);
                byte[] datagram = new byte[envelope.WireSize + payload.Length];
                payload.CopyTo(datagram, envelope.WriteTo(datagram));
                a.Core.Transport.Send(hub.EndPoint, datagram);
            }

            mesh.Network.Log.Clear();
            hub.Logs.Clear();
            Send(61, 2);
            mesh.Run(1);
            Assert.Empty(hub.Messages<EventUpdate>());
            Assert.DoesNotContain(mesh.Network.Log, d => d.From.Equals(hub.EndPoint) && IsGuaranteedDone(d.Data));
            Assert.Contains(hub.Logs, l => l.Contains("dropped segment 0/2"));

            Send(62, 1);
            mesh.Run(1);
            Assert.Equal(99u, Assert.Single(hub.Messages<EventUpdate>()).EventId);
            Assert.Contains(mesh.Network.Log, d => d.From.Equals(hub.EndPoint) && IsGuaranteedDone(d.Data));
        }

        // ------------------------------------------------ the build advertisement (§5.5)

        /// <summary>A hub and a node A, both JFP2 and saying which build they run, negotiated.</summary>
        static (TestMesh Mesh, TestNode Hub, TestNode A) TwoBuilds(string hubBuild, string aBuild)
        {
            var mesh = new TestMesh();
            TestNode hub = mesh.Add("203.0.113.1", 6112, new LegacyPlugin(), new Jfp2Plugin(build: hubBuild));
            TestNode a = mesh.Add("198.51.100.2", 6112, new LegacyPlugin(), new Jfp2Plugin(build: aBuild));
            hub.Core.Mesh.Create(false, 0, false, "");
            a.Core.Mesh.Join(hub.EndPoint, 0);
            mesh.Run(3);
            Assert.True(Jfp2Of(a).IsNegotiated(hub.Id));
            return (mesh, hub, a);
        }

        /// <summary>
        /// The first build learned for a session is logged at event level; what any later handshake
        /// says (a restart with another build, or Hellos forged at any rate) only at network level.
        /// </summary>
        [Fact]
        public void Build_IsLearnedFromTheHandshake_FirstAtEventLevel_ChangesAtNetworkLevel()
        {
            var (mesh, hub, a) = TwoBuilds("26.6.0 JoinFS-FS2024", "26.6.0 JoinFS-CONSOLE");
            mesh.Run(20); // several keepalives each way

            Assert.Equal("26.6.0 JoinFS-CONSOLE", Jfp2Of(hub).BuildOf(a.Id));
            Assert.Equal("26.6.0 JoinFS-FS2024", Jfp2Of(a).BuildOf(hub.Id));
            Assert.Single(hub.Logs, l => l.Contains("runs build"));
            Assert.Single(hub.EventLogs, l => l == "JFP2: " + a.Id + " runs build 26.6.0 JoinFS-CONSOLE");
            Assert.Single(a.EventLogs, l => l.Contains("runs build"));

            // a restarted with another build
            SendHello(a, hub, 2, 2, hello => hello.Build = "26.7.0 JoinFS-CONSOLE");
            mesh.Run(0.1);
            Assert.Equal("26.7.0 JoinFS-CONSOLE", Jfp2Of(hub).BuildOf(a.Id));
            Assert.Single(hub.Logs, l => l.EndsWith("runs build 26.7.0 JoinFS-CONSOLE"));
            Assert.Single(hub.EventLogs, l => l.Contains("runs build"));

            // then with a build that does not say (the Hello carries no Build)
            SendHello(a, hub, 2, 2);
            mesh.Run(0.1);
            Assert.Null(Jfp2Of(hub).BuildOf(a.Id));
        }

        [Fact]
        public void Build_NotSaid_IsNone()
        {
            var (mesh, hub, a) = TwoNegotiated();
            mesh.Run(6);

            Assert.Null(Jfp2Of(hub).BuildOf(a.Id));
            Assert.DoesNotContain(hub.Logs, l => l.Contains("runs build"));
        }

        static bool IsGuaranteedApplication(byte[] data) =>
            data[0] == Envelope.Magic && (data[2] & (byte)EnvelopeFlags.Guaranteed) != 0 && (data[2] & (byte)EnvelopeFlags.Internal) == 0;

        static bool IsGuaranteedDone(byte[] data) =>
            data[0] == Envelope.Magic && (data[2] & (byte)EnvelopeFlags.Internal) != 0 && data[7] == MessageClasses.GuaranteedDone;

        static NotesBundle Note(string text) => new()
        {
            Scope = CommsScope.Single,
            Users = [new NotesUser { Guid = Guid.NewGuid(), Nickname = "n", Callsign = "c", Notes = [new CommsNote { NoteId = 5, Channel = 1, Text = text }] }],
        };

        /// <summary>
        /// A JFP2 sender relaying through a JFP2 hub to a legacy-only target (what builds from this
        /// branch before the rewrite did): the hub translates. Position must be credited to the true
        /// sender (the old Tier 3 credited the hub), and guaranteed classes must arrive (the old Tier 3
        /// dropped them).
        /// </summary>
        [Fact]
        public void RelayedJfp2_IsTranslatedForLegacyTarget()
        {
            var t = new HubBetweenJfp2AndLegacy();
            byte[] buffer = new byte[512];
            int n = new IdentityV1Codec().Encode(Identity(8, "RLY1"), buffer);
            t.Relayed(MessageClasses.Identity, buffer.AsSpan(0, n), false, 0);
            n = new PositionV1Codec().Encode(new PositionUpdate { ObjectId = 8, NetTime = 2, Latitude = 33, StateFlags = PositionStateFlags.UserControlled }, buffer);
            t.Relayed(MessageClasses.Position, buffer.AsSpan(0, n), false, 0);
            n = new EventV1Codec().Encode(new EventUpdate { ObjectId = 8, EventId = 1234, Data = 1 }, buffer);
            t.Relayed(MessageClasses.Event, buffer.AsSpan(0, n), true, 4321);
            t.Mesh.Run(1);

            var (meta, position) = Assert.Single(t.B.MessagesWithMeta<PositionUpdate>());
            Assert.Equal(t.A.Id, meta.Sender);
            Assert.Equal(33, position.Latitude);
            Assert.Equal("RLY1", Assert.Single(t.B.Messages<IdentityUpdate>()).Callsign);
            var (eventMeta, evt) = Assert.Single(t.B.MessagesWithMeta<EventUpdate>());
            Assert.Equal(t.A.Id, eventMeta.Sender);
            Assert.Equal(1234u, evt.EventId);

            // the hub's legacy delivery was acknowledged (and the ack consumed, not relayed back to a)
            t.Mesh.Run(3);
            var legacy = t.Hub.Core.Plugins.OfType<LegacyPlugin>().Single();
            Assert.Equal(0, legacy.GuaranteedOutCount);
            Assert.Single(t.B.Messages<EventUpdate>());
        }

        /// <summary>
        /// The hub acks upstream as soon as it has translated. If that ack is lost the sender
        /// retransmits: the hub must ack again, but not deliver the message downstream a second time.
        /// </summary>
        [Fact]
        public void RelayedJfp2_RetransmittedGuaranteed_IsTranslatedOnce()
        {
            var t = new HubBetweenJfp2AndLegacy();
            byte[] buffer = new byte[64];
            int n = new EventV1Codec().Encode(new EventUpdate { ObjectId = 8, EventId = 1234, Data = 1 }, buffer);
            t.Mesh.Network.Log.Clear();
            t.Relayed(MessageClasses.Event, buffer.AsSpan(0, n), true, 4321);
            t.Mesh.Run(1);
            t.Relayed(MessageClasses.Event, buffer.AsSpan(0, n), true, 4321);
            t.Mesh.Run(3);

            Assert.Single(t.B.Messages<EventUpdate>());
            Assert.Equal(2, t.Mesh.Network.Log.Count(d => d.From.Equals(t.Hub.EndPoint) && d.To.Equals(t.A.EndPoint) && IsGuaranteedDone(d.Data)));
        }

        /// <summary>
        /// A JFP2 node A and a legacy-only node B that reach each other only through a JFP2 hub, and
        /// A's hand-built Forwarded envelopes for B.
        /// </summary>
        sealed class HubBetweenJfp2AndLegacy
        {
            public readonly TestMesh Mesh = new();
            public readonly TestNode Hub, A, B;
            readonly ushort hopLocal, hopRemote;

            public HubBetweenJfp2AndLegacy()
            {
                Hub = Jfp2Node(Mesh, "203.0.113.1");
                A = Jfp2Node(Mesh, "198.51.100.2");
                B = LegacyNode(Mesh, "192.0.2.3");
                Mesh.Partition(A, B);
                Hub.Core.Mesh.Create(false, 0, false, "");
                A.Core.Mesh.Join(Hub.EndPoint, 0);
                B.Core.Mesh.Join(Hub.EndPoint, 0);
                Mesh.Run(15);
                Assert.True(Jfp2Of(Hub).IsNegotiated(A.Id));
                Assert.False(Jfp2Of(Hub).IsNegotiated(B.Id));
                Assert.True(Jfp2Of(A).TryGetHopIds(Hub.Id, out hopLocal, out hopRemote));
            }

            /// <summary>Send from A to the hub, addressed to B: hop ids of A's session with the hub, end-to-end Origin/Target.</summary>
            public void Relayed(byte messageClass, ReadOnlySpan<byte> payload, bool guaranteed, ushort id)
            {
                var flags = EnvelopeFlags.Forwarded | (guaranteed ? EnvelopeFlags.Guaranteed : 0);
                var envelope = new Envelope(flags, hopLocal, hopRemote, messageClass, id, 0, (byte)(guaranteed ? 1 : 0),
                    NodeName.FromLegacy(A.Id), NodeName.FromLegacy(B.Id));
                byte[] data = new byte[envelope.WireSize + payload.Length];
                int header = envelope.WriteTo(data);
                payload.CopyTo(data.AsSpan(header));
                A.Core.Transport.Send(Hub.EndPoint, data);
            }
        }
    }
}
