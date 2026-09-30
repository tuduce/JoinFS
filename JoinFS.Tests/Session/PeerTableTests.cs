using System.Net;
using JoinFS.Net;

namespace JoinFS.Tests.Session
{
    /// <summary>PeerTable: what nodes tell us about themselves, and what we tell them.</summary>
    public class PeerTableTests
    {
        static readonly NodeId Peer = new(0x0A000001, 6112, 1);
        static readonly NodeId Other = new(0x0A000002, 6112, 2);

        static PeerInfo Info(string nickname, bool hub = false, bool atc = false, string airport = "", byte level = 2, short frequency = 22800) => new()
        {
            Nickname = nickname, Hub = hub, Atc = atc, AtcAirport = airport, AtcLevel = level, AtcFrequency = frequency,
            Version = "26.6", Simulator = "MSFS", SimulatorConnected = true, ActivityCircle = 60,
        };

        static NetworkSnapshot WithPeers(params NodeId[] ids)
        {
            var peers = ids.Select(id => new PeerSnapshot { Id = id, EndPoint = id.ToEndPoint(id.port) }).ToList();
            return new NetworkSnapshot { State = SessionState.Connected, Peers = peers.ToDictionary(p => p.Id), PeerList = peers };
        }

        [Fact]
        public void PeerInfo_DescribesTheNode_AndReportsWhatChanged()
        {
            var rig = new SessionRig();
            rig.Peers.OnPeerJoined(Peer, Peer.ToEndPoint(Peer.port));

            rig.Peers.Handle(SessionRig.From(Peer), Info("Bob", hub: true, atc: true, airport: "EDDF", level: 3, frequency: 12345));

            PeerTable.Node node = rig.Peers.Nodes[Peer];
            Assert.Equal(("Bob", true, "26.6", "MSFS", (byte)60), (node.nickname, node.atc, node.version, node.simulator, node.activityCircle));
            Assert.Equal(Peer, Assert.Single(rig.Sim.NicknameChanges));
            Assert.Equal("add EDDF 3 12345", Assert.Single(rig.Atc.Calls));
            // it became a hub: the directory checks it
            Assert.Single(rig.Outbox.Messages<StatusRequestUpdate>());

            // stops controlling; nickname and hub unchanged
            rig.Peers.Handle(SessionRig.From(Peer), Info("Bob", hub: true));
            Assert.Single(rig.Sim.NicknameChanges);
            Assert.Equal("remove EDDF 3 12345", rig.Atc.Calls[1]);
            Assert.Single(rig.Outbox.Messages<StatusRequestUpdate>());
        }

        [Fact]
        public void PeerInfo_FromAnUnknownNode_OnlyAppliesCockpitSharing()
        {
            var rig = new SessionRig();
            rig.Peers.Handle(SessionRig.From(Peer), Info("Bob") with { Share = ShareCockpitFlags.Cockpit });

            Assert.Equal((Peer, ShareCockpitFlags.Cockpit), Assert.Single(rig.Sim.Shares));
            Assert.Empty(rig.Peers.Nodes);
            Assert.Empty(rig.Sim.NicknameChanges);
        }

        [Fact]
        public void BuildPeerInfo_SaysWhatWeShareWithThatNode()
        {
            var rig = new SessionRig();
            Assert.Equal(ShareCockpitFlags.None, rig.Peers.BuildPeerInfo(Peer).Share);

            rig.Policy.SharingNodes.Add(Peer);
            rig.Peers.shareFlightControls = Peer;
            rig.Peers.shareNavControls = Other;
            Assert.Equal(ShareCockpitFlags.Cockpit | ShareCockpitFlags.FlightControls, rig.Peers.BuildPeerInfo(Peer).Share);
            Assert.Equal(ShareCockpitFlags.NavControls, rig.Peers.BuildPeerInfo(Other).Share);

            rig.Profile.ShareCockpitEveryone = true;
            Assert.Equal(ShareCockpitFlags.Cockpit | ShareCockpitFlags.NavControls, rig.Peers.BuildPeerInfo(Other).Share);

            PeerInfo info = rig.Peers.BuildPeerInfo(Other);
            Assert.Equal(("Me", "TestSim", true), (info.Nickname, info.Simulator, info.SimulatorConnected));
        }

        [Fact]
        public void ScheduledPeerInfo_IsSentOnce_ToTheFirstNodeScheduled()
        {
            var rig = new SessionRig();
            rig.Peers.SchedulePeerInfo(Peer);
            rig.Peers.SchedulePeerInfo(Other);
            rig.Peers.DoScheduled();
            rig.Peers.DoScheduled();

            Assert.Equal(Peer, Assert.Single(rig.Outbox.Of<PeerInfo>()).To);
        }

        [Fact]
        public void PeerInfo_GoesToEveryNodeEveryFiveSeconds_WhileInASession()
        {
            var rig = new SessionRig();
            rig.Session.Snapshot = WithPeers(Peer, Other);

            rig.Peers.DoWork();
            Assert.Empty(rig.Outbox.Sent);

            rig.Clock.Advance(5.1);
            rig.Peers.DoWork();
            Assert.Equal(new[] { Peer, Other }, rig.Outbox.Of<PeerInfo>().Select(s => s.To));

            rig.Session.Connected = false;
            rig.Clock.Advance(5.1);
            rig.Peers.DoWork();
            Assert.Equal(2, rig.Outbox.Sent.Count);
        }

        [Fact]
        public void Names_FallBackToTheAddress_AndInvalidMeansThisNode()
        {
            var rig = new SessionRig();
            rig.Session.Snapshot = WithPeers(Peer);

            Assert.Equal("Me", rig.Peers.GetNodeName(new NodeId()));
            Assert.Equal(Peer.ToEndPoint(Peer.port).ToString(), rig.Peers.GetNodeName(Peer));
            Assert.Equal("", rig.Peers.GetNodeName(Other));

            rig.Peers.OnPeerJoined(Peer, Peer.ToEndPoint(Peer.port));
            rig.Peers.Handle(SessionRig.From(Peer), Info("Bob"));
            Assert.Equal("Bob", rig.Peers.GetNodeName(Peer));
        }

        [Fact]
        public void MainAtc_IsThisNodeFirst_ThenTheSessions()
        {
            var rig = new SessionRig();
            rig.Peers.OnPeerJoined(Peer, Peer.ToEndPoint(Peer.port));
            rig.Peers.Handle(SessionRig.From(Peer), Info("Bob", atc: true, airport: "eddf", level: 4));
            Assert.Equal((1, "EDDF", 4), (rig.Peers.GetMainAtc(out string airport, out int level), airport, level));

            rig.Profile.Atc = true;
            rig.Profile.AtcAirport = "LROP";
            Assert.Equal((2, "LROP", 2), (rig.Peers.GetMainAtc(out airport, out level), airport, level));
            Assert.Equal("LROP_TWR", rig.Peers.GetLocalCallsign());
        }

        [Fact]
        public void JoiningASession_ResetsSharedControls()
        {
            var rig = new SessionRig();
            rig.Peers.shareFlightControls = Peer;
            rig.Peers.shareAncillaryControls = Peer;
            rig.Peers.OnSessionJoined();

            Assert.True(rig.Peers.shareFlightControls.Invalid());
            Assert.True(rig.Peers.shareAncillaryControls.Invalid());
        }
    }
}
