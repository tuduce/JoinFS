using JoinFS.Fsd;
using JoinFS.Net;
using JoinFS.Tests.Session;

namespace JoinFS.Tests.Atc
{
    /// <summary>docs/ATC/atc-fsd-plan.md stage 3: the logged-in station is this node's ATC presence.</summary>
    public class LocalAtcTests
    {
        static readonly NodeId Peer = NodeId.FromLegacy(0x0A000001, 6112, 1);
        static readonly NodeId Other = NodeId.FromLegacy(0x0A000002, 6112, 2);

        static AtcStationInfo Station(string callsign, FsdFacility facility, int khz = 128550, bool positionKnown = true) =>
            new(callsign, "Carla", 4, facility, 50, khz, 48.35377, 11.78609, positionKnown);

        static LocalAtc FromStation(string callsign, FsdFacility facility, int khz = 128550) =>
            LocalAtc.Resolve(Station(callsign, facility, khz));

        // ---- the mapping from a station

        [Theory]
        [InlineData(FsdFacility.Delivery, 0)]
        [InlineData(FsdFacility.Ground, 1)]
        [InlineData(FsdFacility.Tower, 2)]
        [InlineData(FsdFacility.Approach, 3)]
        [InlineData(FsdFacility.Centre, 4)]
        [InlineData(FsdFacility.FlightServiceStation, 4)]
        public void TheLevelComesFromTheFacility(FsdFacility facility, int level)
        {
            LocalAtc atc = FromStation("EDDM_XXX", facility);

            Assert.True(atc.Active);
            Assert.Equal(level, atc.Level);
        }

        [Fact]
        public void AFacilityThatDisagreesWithTheSuffix_WinsOverTheSuffix()
        {
            LocalAtc atc = FromStation("EDDM_TWR", FsdFacility.Approach);

            Assert.Equal(("EDDM_TWR", "EDDM", 3), (atc.Callsign, atc.Airport, atc.Level));
        }

        [Theory]
        [InlineData("EDDM_TWR", "EDDM")]
        [InlineData("EDDM_N_TWR", "EDDM")]
        [InlineData("EDMM_ALB_CTR", "EDMM")]
        [InlineData("EDDM", "EDDM")]
        public void TheAirportIsTheCallsignUpToItsFirstUnderscore_AndTheCallsignStaysWhole(string callsign, string airport)
        {
            LocalAtc atc = FromStation(callsign, FsdFacility.Tower);

            Assert.Equal((callsign, airport), (atc.Callsign, atc.Airport));
        }

        [Fact]
        public void ADividedPosition_IsTowerAtTheAirport()
        {
            LocalAtc north = FromStation("EDDM_N_TWR", FsdFacility.Tower, 118700);
            LocalAtc south = FromStation("EDDM_S_TWR", FsdFacility.Tower, 119250);

            // a legacy peer sees both as tower at EDDM, told apart by frequency
            Assert.Equal(("EDDM", 2, 18700), (north.Airport, north.Level, north.Frequency));
            Assert.Equal(("EDDM", 2, 19250), (south.Airport, south.Level, south.Frequency));
        }

        [Fact]
        public void AnObserver_IsNotActive_ButTheStationIsOnline()
        {
            LocalAtc atc = FromStation("EDDM_OBS", FsdFacility.Observer);

            Assert.False(atc.Active);
            Assert.True(atc.StationOnline);
            Assert.Equal("", atc.Airport);
        }

        [Fact]
        public void BeforeTheFirstPositionReport_TheStationIsOnlineButNotYetActive()
        {
            LocalAtc atc = LocalAtc.Resolve(Station("EDDM_TWR", FsdFacility.Observer, 0, positionKnown: false));

            Assert.False(atc.Active);
            Assert.True(atc.StationOnline);
        }

        [Theory]
        [InlineData(128550, 28550)]
        [InlineData(118000, 18000)]
        [InlineData(0, 0)]
        [InlineData(99999, 0)]
        [InlineData(200000, 0)]
        public void TheFrequencyIsInTheFormPeerInfoUses(int khz, int expected)
        {
            Assert.Equal(expected, FromStation("EDDM_TWR", FsdFacility.Tower, khz).Frequency);
        }

        [Fact]
        public void TheStationsOwnPositionIsKept()
        {
            LocalAtc atc = FromStation("EDDM_TWR", FsdFacility.Tower);

            Assert.True(atc.HasPosition);
            Assert.Equal((48.35377, 11.78609), (atc.Latitude, atc.Longitude));
        }

        // ---- no station

        [Fact]
        public void NoStation_IsInactive()
        {
            LocalAtc atc = LocalAtc.Resolve(null);

            Assert.False(atc.Active);
            Assert.False(atc.StationOnline);
            Assert.Equal("", atc.Callsign);
        }

        [Fact]
        public void AnObserver_DoesNotMakeTheNodeAController()
        {
            LocalAtc observer = LocalAtc.Resolve(Station("EDDM_OBS", FsdFacility.Observer));
            Assert.False(observer.Active);
        }

        // ---- what makes a PeerInfo worth sending

        [Fact]
        public void APositionReport_IsNotAChangeAPeerCanSee_ButALoginAndALogoffAre()
        {
            LocalAtc a = FromStation("EDDM_TWR", FsdFacility.Tower);
            LocalAtc moved = a with { Latitude = 49.0 };
            LocalAtc retuned = FromStation("EDDM_TWR", FsdFacility.Tower, 118700);

            Assert.True(a.SamePresenceAs(moved));
            Assert.False(a.SamePresenceAs(retuned));
            Assert.False(LocalAtc.Inactive.SamePresenceAs(a));
            Assert.False(a.SamePresenceAs(LocalAtc.Inactive));
            // an observer logging in changes what feeds the full rate
            Assert.False(LocalAtc.Inactive.SamePresenceAs(FromStation("EDDM_OBS", FsdFacility.Observer)));
        }

        // ---- from the gateway to the session

        [Fact]
        public void TheGatewaysStation_BecomesLocalAtc_AndGoesWhenItLogsOff()
        {
            var gateway = new AtcGateway(_ => { });
            var connection = new FakeFsdConnection();
            gateway.OnConnected(connection);
            gateway.OnLine(connection, "#AAEDDM_N_TWR:SERVER:Carla:1234567:token:4:100");

            // logged in, no position yet
            LocalAtc waiting = LocalAtc.Resolve(gateway.Station);
            Assert.True(waiting.StationOnline);
            Assert.False(waiting.Active);

            gateway.OnLine(connection, "%EDDM_N_TWR:18700:4:50:4:48.35377:11.78609:0");
            LocalAtc online = LocalAtc.Resolve(gateway.Station);
            Assert.Equal(("EDDM_N_TWR", "EDDM", 2, 18700, true), (online.Callsign, online.Airport, online.Level, online.Frequency, online.Active));

            gateway.OnLine(connection, "#DAEDDM_N_TWR:SERVER:1234567");
            LocalAtc gone = LocalAtc.Resolve(gateway.Station);
            Assert.False(gone.Active);
            Assert.False(gone.StationOnline);
        }

        // ---- what the session sends with a station online

        /// <summary>What <c>MainSessionHost</c> reads from <c>Main.LocalAtc</c>.</summary>
        static void Apply(FakeProfile profile, LocalAtc atc)
        {
            profile.Atc = atc.Active;
            profile.AtcCallsign = atc.Callsign;
            profile.AtcAirport = atc.Airport;
            profile.AtcLevel = atc.Level;
            profile.AtcFrequency = atc.Frequency;
            profile.FeedFullRate = atc.StationOnline;
        }

        [Fact]
        public void PeerInfo_CarriesTheStation_AsTheOldModeCarriedItsSettings()
        {
            var rig = new SessionRig();
            rig.Sim.SimulatorConnected = false;
            LocalAtc atc = FromStation("EDDM_N_TWR", FsdFacility.Tower, 118700);
            Apply(rig.Profile, atc);

            PeerInfo info = rig.Peers.BuildPeerInfo(Peer);

            Assert.Equal((true, "EDDM", (byte)2, (short)18700), (info.Atc, info.AtcAirport, info.AtcLevel, info.AtcFrequency));
            // no simulator, but pilots must still send this node positions at the full rate
            Assert.True(info.SimulatorConnected);
            // this build shows the whole callsign for its own controller
            Assert.Equal("EDDM_N_TWR", rig.Peers.GetLocalCallsign());
            Assert.Equal("EDDM_N_TWR", rig.Peers.GetNodeCallsign(new NodeId()));
        }

        [Fact]
        public void PeerInfo_WithAnObserverOnline_IsNotAController_ButFeedsTheFullRate()
        {
            var rig = new SessionRig();
            rig.Sim.SimulatorConnected = false;
            LocalAtc atc = FromStation("EDDM_OBS", FsdFacility.Observer);
            Apply(rig.Profile, atc);

            PeerInfo info = rig.Peers.BuildPeerInfo(Peer);

            Assert.False(info.Atc);
            Assert.True(info.SimulatorConnected);
            Assert.Equal(0, rig.Peers.GetMainAtc(out _, out _));
        }

        [Fact]
        public void ALoginOrLogoff_SendsPeerInfoToEveryNodeAtOnce_OnlyInASession()
        {
            var rig = new SessionRig();
            rig.Peers.SendPeerInfoToAll();
            Assert.Empty(rig.Outbox.Sent); // not in a session

            rig.Session.Snapshot = new NetworkSnapshot
            {
                State = SessionState.Connected,
                Peers = new[] { Peer, Other }.ToDictionary(id => id, id => new PeerSnapshot { Id = id, EndPoint = id.ToEndPoint() }),
                PeerList = new[] { Peer, Other }.Select(id => new PeerSnapshot { Id = id, EndPoint = id.ToEndPoint() }).ToList(),
            };
            rig.Peers.SendPeerInfoToAll();

            Assert.Equal(new[] { Peer, Other }, rig.Outbox.Of<PeerInfo>().Select(s => s.To));
        }

        [Fact]
        public void TheLocalHubUser_ShowsTheWholeCallsign()
        {
            var rig = new SessionRig();
            rig.Profile.Hub = true;
            Apply(rig.Profile, FromStation("EDDM_N_TWR", FsdFacility.Tower, 118700));
            rig.Sim.Airports["EDDM"] = new Main.Airport();

            rig.Clock.Advance(11);
            rig.Host.DoLocalUsers();

            HubDirectory.HubUser user = Assert.Single(rig.Host.LocalUsers);
            Assert.Equal("EDDM_N_TWR", user.flightPlan.callsign);
        }
    }
}
