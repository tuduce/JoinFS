using JoinFS.Net;
using JoinFS.Net.Jfp2;
using JoinFS.Net.Legacy;
using JoinFS.Tests.Net;

namespace JoinFS.Tests.Session
{
    /// <summary>
    /// SimIngest: how other nodes' objects reach the simulator - the identity cache, filtering,
    /// shared cockpit, variables, events, flight plans, weather - and, end to end over the
    /// in-memory mesh, that the outcome is the same whichever protocol carried the messages.
    /// </summary>
    public class SimIngestTests
    {
        static readonly NodeId Peer = new(0x0A000001, 6112, 1);
        static readonly NodeId Other = new(0x0A000002, 6112, 2);

        static IdentityUpdate Identity(uint id, string model = "C172", string callsign = "N1") => new()
        {
            ObjectId = id, IsAircraft = true, IsPlane = true, Callsign = callsign, Model = model, Livery = "", IcaoType = "C172",
            IcaoAirline = "", Registration = "", FlightNumber = "", ClassCode = "", Wtc = "", TypeRole = 1,
        };

        static PositionUpdate Position(uint id, bool user = true, double latitude = 50) =>
            new() { ObjectId = id, NetTime = 1, Latitude = latitude, StateFlags = user ? PositionStateFlags.UserControlled : PositionStateFlags.None };

        static SessionRig WithPeer(string nickname = "Bob")
        {
            var rig = new SessionRig();
            rig.Peers.OnPeerJoined(Peer, Peer.ToEndPoint(Peer.port));
            rig.Peers.Handle(SessionRig.From(Peer), new PeerInfo { Nickname = nickname, AtcAirport = "", Version = "", Simulator = "" });
            return rig;
        }

        [Fact]
        public void PositionBeforeIdentity_IsDroppedAndLogged()
        {
            var rig = WithPeer();
            rig.Ingest.Handle(SessionRig.From(Peer), Position(7));

            Assert.Empty(rig.Sim.Aircraft);
            Assert.Contains(rig.Log.NetworkLines, l => l.Contains("identity not known yet"));
        }

        [Fact]
        public void IdentityThenPosition_UpdatesAircraftWithIdentityAndNickname()
        {
            var rig = WithPeer("Bob");
            rig.Ingest.Handle(SessionRig.From(Peer), Identity(7, "A320", "DLH1"));
            rig.Ingest.Handle(SessionRig.From(Peer), Position(7, latitude: 48.5));

            var update = Assert.Single(rig.Sim.Aircraft);
            Assert.Equal(Peer, update.Owner);
            Assert.Equal("A320", update.Identity.Model);
            Assert.Equal("DLH1", update.Identity.Callsign);
            Assert.True(update.User);
            Assert.Equal("Bob", update.Nickname);
            Assert.Equal(48.5, update.Position.Latitude);
        }

        [Fact]
        public void IdentityChange_IsAppliedOnlyWhenSomethingChanged()
        {
            var rig = WithPeer();
            rig.Ingest.Handle(SessionRig.From(Peer), Identity(7, "C172"));
            // the first identity only primes the cache (the object is created by its first position)
            Assert.Empty(rig.Sim.IdentityChanges);

            rig.Ingest.Handle(SessionRig.From(Peer), Identity(7, "C172"));
            Assert.Empty(rig.Sim.IdentityChanges);

            rig.Ingest.Handle(SessionRig.From(Peer), Identity(7, "A320"));
            var change = Assert.Single(rig.Sim.IdentityChanges);
            Assert.Equal("A320", change.Identity.Model);
        }

        [Fact]
        public void AdditionalObjects_OnlyWhenAllowed()
        {
            var rig = WithPeer();
            rig.Ingest.Handle(SessionRig.From(Peer), Identity(8));
            rig.Ingest.Handle(SessionRig.From(Peer), Position(8, user: false));
            rig.Ingest.Handle(SessionRig.From(Peer), new ObjectPositionUpdate { ObjectId = 8 });
            Assert.Empty(rig.Sim.Aircraft);
            Assert.Empty(rig.Sim.Objects);

            // allowed for this node...
            rig.Policy.MultipleObjectNodes.Add(Peer);
            rig.Ingest.Handle(SessionRig.From(Peer), Position(8, user: false));
            rig.Ingest.Handle(SessionRig.From(Peer), new ObjectPositionUpdate { ObjectId = 8 });
            var aircraft = Assert.Single(rig.Sim.Aircraft);
            Assert.Equal("", aircraft.Nickname);
            Assert.Single(rig.Sim.Objects);

            // ...or for everyone
            rig.Policy.MultipleObjectNodes.Clear();
            rig.Profile.MultipleObjects = true;
            rig.Ingest.Handle(SessionRig.From(Peer), Position(8, user: false));
            Assert.Equal(2, rig.Sim.Aircraft.Count);
        }

        [Fact]
        public void SharedCockpitPosition_OnlyFromTheNodeWithFlightControls()
        {
            var rig = WithPeer();
            rig.Ingest.Handle(SessionRig.From(Peer), Position(uint.MaxValue));
            Assert.Empty(rig.Sim.OwnAircraftPositions);

            rig.Peers.shareFlightControls = Peer;
            rig.Ingest.Handle(SessionRig.From(Other), Position(uint.MaxValue));
            Assert.Empty(rig.Sim.OwnAircraftPositions);

            rig.Ingest.Handle(SessionRig.From(Peer), Position(uint.MaxValue, latitude: 1.5));
            Assert.Equal(1.5, Assert.Single(rig.Sim.OwnAircraftPositions).Latitude);
            Assert.Empty(rig.Sim.Aircraft);
        }

        [Fact]
        public void RemoveObject_ForgetsItsIdentity()
        {
            var rig = WithPeer();
            rig.Ingest.Handle(SessionRig.From(Peer), Identity(7));
            rig.Ingest.Handle(SessionRig.From(Peer), new RemoveObject { ObjectId = 7 });
            rig.Ingest.Handle(SessionRig.From(Peer), Position(7));

            Assert.Equal((Peer, 7u), Assert.Single(rig.Sim.Removed));
            Assert.Empty(rig.Sim.Aircraft);
            Assert.Equal(0, rig.Ingest.IdentityCount);
        }

        [Fact]
        public void PeerLeft_ForgetsOnlyThatNodesIdentitiesAndRemovesItsObjects()
        {
            var rig = WithPeer();
            rig.Ingest.Handle(SessionRig.From(Peer), Identity(1));
            rig.Ingest.Handle(SessionRig.From(Peer), Identity(2));
            rig.Ingest.Handle(SessionRig.From(Other), Identity(1));

            rig.Ingest.OnPeerLeft(Peer);

            Assert.Equal(1, rig.Ingest.IdentityCount);
            Assert.Equal(Peer, Assert.Single(rig.Sim.RemovedOwners));
        }

        [Fact]
        public void Variables_AreSplitByKind_AndSharedCockpitGoesToOurAircraftUnrecorded()
        {
            var rig = WithPeer();
            var sync = new VariableSyncUpdate
            {
                Owner = Peer, ObjectId = 7,
                Entries = [
                    new VariableEntry { Vuid = 1, Kind = VariableKind.Int32, IntValue = 5 },
                    new VariableEntry { Vuid = 2, Kind = VariableKind.Float32, FloatValue = 2.5f },
                ],
            };
            rig.Ingest.Handle(SessionRig.From(Peer), sync);
            var applied = Assert.Single(rig.Sim.Variables);
            Assert.Equal((Peer, 7u, true), (applied.Owner, applied.NetId, applied.Record));
            Assert.Equal(5, applied.Integers[1]);
            Assert.Equal(2.5f, applied.Floats[2]);
            Assert.Null(applied.String8s);

            rig.Sim.OwnAircraftOwner = Other;
            rig.Sim.OwnAircraftNetId = 99;
            rig.Ingest.Handle(SessionRig.From(Peer), sync with { ObjectId = uint.MaxValue });
            var shared = rig.Sim.Variables[1];
            Assert.Equal((Other, 99u, false), (shared.Owner, shared.NetId, shared.Record));
        }

        [Fact]
        public void Events_NeedASession_AndOurAircraftsFlightControlsNeedSharing()
        {
            var rig = WithPeer();
            rig.Session.Connected = false;
            rig.Ingest.Handle(SessionRig.From(Peer), new EventUpdate { ObjectId = 7, EventId = 3, Data = 4 });
            Assert.Empty(rig.Sim.Events);

            rig.Session.Connected = true;
            rig.Ingest.Handle(SessionRig.From(Peer), new EventUpdate { ObjectId = 7, EventId = 3, Data = 4 });
            Assert.Equal((Peer, 7u, 3u, 4u, true, true), Assert.Single(rig.Sim.Events));

            // on our aircraft: flight-control events only from the node we gave them to, with sharing allowed
            rig.Peers.shareFlightControls = Peer;
            rig.Ingest.Handle(SessionRig.From(Peer), new EventUpdate { ObjectId = uint.MaxValue, EventId = 5 });
            Assert.False(rig.Sim.Events[1].FlightControls);
            rig.Policy.SharingNodes.Add(Peer);
            rig.Ingest.Handle(SessionRig.From(Peer), new EventUpdate { ObjectId = uint.MaxValue, EventId = 5 });
            Assert.True(rig.Sim.Events[2].FlightControls);
            Assert.False(rig.Sim.Events[2].Record);
        }

        [Fact]
        public void FlightPlan_OursUpdatesTheUserPlan_OthersUpdateTheirAircraft()
        {
            var rig = WithPeer();
            rig.Ingest.Handle(SessionRig.From(Peer), new FlightPlanUpdate { Owner = Peer, ObjectId = 7, Callsign = "X" });
            Assert.Equal((Peer, 7u), (rig.Sim.AircraftFlightPlans[0].Owner, rig.Sim.AircraftFlightPlans[0].NetId));

            rig.Ingest.Handle(SessionRig.From(Peer), new FlightPlanUpdate { Owner = rig.Session.LocalId, ObjectId = 1, Callsign = "ME" });
            Assert.Equal("ME", Assert.Single(rig.Sim.UserFlightPlans).Callsign);
        }

        [Fact]
        public void WeatherRequest_IsAnsweredOnlyWithAnObservation()
        {
            var rig = WithPeer();
            rig.Ingest.Handle(SessionRig.From(Peer), new WeatherRequest());
            Assert.Empty(rig.Outbox.Sent);

            rig.Sim.CurrentMetar = "EDDF 121250Z";
            rig.Ingest.Handle(SessionRig.From(Peer), new WeatherRequest());
            Sent reply = Assert.Single(rig.Outbox.Sent);
            Assert.Equal("EDDF 121250Z", ((WeatherReply)reply.Message).Metar);
            Assert.Equal(Peer, reply.To);
            Assert.True(reply.Guaranteed);
        }

        // ------------------------------------------------------------------ end to end

        /// <summary>
        /// A position crosses the in-memory mesh and is applied: legacy carries identity inline in
        /// the position datagram, JFP2 sends it separately first - the simulator sees the same thing.
        /// </summary>
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void PositionOverTheMesh_ReachesTheSimulatorWithItsIdentity(bool jfp2)
        {
            var mesh = new TestMesh();
            IProtocolPlugin[] Plugins() => jfp2 ? [new LegacyPlugin(), new Jfp2Plugin()] : [new LegacyPlugin()];
            TestNode a = mesh.Add("198.51.100.2", 6112, Plugins());
            TestNode b = mesh.Add("192.0.2.3", 6112, Plugins());
            a.Core.Mesh.Create(false, 0, false, "");
            b.Core.Mesh.Join(a.EndPoint, 0);
            mesh.Run(3);
            Assert.Equal(jfp2 ? "JFP2" : "Legacy", a.Core.Route(b.Id, MessageKind.Position)!.Name);
            b.Received.Clear();

            // what NetworkService.SendObjectState does on the network thread
            a.Core.Objects.SetIdentity(a.Id, Identity(4, "B738", "RYR1"));
            a.Core.SendTo(b.Id, Position(4, latitude: 41.25), false);
            mesh.Run(0.1);

            var rig = new SessionRig();
            rig.Peers.OnPeerJoined(a.Id, a.EndPoint);
            foreach (var (meta, message) in b.Received)
            {
                ((IMessage)message).Dispatch(rig.Ingest, meta);
            }

            var update = Assert.Single(rig.Sim.Aircraft);
            Assert.Equal(a.Id, update.Owner);
            Assert.Equal("B738", update.Identity.Model);
            Assert.Equal("RYR1", update.Identity.Callsign);
            Assert.Equal(41.25, update.Position.Latitude);
        }
    }
}
