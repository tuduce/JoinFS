using JoinFS.Fsd;
using JoinFS.Net;

namespace JoinFS.Tests.Session
{
    /// <summary>docs/ATC/atc-fsd-plan.md stage 7: the station table of the session, with <see cref="SessionRig"/>.</summary>
    public class AtcSessionTests
    {
        // the rig's own node is 1.2.3.4; of two nodes that claim one callsign the lower id keeps it
        static readonly NodeId Lower = NodeId.FromLegacy(0x01020301, 6112, 4);
        static readonly NodeId Higher = NodeId.FromLegacy(0x0A000001, 6112, 4);
        static readonly NodeId Third = NodeId.FromLegacy(0x0B000001, 6112, 4);

        readonly SessionRig rig = new();
        readonly List<AtcRemoteStation> changed = [];
        readonly List<AtcRemoteStation> gone = [];
        readonly List<(int Slot, string Callsign)> lost = [];

        public AtcSessionTests()
        {
            rig.Stations.StationChanged += changed.Add;
            rig.Stations.StationGone += gone.Add;
            rig.Stations.LocalStationLost += (slot, callsign) => lost.Add((slot, callsign));
        }

        static AtcStationInfo Local(string callsign = "EDDM_TWR", FsdFacility facility = FsdFacility.Tower, int khz = 118700, bool positionKnown = true) =>
            new(callsign, "Carla", 4, facility, 50, khz, 48.35377, 11.78609, positionKnown);

        static AtcStation Announce(string callsign, int slot = 0, bool online = true, uint khz = 119_150, byte facility = 5, string name = "Ben") => new()
        {
            Slot = (byte)slot, Online = online, Facility = facility, Rating = 5, Visibility = 100, Frequency = khz,
            Latitude = 48.1, Longitude = 11.5, Callsign = callsign, Name = name,
        };

        void Member(NodeId node)
        {
            rig.Stations.OnPeerJoined(node);
            rig.Stations.OnPeerEstablished(node);
        }

        void Receive(NodeId from, AtcStation message)
        {
            rig.Stations.Handle(SessionRig.From(from), message);
            rig.Stations.DoWork();
        }

        void Advance(double seconds)
        {
            rig.Clock.Advance(seconds);
            rig.Stations.DoWork();
        }

        // ---- sending

        [Fact]
        public void ALocalStation_IsSentToEveryone_WhenItsFirstPositionIsKnown_AndWhenItChanges()
        {
            rig.Stations.SetLocalStation(0, Local(positionKnown: false));
            Assert.Empty(rig.Outbox.Sent); // not announced before its first %

            rig.Stations.SetLocalStation(0, Local());
            AtcStation sent = Assert.Single(rig.Outbox.Messages<AtcStation>());
            Assert.True(rig.Outbox.Sent[0].Broadcast);
            Assert.True(sent.Online);
            Assert.Equal(0, sent.Slot);
            Assert.Equal("EDDM_TWR", sent.Callsign);
            Assert.Equal("Carla", sent.Name);
            Assert.Equal((byte)FsdFacility.Tower, sent.Facility);
            Assert.Equal(4, sent.Rating);
            Assert.Equal(50, sent.Visibility);
            Assert.Equal(118_700u, sent.Frequency);
            Assert.Equal(48.35377, sent.Latitude);

            rig.Stations.SetLocalStation(0, Local()); // the same again: the next % of a client that did not move
            Assert.Single(rig.Outbox.Messages<AtcStation>());

            rig.Stations.SetLocalStation(0, Local(khz: 118_725));
            Assert.Equal(2, rig.Outbox.Messages<AtcStation>().Count());
            Assert.Equal(118_725u, rig.Outbox.Messages<AtcStation>().Last().Frequency);
        }

        [Fact]
        public void ALogoff_IsSentOnce_WithTheCallsignItHad()
        {
            rig.Stations.SetLocalStation(0, Local());
            rig.Outbox.Sent.Clear();

            rig.Stations.SetLocalStation(0, null);
            rig.Stations.SetLocalStation(0, null);

            AtcStation sent = Assert.Single(rig.Outbox.Messages<AtcStation>());
            Assert.False(sent.Online);
            Assert.Equal("EDDM_TWR", sent.Callsign);
        }

        [Fact]
        public void TheStation_IsRepeatedEveryTenSeconds_NotBefore()
        {
            rig.Stations.SetLocalStation(0, Local());
            Assert.Single(rig.Outbox.Messages<AtcStation>());

            Advance(9.5);
            Assert.Single(rig.Outbox.Messages<AtcStation>());
            Advance(1.0);
            Assert.Equal(2, rig.Outbox.Messages<AtcStation>().Count());
            Advance(10.0);
            Assert.Equal(3, rig.Outbox.Messages<AtcStation>().Count());
        }

        [Fact]
        public void ANodeThatJoins_IsToldAtOnce_ByAMessageToItAlone()
        {
            rig.Stations.SetLocalStation(0, Local());
            rig.Outbox.Sent.Clear();

            Member(Higher);

            Sent sent = Assert.Single(rig.Outbox.Of<AtcStation>());
            Assert.Equal(Higher, sent.To);
            Assert.False(sent.Broadcast);
            Assert.Equal("EDDM_TWR", ((AtcStation)sent.Message).Callsign);
        }

        [Fact]
        public void NothingIsBroadcast_OutsideASession()
        {
            rig.Session.Connected = false;

            rig.Stations.SetLocalStation(0, Local());
            Advance(11);

            Assert.Empty(rig.Outbox.Sent);
        }

        // ---- receiving

        [Fact]
        public void AStationFromAMember_IsListed_AndAnnouncedOnce()
        {
            Member(Higher);

            Receive(Higher, Announce("EDDM_APP"));

            AtcRemoteStation station = Assert.Single(rig.Stations.Stations);
            Assert.Equal(Higher, station.Node);
            Assert.Equal("EDDM_APP", station.Callsign);
            Assert.Equal("Ben", station.Name);
            Assert.Equal(FsdFacility.Approach, station.Facility);
            Assert.Equal(119_150, station.FrequencyKhz);
            Assert.Equal(48.1, station.Latitude);
            Assert.Equal(station, Assert.Single(changed));

            // the same every 10 s: nothing to tell
            Receive(Higher, Announce("EDDM_APP"));
            Assert.Single(changed);
        }

        [Fact]
        public void AChange_IsAnnounced()
        {
            Member(Higher);
            Receive(Higher, Announce("EDDM_APP"));

            Receive(Higher, Announce("EDDM_APP", khz: 119_300));

            Assert.Equal(2, changed.Count);
            Assert.Equal(119_300, Assert.Single(rig.Stations.Stations).FrequencyKhz);
            Assert.Empty(gone);
        }

        [Fact]
        public void ALogoffNotice_DropsTheStationAtOnce()
        {
            Member(Higher);
            Receive(Higher, Announce("EDDM_APP"));

            Receive(Higher, Announce("EDDM_APP", online: false));

            Assert.Empty(rig.Stations.Stations);
            Assert.Equal("EDDM_APP", Assert.Single(gone).Callsign);
            Assert.False(rig.Stations.CallsignInUse("EDDM_APP"));
        }

        [Fact]
        public void AStationNotHeardFor35Seconds_IsDropped()
        {
            Member(Higher);
            Receive(Higher, Announce("EDDM_APP"));

            Advance(34.0);
            Assert.Single(rig.Stations.Stations);

            Receive(Higher, Announce("EDDM_APP")); // heard again: the 35 s start over
            Advance(34.0);
            Assert.Single(rig.Stations.Stations);

            Advance(1.5);
            Assert.Empty(rig.Stations.Stations);
            Assert.Single(gone);
        }

        [Fact]
        public void AStationOfAMemberThatLeft_IsDroppedAtOnce()
        {
            Member(Higher);
            Member(Third);
            Receive(Higher, Announce("EDDM_APP"));
            Receive(Third, Announce("EDDM_GND", facility: 3));

            rig.Stations.OnPeerLeft(Higher);
            rig.Stations.DoWork();

            Assert.Equal("EDDM_GND", Assert.Single(rig.Stations.Stations).Callsign);
            Assert.Equal("EDDM_APP", Assert.Single(gone).Callsign);
        }

        [Fact]
        public void BothSlotsOfANode_AreTwoStations()
        {
            Member(Higher);

            Receive(Higher, Announce("EDDM_APP"));
            Receive(Higher, Announce("EDDM_ATIS", slot: 1, facility: 0));

            Assert.Equal(2, rig.Stations.Stations.Count);
            Receive(Higher, Announce("EDDM_ATIS", slot: 1, online: false));
            Assert.Equal("EDDM_APP", Assert.Single(rig.Stations.Stations).Callsign);
        }

        [Fact]
        public void TheNorthAndTheSouthTower_OnTwoNodes_AreTwoStations()
        {
            Member(Higher);
            Member(Third);

            Receive(Higher, Announce("EDDM_N_TWR", facility: 4, khz: 118_700));
            Receive(Third, Announce("EDDM_S_TWR", facility: 4, khz: 120_500));

            Assert.Equal(["EDDM_N_TWR", "EDDM_S_TWR"], rig.Stations.Stations.Select(s => s.Callsign).Order());
            Assert.True(rig.Stations.CallsignInUse("EDDM_N_TWR"));
            Assert.False(rig.Stations.CallsignInUse("EDDM_TWR"));
        }

        // ---- what a peer sends is not trusted

        [Fact]
        public void AMessage_FromANodeThatIsNoMember_OrFromThisNode_OrOnAnUnknownSlot_IsIgnored()
        {
            Member(Higher);

            Receive(Third, Announce("EDDM_APP")); // never joined
            Receive(rig.Session.LocalId, Announce("EDDM_APP")); // ourselves
            Receive(Higher, Announce("EDDM_APP", slot: 2));

            Assert.Empty(rig.Stations.Stations);
            Assert.Empty(changed);
        }

        [Theory]
        [InlineData("")]
        [InlineData("AB")]
        [InlineData("EDDM-TWR")]
        [InlineData("EDDM:TWR")]
        [InlineData("EDDM_TWR_NORTH_X")]
        public void AnInvalidCallsign_IsIgnored(string callsign)
        {
            Member(Higher);

            Receive(Higher, Announce(callsign));

            Assert.Empty(rig.Stations.Stations);
        }

        [Fact]
        public void AFacilityOutOfRange_IsIgnored_AFrequencyOutOfRange_IsNone_AndANameIsCleaned()
        {
            Member(Higher);
            Receive(Higher, Announce("EDDM_APP", facility: 7));
            Assert.Empty(rig.Stations.Stations);

            Receive(Higher, Announce("EDDM_APP", khz: 7, name: "Be:n\r\nX"));
            AtcRemoteStation station = Assert.Single(rig.Stations.Stations);
            Assert.Equal(0, station.FrequencyKhz);
            Assert.Equal("Be n  X", station.Name);
        }

        // ---- the crossing logins of §4.6

        [Fact]
        public void AStationAnnouncedWithOurCallsign_ByALowerNode_TakesItFromUs()
        {
            Member(Lower);
            rig.Stations.SetLocalStation(0, Local("EDDM_TWR"));
            rig.Outbox.Sent.Clear();

            Receive(Lower, Announce("EDDM_TWR", facility: 4));

            Assert.Equal((0, "EDDM_TWR"), Assert.Single(lost));
            AtcStation notice = Assert.Single(rig.Outbox.Messages<AtcStation>());
            Assert.False(notice.Online);
            Assert.Equal("EDDM_TWR", Assert.Single(rig.Stations.Stations).Callsign);
            Assert.Equal(Lower, rig.Stations.Stations[0].Node);

            // the gateway logs the lost station off: that is no second notice
            rig.Stations.SetLocalStation(0, null);
            Assert.Single(rig.Outbox.Messages<AtcStation>());
        }

        [Fact]
        public void AStationAnnouncedWithOurCallsign_ByAHigherNode_KeepsOurs_AndIsHidden()
        {
            Member(Higher);
            rig.Stations.SetLocalStation(0, Local("EDDM_TWR"));

            Receive(Higher, Announce("EDDM_TWR", facility: 4));

            Assert.Empty(lost);
            Assert.Empty(rig.Stations.Stations);
            Assert.Empty(changed);
            Assert.True(rig.Stations.CallsignInUse("EDDM_TWR"));

            // when ours logs off, the other one is the station
            rig.Stations.SetLocalStation(0, null);
            rig.Stations.DoWork();
            Assert.Equal(Higher, Assert.Single(rig.Stations.Stations).Node);
        }

        [Fact]
        public void TheLoginThatLosesBeforeItsFirstPosition_LosesWhenItArrives()
        {
            Member(Lower);
            Receive(Lower, Announce("EDDM_TWR", facility: 4)); // announced while our client had not sent its first %

            rig.Stations.SetLocalStation(0, Local("EDDM_TWR"));
            rig.Stations.DoWork();

            Assert.Equal((0, "EDDM_TWR"), Assert.Single(lost));
        }

        [Fact]
        public void TwoRemoteStationsWithOneCallsign_ShowTheLowerNodes_AndTheOtherWhenItLeaves()
        {
            Member(Higher);
            Member(Lower);
            Receive(Higher, Announce("EDDM_APP"));
            Receive(Lower, Announce("EDDM_APP", khz: 120_000));

            AtcRemoteStation shown = Assert.Single(rig.Stations.Stations);
            Assert.Equal(Lower, shown.Node);

            changed.Clear();
            gone.Clear();
            rig.Stations.OnPeerLeft(Lower);
            rig.Stations.DoWork();

            // the callsign went to the other node: one that goes, one that comes
            Assert.Equal(Lower, Assert.Single(gone).Node);
            Assert.Equal(Higher, Assert.Single(changed).Node);
            Assert.Equal(Higher, Assert.Single(rig.Stations.Stations).Node);
        }

        // ---- the node's own list

        [Fact]
        public void ARemoteStation_FillsWhatTheNodesPeerInfoDoes()
        {
            rig.Peers.OnPeerJoined(Higher, Higher.ToEndPoint());
            rig.Peers.Handle(SessionRig.From(Higher), new PeerInfo { Nickname = "Ben", Atc = true, AtcAirport = "EDDM", AtcLevel = 3, AtcFrequency = unchecked((short)(137_000 - 100_000)), Version = "26.7", Simulator = "" });
            Assert.Equal("EDDM_APP", rig.Peers.GetNodeCallsign(Higher)); // airport and level, what an older build tells

            Member(Higher);
            Receive(Higher, Announce("EDDM_N_APP", khz: 137_000));

            Assert.Equal("EDDM_N_APP", rig.Peers.GetNodeCallsign(Higher));
            Assert.Equal(37_000, rig.Peers.Nodes[Higher].atcFrequency); // R8: in full, not the 16 bit field

            // the next PeerInfo does not take it back
            rig.Peers.Handle(SessionRig.From(Higher), new PeerInfo { Nickname = "Ben", Atc = true, AtcAirport = "EDDM", AtcLevel = 3, AtcFrequency = unchecked((short)(137_000 - 100_000)), Version = "26.7", Simulator = "" });
            Assert.Equal("EDDM_N_APP", rig.Peers.GetNodeCallsign(Higher));
            Assert.Equal(37_000, rig.Peers.Nodes[Higher].atcFrequency);

            Receive(Higher, Announce("EDDM_N_APP", online: false));
            Assert.Equal("EDDM_APP", rig.Peers.GetNodeCallsign(Higher));
        }

        [Fact]
        public void AnObserver_IsNoControllerInTheNodesList()
        {
            rig.Peers.OnPeerJoined(Higher, Higher.ToEndPoint());
            rig.Peers.Handle(SessionRig.From(Higher), new PeerInfo { Nickname = "Ben", Atc = false, Version = "26.7", Simulator = "" });
            Member(Higher);

            Receive(Higher, Announce("EDDM_OBS", facility: 0, khz: 0));

            Assert.Equal("", rig.Peers.Nodes[Higher].atcCallsign);
        }
    }
}
