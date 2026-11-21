using JoinFS.Fsd;
using JoinFS.Net;

namespace JoinFS.Tests.Session
{
    /// <summary>docs/ATC/atc-fsd-plan.md stage 8: sending and receiving <see cref="AtcRelay"/> and the checks of §4.4, with <see cref="SessionRig"/>.</summary>
    public class AtcSessionRelayTests
    {
        static readonly NodeId Ben = NodeId.FromLegacy(0x0A000001, 6112, 4);
        static readonly NodeId Sam = NodeId.FromLegacy(0x0B000001, 6112, 4);
        static readonly NodeId Stranger = NodeId.FromLegacy(0x0C000001, 6112, 4);

        readonly SessionRig rig = new();
        readonly List<string> received = [];

        public AtcSessionRelayTests()
        {
            rig.Stations.LineReceived += received.Add;
        }

        static AtcStationInfo Local(string callsign = "EDDM_TWR") =>
            new(callsign, "Carla", 4, FsdFacility.Tower, 50, 118_700, 48.35, 11.78, true);

        static AtcStation Announce(string callsign, int slot = 0, uint khz = 119_150) => new()
        {
            Slot = (byte)slot, Online = true, Facility = 5, Rating = 5, Visibility = 100, Frequency = khz,
            Latitude = 48.1, Longitude = 11.5, Callsign = callsign, Name = "x",
        };

        void Station(NodeId node, string callsign, int slot = 0)
        {
            rig.Stations.OnPeerJoined(node);
            rig.Stations.Handle(SessionRig.From(node), Announce(callsign, slot));
            rig.Stations.DoWork();
        }

        void Receive(NodeId from, string line) => rig.Stations.Handle(SessionRig.From(from), new AtcRelay { Line = line });

        IEnumerable<Sent> Relays => rig.Outbox.Of<AtcRelay>();

        // ---- sending

        [Fact]
        public void ALineToAStation_IsOneGuaranteedUnicast_ToItsNode()
        {
            Station(Ben, "EDDM_APP");
            Station(Sam, "EDDM_GND");

            AtcRelayResult result = rig.Stations.Relay("$HOEDDM_TWR:EDDM_APP:DLH4TK", "EDDM_APP");

            Assert.Equal(AtcRelayResult.Sent, result);
            Sent sent = Assert.Single(Relays);
            Assert.Equal(Ben, sent.To);
            Assert.True(sent.Guaranteed);
            Assert.False(sent.Broadcast);
            Assert.Equal("$HOEDDM_TWR:EDDM_APP:DLH4TK", ((AtcRelay)sent.Message).Line);
            Assert.Equal(1, rig.Stations.RelayedOut);
        }

        [Fact]
        public void ALineToAnUnknownStation_IsNotSent_AndSaysSo()
        {
            Station(Ben, "EDDM_APP");

            Assert.Equal(AtcRelayResult.NoSuchStation, rig.Stations.Relay("#TMEDDM_TWR:EDDM_GND:hi", "EDDM_GND"));

            Assert.Empty(Relays);
        }

        [Fact]
        public void AGroupLine_GoesOnceToEveryNodeWithAStation_AndNotToOthers()
        {
            Station(Ben, "EDDM_APP");
            Station(Sam, "EDDM_GND");
            Station(Sam, "EDDM_ATIS", slot: 1); // a second station of the same node: still one line
            rig.Stations.OnPeerJoined(Stranger);  // a member without a station

            rig.Stations.RelayToAll("$CQEDDM_TWR:@94835:IT:DLH4TK");

            Assert.Equal(new[] { Ben, Sam }.OrderBy(n => n.ToString()), Relays.Select(r => r.To).OrderBy(n => n.ToString()));
            Assert.All(Relays, r => Assert.True(r.Guaranteed));
            Assert.Equal(2, rig.Stations.RelayedOut);
        }

        [Fact]
        public void AGroupLine_WithNoStationOnTheSession_GoesNowhere_AndIsNotACount()
        {
            rig.Stations.RelayToAll("$CQEDDM_TWR:@94835:WH:DLH4TK");

            Assert.Empty(Relays);
            Assert.Equal(0, rig.Stations.RelayedOut);
        }

        [Fact]
        public void ALineOverTheLimit_IsNotSent_AndIsLogged_ForOneStationAndForAGroup()
        {
            Station(Ben, "EDDM_APP");
            Station(Sam, "EDDM_GND");
            string longLine = "$CQEDDM_TWR:EDDM_APP:GD:" + new string('x', FsdRelayPolicy.MaxLineBytes);

            Assert.Equal(AtcRelayResult.Refused, rig.Stations.Relay(longLine, "EDDM_APP"));
            rig.Stations.RelayToAll(longLine);

            Assert.Empty(Relays);
            Assert.Equal(2, rig.Stations.Dropped(AtcRelayDrop.TooLong)); // the group line once, not once per node
            Assert.Equal(2, rig.Log.Events.Count(e => e.Contains("TooLong")));
        }

        [Fact]
        public void ALineAtTheLimit_IsSent()
        {
            Station(Ben, "EDDM_APP");
            string line = "#TMEDDM_TWR:EDDM_APP:" + new string('x', FsdRelayPolicy.MaxLineBytes - "#TMEDDM_TWR:EDDM_APP:".Length);
            Assert.Equal(FsdRelayPolicy.MaxLineBytes, line.Length);

            Assert.Equal(AtcRelayResult.Sent, rig.Stations.Relay(line, "EDDM_APP"));
        }

        [Fact]
        public void ALineWithAControlCharacter_OrALandline_IsNotSent()
        {
            Station(Ben, "EDDM_APP");

            Assert.Equal(AtcRelayResult.Refused, rig.Stations.Relay("#TMEDDM_TWR:EDDM_APP:a\rb", "EDDM_APP"));
            Assert.Equal(AtcRelayResult.Refused, rig.Stations.Relay("#PCEDDM_TWR:EDDM_APP:CCP:IC:1.2.3.4", "EDDM_APP"));

            Assert.Empty(Relays);
            Assert.Equal(1, rig.Stations.Dropped(AtcRelayDrop.ControlCharacter));
            Assert.Equal(1, rig.Stations.Dropped(AtcRelayDrop.Landline));
        }

        // ---- receiving: the checks of §4.4

        [Fact]
        public void ALineFromTheOriginsOwnStation_IsDelivered()
        {
            Station(Ben, "EDDM_APP");
            rig.Stations.SetLocalStation(0, Local());

            Receive(Ben, "$HOEDDM_APP:EDDM_TWR:DLH4TK");

            Assert.Equal(["$HOEDDM_APP:EDDM_TWR:DLH4TK"], received);
            Assert.Equal(1, rig.Stations.RelayedIn);
        }

        [Fact]
        public void ALineWhoseVerbIsNotInTheList_IsDropped()
        {
            Station(Ben, "EDDM_APP");
            rig.Stations.SetLocalStation(0, Local());

            Receive(Ben, "$FPEDDM_APP:*A:I:A320:450:EDDM:0:0:FL350:EDDF:1:0:2:0:EDDS::");
            Receive(Ben, "$!!EDDM_APP:EDDM_TWR:hello");
            Receive(Ben, "#DAEDDM_APP:1234567");

            Assert.Empty(received);
            Assert.Equal(3, rig.Stations.Dropped(AtcRelayDrop.VerbNotRelayed));
        }

        [Fact]
        public void ALineWhoseFromIsNotTheOriginsStation_IsDropped()
        {
            Station(Ben, "EDDM_APP");
            Station(Sam, "EDDM_GND");
            rig.Stations.SetLocalStation(0, Local());

            Receive(Ben, "$HOEDDM_GND:EDDM_TWR:DLH4TK");   // Ben speaks for Sam's station
            Receive(Ben, "$HONOBODY:EDDM_TWR:DLH4TK");     // for none
            Receive(Ben, "$HOEDDM_TWR:EDDM_TWR:DLH4TK");   // for the local one

            Assert.Empty(received);
            Assert.Equal(3, rig.Stations.Dropped(AtcRelayDrop.UnknownSender));
        }

        [Fact]
        public void ALineFromAStationThatLoggedOff_IsDropped()
        {
            Station(Ben, "EDDM_APP");
            rig.Stations.SetLocalStation(0, Local());
            rig.Stations.Handle(SessionRig.From(Ben), new AtcStation { Slot = 0, Online = false, Callsign = "EDDM_APP" });

            Receive(Ben, "$HOEDDM_APP:EDDM_TWR:DLH4TK");

            Assert.Empty(received);
            Assert.Equal(1, rig.Stations.Dropped(AtcRelayDrop.UnknownSender));
        }

        [Fact]
        public void ALineFromTheAtisStationOfTheNode_IsDelivered()
        {
            Station(Ben, "EDDM_APP");
            Station(Ben, "EDDM_ATIS", slot: 1);
            rig.Stations.SetLocalStation(0, Local());

            Receive(Ben, "$CREDDM_ATIS:EDDM_TWR:ATIS:A:EDDM information");

            Assert.Single(received);
        }

        [Fact]
        public void ALineWithAControlCharacter_IsDropped()
        {
            Station(Ben, "EDDM_APP");
            rig.Stations.SetLocalStation(0, Local());

            Receive(Ben, "#TMEDDM_APP:EDDM_TWR:hello\nworld");

            Assert.Empty(received);
            Assert.Equal(1, rig.Stations.Dropped(AtcRelayDrop.ControlCharacter));
        }

        [Fact]
        public void ALine_WhenThisInstanceHasNoStation_IsDropped()
        {
            Station(Ben, "EDDM_APP");

            Receive(Ben, "$HOEDDM_APP:EDDM_TWR:DLH4TK");

            Assert.Empty(received);
            Assert.Equal(1, rig.Stations.Dropped(AtcRelayDrop.NoLocalStation));
        }

        [Fact]
        public void ALine_FromANodeThatIsNoMember_OrFromThisNode_IsDropped()
        {
            rig.Stations.SetLocalStation(0, Local());

            Receive(Stranger, "$HOEDDM_APP:EDDM_TWR:DLH4TK");
            Receive(rig.Session.LocalId, "$HOEDDM_TWR:EDDM_TWR:DLH4TK");

            Assert.Empty(received);
            Assert.Equal(2, rig.Stations.Dropped(AtcRelayDrop.NotAMember));
        }

        [Fact]
        public void AnOverlongOrMalformedLine_IsDropped()
        {
            Station(Ben, "EDDM_APP");
            rig.Stations.SetLocalStation(0, Local());

            Receive(Ben, "#TMEDDM_APP:EDDM_TWR:" + new string('x', 1000));
            Receive(Ben, "");

            Assert.Empty(received);
            Assert.Equal(1, rig.Stations.Dropped(AtcRelayDrop.TooLong));
            Assert.Equal(1, rig.Stations.Dropped(AtcRelayDrop.Malformed));
        }

        // ---- the monitor

        [Fact]
        public void TheCounters_AreWrittenToTheMonitorOnce_WhenTheyChanged()
        {
            Station(Ben, "EDDM_APP");
            rig.Stations.SetLocalStation(0, Local());
            Assert.Equal("", rig.Stations.CounterText());

            rig.Stations.Relay("$HOEDDM_TWR:EDDM_APP:DLH4TK", "EDDM_APP");
            Receive(Ben, "$HAEDDM_APP:EDDM_TWR:DLH4TK");
            Receive(Ben, "$FPEDDM_APP:*A:I");

            rig.Clock.Advance(AtcSession.CounterIntervalS + 1);
            rig.Stations.DoWork();
            rig.Clock.Advance(AtcSession.CounterIntervalS + 1);
            rig.Stations.DoWork(); // unchanged: not repeated

            Assert.Equal("ATC lines: sent 1, received 1, dropped VerbNotRelayed 1", rig.Log.NetworkLines.Single(l => l.StartsWith("ATC lines")));
        }
    }
}
