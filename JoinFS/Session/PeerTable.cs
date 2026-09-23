using JoinFS.Net;
using JoinFS.Properties;
using System;
using System.Collections.Generic;
using System.Net;

namespace JoinFS
{
    /// <summary>
    /// The application's view of the nodes in the session: what each one told us about itself
    /// (<see cref="PeerInfo"/>: nickname, ATC, version, simulator...), and what we share of our
    /// cockpit with whom. Transport state (endpoint, RTT, protocol) is in the network snapshot.
    /// </summary>
    public sealed class PeerTable
    {
        /// <summary>A node in the session, as it describes itself.</summary>
        public class Node
        {
            public ushort dataVersion = 0;
            public string nickname = "";
            public Guid guid = Guid.Empty;
            public bool hub = false;
            public bool atc = false;
            public string atcAirport = "";
            public int atcLevel = 2;
            public int atcFrequency = 22800;
            public byte activityCircle = 40;
            public string version = "";
            public string simulator = "";
            public bool simulatorConnected = false;
        }

        /// <summary>Nodes in the session (not including this one).</summary>
        public readonly Dictionary<NodeId, Node> Nodes = [];

        /// <summary>Who has which of our controls (shared cockpit).</summary>
        public NodeId shareFlightControls = new();
        public NodeId shareAncillaryControls = new();
        public NodeId shareNavControls = new();

        /// <summary>A node told us it is a hub (the endpoint to check it on).</summary>
        public event Action<IPEndPoint> HubAnnounced;

        readonly INetworkOutbox outbox;
        readonly ISessionState session;
        readonly ILocalProfile profile;
        readonly ISimView simView;
        readonly ISimSink sim;
        readonly IPeerPolicy policy;
        readonly IAtcListener atc;
        readonly IClock clock;
        readonly ISessionLog log;

        readonly Timer peerInfoTimer = new(5.0);

        /// <summary>A PeerInfo to send on the next tick (e.g. after changing cockpit sharing in the UI).</summary>
        NodeId schedulePeerInfo = new();

        public PeerTable(INetworkOutbox outbox, ISessionState session, ILocalProfile profile, ISimView simView, ISimSink sim, IPeerPolicy policy, IAtcListener atc, IClock clock, ISessionLog log)
        {
            this.outbox = outbox;
            this.session = session;
            this.profile = profile;
            this.simView = simView;
            this.sim = sim;
            this.policy = policy;
            this.atc = atc;
            this.clock = clock;
            this.log = log;
            // first periodic PeerInfo after one interval
            peerInfoTimer.Elapsed(clock.Now);
        }

        // ------------------------------------------------------------------ session events

        public void OnSessionJoined()
        {
            log.Event("Joined session");
            shareFlightControls = new NodeId();
            shareAncillaryControls = new NodeId();
            shareNavControls = new NodeId();
        }

        public void OnPeerJoined(NodeId nuid, IPEndPoint endPoint)
        {
            Nodes[nuid] = new Node();
            log.Event("Added node '" + nuid + "' @ '" + AddressCodec.EncodeIP(endPoint.ToString()) + "'");
        }

        public void OnPeerEstablished(NodeId nuid)
        {
            log.Event("Connected '" + nuid + "'");
            SendPeerInfo(nuid);
        }

        public void OnPeerLeft(NodeId nuid)
        {
            Nodes.Remove(nuid);
            log.Event("Removed node '" + nuid + "'");
        }

        // ------------------------------------------------------------------ PeerInfo

        /// <summary>Send our PeerInfo to <paramref name="nuid"/> on the next tick.</summary>
        public void SchedulePeerInfo(NodeId nuid)
        {
            if (schedulePeerInfo.Invalid())
            {
                schedulePeerInfo = nuid;
            }
        }

        /// <summary>Send the scheduled PeerInfo, if any (only once this node can be in a session).</summary>
        public void DoScheduled()
        {
            if (schedulePeerInfo.Valid())
            {
                SendPeerInfo(schedulePeerInfo);
                schedulePeerInfo = new NodeId();
            }
        }

        /// <summary>Our PeerInfo to every node, every 5 s.</summary>
        public void DoWork()
        {
            if (peerInfoTimer.Elapsed(clock.Now) && session.Connected)
            {
                try
                {
                    foreach (PeerSnapshot peer in session.Snapshot.PeerList)
                    {
                        SendPeerInfo(peer.Id);
                    }
                }
                catch (Exception ex)
                {
                    log.Event("Failed to write SharedData message: " + ex.Message);
                }
            }
        }

        public void SendPeerInfo(NodeId nuid) => outbox.SendTo(nuid, BuildPeerInfo(nuid));

        /// <summary>Our description of ourselves, including what we share with <paramref name="nuid"/>.</summary>
        public PeerInfo BuildPeerInfo(NodeId nuid)
        {
            ShareCockpitFlags share = ShareCockpitFlags.None;
            if (policy.ShareCockpit(nuid) || profile.ShareCockpitEveryone) share |= ShareCockpitFlags.Cockpit;
            if (nuid == shareFlightControls) share |= ShareCockpitFlags.FlightControls;
            if (nuid == shareAncillaryControls) share |= ShareCockpitFlags.AncillaryControls;
            if (nuid == shareNavControls) share |= ShareCockpitFlags.NavControls;
            return new PeerInfo
            {
                Share = share,
                Nickname = profile.Nickname,
                Guid = profile.Guid,
                Hub = profile.Hub,
                Atc = profile.Atc,
                SimulatorConnected = simView.Available && simView.SimulatorConnected,
                AtcAirport = profile.AtcAirport,
                AtcLevel = (byte)profile.AtcLevel,
                AtcFrequency = (short)profile.AtcFrequency,
                ActivityCircle = (byte)profile.ActivityCircle,
                Version = Main.Version,
                Simulator = simView.Available ? simView.SimulatorName : Resources.Strings.NotConnected,
            };
        }

        public void Handle(in MessageMeta meta, in PeerInfo info)
        {
            NodeId nuid = meta.Sender;
            sim.ShareCockpit(nuid, info.Share);
            if (!Nodes.TryGetValue(nuid, out Node node))
            {
                return;
            }
            node.dataVersion = (ushort)meta.DataVersion;
            if (info.Nickname.Equals(node.nickname) == false)
            {
                node.nickname = info.Nickname;
                sim.NicknameChanged(nuid);
            }
            node.guid = info.Guid;
            if (node.hub == false && info.Hub)
            {
                HubAnnounced?.Invoke(meta.EndPoint);
            }
            node.hub = info.Hub;
            if (node.atc && info.Atc == false)
            {
                atc.RemoveAtc(node.atcAirport, node.atcLevel, node.atcFrequency);
            }
            node.atcAirport = info.AtcAirport;
            node.atcLevel = info.AtcLevel;
            node.atcFrequency = info.AtcFrequency;
            if (node.atc == false && info.Atc)
            {
                atc.AddAtc(node.atcAirport, node.atcLevel, node.atcFrequency);
            }
            node.atc = info.Atc;
            node.activityCircle = info.ActivityCircle;
            node.version = info.Version;
            node.simulator = info.Simulator;
            node.simulatorConnected = info.SimulatorConnected;
        }

        // ------------------------------------------------------------------ queries
        //
        // An invalid NodeId means this node.

        public string GetNodeName(NodeId nuid)
        {
            if (nuid.Invalid()) return profile.Nickname;
            if (Nodes.TryGetValue(nuid, out Node value)) return value.nickname;
            // not described itself yet: use its address
            if (session.Snapshot.Peer(nuid) is PeerSnapshot peer) return peer.EndPoint.ToString();
            return "";
        }

        public Guid GetNodeGuid(NodeId nuid)
        {
            if (nuid.Invalid()) return profile.Guid;
            return Nodes.TryGetValue(nuid, out Node value) ? value.guid : Guid.Empty;
        }

        public string GetNodeCallsign(NodeId nuid)
        {
            if (GetNodeAtc(nuid, out string airport, out int level))
            {
                return Sim.MakeAtcCallsign(airport, level);
            }
            return simView.FindUserAircraft(nuid)?.flightPlan.callsign ?? "";
        }

        public int GetNodeActivityCircle(NodeId nuid)
        {
            if (nuid.Invalid()) return profile.ActivityCircle;
            return Nodes.TryGetValue(nuid, out Node value) ? value.activityCircle : 40;
        }

        /// <summary>The session's first controller (this node first), and how many there are.</summary>
        public int GetMainAtc(out string airport, out int level)
        {
            int atcCount = 0;
            airport = "";
            level = 2;

            if (profile.Atc && profile.AtcAirport.Length > 0)
            {
                airport = profile.AtcAirport;
                level = profile.AtcLevel;
                atcCount++;
            }

            foreach (var node in Nodes.Values)
            {
                if (node.atc && node.atcAirport.Length > 0)
                {
                    if (atcCount == 0)
                    {
                        airport = node.atcAirport.ToUpper();
                        level = node.atcLevel;
                    }
                    atcCount++;
                }
            }
            return atcCount;
        }

        public bool GetNodeAtc(NodeId nuid, out string airport, out int level)
        {
            airport = "";
            level = 0;

            if (nuid.Invalid())
            {
                airport = profile.AtcAirport;
                if (profile.Atc && airport.Length > 0)
                {
                    level = profile.AtcLevel;
                    return true;
                }
                return false;
            }

            if (Nodes.TryGetValue(nuid, out Node value))
            {
                airport = value.atcAirport;
                level = value.atcLevel;
                return value.atc;
            }
            return false;
        }

        public string GetNodeVersion(NodeId nuid)
        {
            if (nuid.Invalid()) return Main.Version;
            return Nodes.TryGetValue(nuid, out Node value) ? value.version : "";
        }

        public string GetNodeSimulator(NodeId nuid)
        {
            if (simView.Available && nuid.Invalid()) return simView.SimulatorName;
            if (Nodes.TryGetValue(nuid, out Node value)) return value.simulator == "" ? Resources.Strings.NotConnected : value.simulator;
            return Resources.Strings.NotConnected;
        }

        public bool GetNodeSimulatorConnected(NodeId nuid)
        {
            if (simView.Available && nuid.Invalid()) return simView.SimulatorConnected;
            return Nodes.TryGetValue(nuid, out Node value) && value.simulatorConnected;
        }

        public string GetLocalCallsign()
        {
            if (GetNodeAtc(new NodeId(), out string airport, out int level))
            {
                return Sim.MakeAtcCallsign(airport, level);
            }
            return simView.UserCallsign;
        }
    }
}
