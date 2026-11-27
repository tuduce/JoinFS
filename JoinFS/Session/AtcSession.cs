using JoinFS.Fsd;
using JoinFS.Net;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;

namespace JoinFS
{
    /// <summary>
    /// A controller station on another node, as that node announced it (<see cref="AtcStation"/>). Immutable; equal
    /// records are the same state, so a repeat of an announcement raises nothing.
    /// </summary>
    /// <param name="Node">The node the station is logged in on.</param>
    /// <param name="Slot">0 the main station, 1 its ATIS.</param>
    /// <param name="Callsign">The whole station callsign (<c>EDDM_N_TWR</c>), checked with <see cref="FsdCallsign.IsValidStation"/>.</param>
    /// <param name="FrequencyKhz">0 when none or out of the range FSD can carry.</param>
    public sealed record AtcRemoteStation(
        NodeId Node, int Slot, string Callsign, string Name, int Rating, FsdFacility Facility, int Visibility,
        int FrequencyKhz, double Latitude, double Longitude);

    /// <summary>
    /// What <c>AtcGateway</c> sees of the mesh (docs/ATC/atc-fsd-plan.md §4.1): the stations of other nodes, and this
    /// node's own station handed over. Everything runs on the app thread; the events come from <see cref="AtcSession.DoWork"/>,
    /// never from inside a call the gateway makes, so a handler may call back into the mesh.
    /// </summary>
    public interface IAtcMesh
    {
        /// <summary>The online stations of other nodes, one per callsign (when two nodes claim one callsign, the lower node id's).</summary>
        IReadOnlyList<AtcRemoteStation> Stations { get; }

        /// <summary>Is the callsign taken by a station on another node? Then a login with it is refused.</summary>
        bool CallsignInUse(string callsign);

        /// <summary>This node's station of <paramref name="slot"/> changed; null: it logged off. Announced once its client sent its first <c>%</c>.</summary>
        void SetLocalStation(int slot, AtcStationInfo station);

        /// <summary>A station came online or changed.</summary>
        event Action<AtcRemoteStation> StationChanged;

        /// <summary>A station logged off, its node left or it was not heard for <see cref="AtcSession.ExpiryS"/>.</summary>
        event Action<AtcRemoteStation> StationGone;

        /// <summary>Two nodes logged in with one callsign and the other node's id is lower (§4.6): this node's station of the slot is gone.</summary>
        event Action<int, string> LocalStationLost;

        /// <summary>
        /// Send a line of this node's client to the node of the station <paramref name="toCallsign"/> (stage 8, §4.3 row 2).
        /// <see cref="AtcRelayResult.NoSuchStation"/> when no other node has it, so the gateway can look further.
        /// </summary>
        AtcRelayResult Relay(string line, string toCallsign);

        /// <summary>Send a line of this node's client to every other node that has an online station (a group address, §4.3 row 4).</summary>
        void RelayToAll(string line);

        /// <summary>A line another node's client sent, checked by §4.4: the gateway writes it to its client. Raised inside the handling of the message, on the app thread.</summary>
        event Action<string> LineReceived;
    }

    /// <summary>What <see cref="IAtcMesh.Relay"/> did with a line.</summary>
    public enum AtcRelayResult
    {
        /// <summary>Sent to the node of the station.</summary>
        Sent,
        /// <summary>No other node has a station with that callsign; nothing was sent.</summary>
        NoSuchStation,
        /// <summary>The station exists but the line may not be sent (too long, a control character, a landline); counted and logged.</summary>
        Refused,
    }

    /// <summary>Why a relayed line was not sent or not delivered (the counters of <see cref="AtcSession"/>).</summary>
    public enum AtcRelayDrop
    {
        /// <summary>Over <see cref="FsdRelayPolicy.MaxLineBytes"/> (R6): not sent, logged.</summary>
        TooLong,
        ControlCharacter,
        VerbNotRelayed,
        Landline,
        /// <summary>Not an FSD line.</summary>
        Malformed,
        /// <summary>Its <c>From</c> is not an online station the origin node announced (§4.4).</summary>
        UnknownSender,
        /// <summary>This instance has no online station (§4.4).</summary>
        NoLocalStation,
        /// <summary>The origin is not a member of the session, or is this node.</summary>
        NotAMember,
    }

    /// <summary>
    /// The station table of the session (docs/ATC/atc-fsd-plan.md §4.1, §4.5, D7): the stations the other nodes announced,
    /// sending this node's own, expiry and the crossing-login rule of §4.6. A session part like <see cref="PeerTable"/>:
    /// it runs on the app thread and sends through <see cref="INetworkOutbox"/>.
    /// </summary>
    public sealed class AtcSession : IAtcMesh
    {
        /// <summary>This node repeats its station this often, seconds (D7).</summary>
        public const double SendIntervalS = 10.0;

        /// <summary>A station not heard for this long is dropped, seconds (D7).</summary>
        public const double ExpiryS = 35.0;

        /// <summary>Main station and ATIS.</summary>
        public const int Slots = 2;

        sealed class Entry
        {
            public AtcRemoteStation Station;
            public double LastHeard;
        }

        readonly INetworkOutbox outbox;
        readonly ISessionState session;
        readonly IClock clock;
        readonly ISessionLog log;

        /// <summary>What every node announced, hidden duplicates included.</summary>
        readonly Dictionary<(NodeId Node, int Slot), Entry> table = new();

        /// <summary>The stations the gateway is told of, by callsign: the table with one station per callsign.</summary>
        readonly Dictionary<string, AtcRemoteStation> published = new();

        /// <summary>The nodes of the session; a node that is not one cannot announce a station.</summary>
        readonly HashSet<NodeId> members = new();

        readonly AtcStationInfo[] local = new AtcStationInfo[Slots];
        readonly List<Action> pending = new();
        double nextSend;

        /// <summary>How often the relay counters are written to the monitor when they changed, seconds.</summary>
        public const double CounterIntervalS = 60.0;

        readonly int[] dropped = new int[Enum.GetValues<AtcRelayDrop>().Length];
        double nextCounterLog;
        string lastCounterText = "";

        public AtcSession(INetworkOutbox outbox, ISessionState session, IClock clock, ISessionLog log)
        {
            this.outbox = outbox;
            this.session = session;
            this.clock = clock;
            this.log = log;
        }

        public event Action<AtcRemoteStation> StationChanged;
        public event Action<AtcRemoteStation> StationGone;
        public event Action<int, string> LocalStationLost;
        public event Action<string> LineReceived;

        /// <summary>Lines sent to other nodes since the start (one per target node).</summary>
        public int RelayedOut { get; private set; }

        /// <summary>Lines received from other nodes and passed to the gateway.</summary>
        public int RelayedIn { get; private set; }

        /// <summary>Lines dropped for <paramref name="reason"/>, sent or received.</summary>
        public int Dropped(AtcRelayDrop reason) => dropped[(int)reason];

        public IReadOnlyList<AtcRemoteStation> Stations => new List<AtcRemoteStation>(published.Values);

        public bool CallsignInUse(string callsign)
        {
            foreach (Entry entry in table.Values)
            {
                if (entry.Station.Callsign == callsign) return true;
            }
            return false;
        }

        // ------------------------------------------------------------------ this node's station

        public void SetLocalStation(int slot, AtcStationInfo station)
        {
            if (slot < 0 || slot >= Slots) return;
            // a station is announced from its first %, when its facility, frequency and position are real
            if (station != null && !station.PositionKnown) station = null;

            AtcStationInfo before = local[slot];
            if (Equals(before, station)) return;
            local[slot] = station;

            if (station != null)
            {
                Broadcast(Describe(slot, station, true));
                nextSend = clock.Now + SendIntervalS;
            }
            else
            {
                // the logoff goes once, with the callsign it had; a node that misses it drops the station after ExpiryS
                Broadcast(Describe(slot, before, false));
            }
            Reconcile();
        }

        void Broadcast(AtcStation message)
        {
            if (session.Connected) outbox.Broadcast(message);
        }

        static AtcStation Describe(int slot, AtcStationInfo station, bool online) => new()
        {
            Slot = (byte)slot,
            Online = online,
            Facility = (byte)station.Facility,
            Rating = (byte)Math.Clamp(station.Rating, 0, 255),
            Visibility = (ushort)Math.Clamp(station.VisibilityRange, 0, ushort.MaxValue),
            Frequency = (uint)Math.Max(0, station.FrequencyKhz),
            Latitude = station.Latitude,
            Longitude = station.Longitude,
            Callsign = station.Callsign,
            Name = station.Name ?? "",
        };

        // ------------------------------------------------------------------ session events

        public void OnPeerJoined(NodeId node) => members.Add(node);

        /// <summary>A node that just joined learns our stations at once, not after up to <see cref="SendIntervalS"/>.</summary>
        public void OnPeerEstablished(NodeId node)
        {
            for (int slot = 0; slot < Slots; slot++)
            {
                if (local[slot] != null) outbox.SendTo(node, Describe(slot, local[slot], true));
            }
        }

        public void OnPeerLeft(NodeId node)
        {
            members.Remove(node);
            bool removed = false;
            for (int slot = 0; slot < Slots; slot++)
            {
                removed |= table.Remove((node, slot));
            }
            if (removed) Reconcile();
        }

        // ------------------------------------------------------------------ incoming

        public void Handle(in MessageMeta meta, in AtcStation message)
        {
            NodeId from = meta.Sender;
            // the origin is the owner: a station is the sender's own, and the sender is a member of this session
            if (from == session.LocalId || !members.Contains(from)) return;
            if (message.Slot >= Slots) return;

            var key = (from, (int)message.Slot);
            if (!message.Online)
            {
                if (table.Remove(key)) Reconcile();
                return;
            }
            // the table feeds FSD lines: what a peer sends is checked here, never trusted
            if (!FsdCallsign.IsValidStation(message.Callsign) || message.Facility > (byte)FsdFacility.Centre) return;

            int khz = message.Frequency >= FsdFrequency.MinKhz && message.Frequency <= FsdFrequency.MaxKhz ? (int)message.Frequency : 0;
            var station = new AtcRemoteStation(from, message.Slot, message.Callsign, Clean(message.Name), message.Rating,
                (FsdFacility)message.Facility, message.Visibility, khz, Degrees(message.Latitude, 90.0), Degrees(message.Longitude, 180.0));

            if (!table.TryGetValue(key, out Entry entry))
            {
                table[key] = entry = new Entry();
            }
            entry.Station = station;
            entry.LastHeard = clock.Now;
            Reconcile();
        }

        static double Degrees(double value, double limit) => double.IsFinite(value) ? Math.Clamp(value, -limit, limit) : 0.0;

        /// <summary>A name goes into a colon-separated line: control characters and colons become spaces.</summary>
        static string Clean(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            var clean = new StringBuilder(text.Length);
            foreach (char c in text) clean.Append(c < ' ' || c == 0x7f || c == ':' ? ' ' : c);
            return clean.ToString();
        }

        public void Handle(in MessageMeta meta, in AtcRelay message)
        {
            NodeId from = meta.Sender;
            string line = message.Line ?? "";
            if (from == session.LocalId || !members.Contains(from))
            {
                Drop(AtcRelayDrop.NotAMember, "from " + from, line);
                return;
            }
            if (Refusal(line) is AtcRelayDrop refused)
            {
                Drop(refused, "from " + from, line);
                return;
            }
            // a node speaks only for its own station (§4.4)
            FsdLine.TryParse(line, out FsdLine parsed);
            if (!AnnouncedBy(from, parsed.From))
            {
                Drop(AtcRelayDrop.UnknownSender, "from " + from + " as " + parsed.From, line);
                return;
            }
            if (local[0] == null && local[1] == null)
            {
                Drop(AtcRelayDrop.NoLocalStation, "from " + from, line);
                return;
            }
            RelayedIn++;
            LineReceived?.Invoke(line);
        }

        bool AnnouncedBy(NodeId node, string callsign)
        {
            for (int slot = 0; slot < Slots; slot++)
            {
                if (table.TryGetValue((node, slot), out Entry entry) && entry.Station.Callsign == callsign) return true;
            }
            return false;
        }

        // ------------------------------------------------------------------ outgoing lines of the client

        public AtcRelayResult Relay(string line, string toCallsign)
        {
            if (!published.TryGetValue(toCallsign, out AtcRemoteStation station)) return AtcRelayResult.NoSuchStation;
            return Send(line, station.Node) ? AtcRelayResult.Sent : AtcRelayResult.Refused;
        }

        public void RelayToAll(string line)
        {
            var nodes = new HashSet<NodeId>();
            foreach (AtcRemoteStation station in published.Values) nodes.Add(station.Node);
            if (nodes.Count == 0) return;
            // a line that may not be sent is counted once, not once per node
            if (Refusal(line) is AtcRelayDrop refused)
            {
                Drop(refused, "to all stations", line);
                return;
            }
            foreach (NodeId node in nodes) Send(line, node);
        }

        /// <summary>One guaranteed unicast to <paramref name="node"/>, unless the line may not be sent.</summary>
        bool Send(string line, NodeId node)
        {
            if (Refusal(line) is AtcRelayDrop refused)
            {
                Drop(refused, "to " + node, line);
                return false;
            }
            outbox.SendTo(node, new AtcRelay { Line = line }, guaranteed: true);
            RelayedOut++;
            return true;
        }

        /// <summary>The checks on a line that need nothing but the line (§4.3, §4.4); null when it may cross the mesh.</summary>
        static AtcRelayDrop? Refusal(string line)
        {
            if (!string.IsNullOrEmpty(line) && Encoding.UTF8.GetByteCount(line) > FsdRelayPolicy.MaxLineBytes) return AtcRelayDrop.TooLong;
            switch (FsdRelayPolicy.Check(line))
            {
                case FsdRelayRefusal.None: return null;
                case FsdRelayRefusal.ControlCharacter: return AtcRelayDrop.ControlCharacter;
                case FsdRelayRefusal.VerbNotRelayed: return AtcRelayDrop.VerbNotRelayed;
                case FsdRelayRefusal.Landline: return AtcRelayDrop.Landline;
                default: return AtcRelayDrop.Malformed;
            }
        }

        void Drop(AtcRelayDrop reason, string where, string line)
        {
            dropped[(int)reason]++;
            var head = new StringBuilder();
            foreach (char c in line.Length > 40 ? line.Substring(0, 40) + "..." : line) head.Append(c < ' ' || c == 0x7f ? '?' : c);
            // an over-long line is a loss the controller notices (R6): always shown. The others are noise unless the monitor is on.
            string text = "ATC line dropped (" + reason + ") " + where + ": " + head;
            if (reason == AtcRelayDrop.TooLong) log.Event(text);
            else log.Network(text);
        }

        /// <summary>The counters as one line for the monitor, empty when nothing was relayed or dropped.</summary>
        public string CounterText()
        {
            var text = new StringBuilder();
            if (RelayedOut > 0 || RelayedIn > 0) text.Append("sent " + RelayedOut + ", received " + RelayedIn);
            foreach (AtcRelayDrop reason in Enum.GetValues<AtcRelayDrop>())
            {
                if (dropped[(int)reason] == 0) continue;
                text.Append(text.Length == 0 ? "dropped " : ", dropped ").Append(reason).Append(' ').Append(dropped[(int)reason]);
            }
            return text.Length == 0 ? "" : "ATC lines: " + text;
        }

        // ------------------------------------------------------------------ work

        /// <summary>Expire silent stations, repeat our own, and deliver the events the changes since the last call raised. Every app tick.</summary>
        public void DoWork()
        {
            double now = clock.Now;

            List<(NodeId, int)> silent = null;
            foreach (var pair in table)
            {
                if (now - pair.Value.LastHeard > ExpiryS) (silent ??= new()).Add(pair.Key);
            }
            if (silent != null)
            {
                foreach (var key in silent) table.Remove(key);
                Reconcile();
            }

            if (now >= nextSend)
            {
                nextSend = now + SendIntervalS;
                for (int slot = 0; slot < Slots; slot++)
                {
                    if (local[slot] != null) Broadcast(Describe(slot, local[slot], true));
                }
            }

            if (now >= nextCounterLog)
            {
                nextCounterLog = now + CounterIntervalS;
                string counters = CounterText();
                if (counters != lastCounterText)
                {
                    lastCounterText = counters;
                    log.Network(counters);
                }
            }

            if (pending.Count > 0)
            {
                Action[] events = pending.ToArray();
                pending.Clear();
                foreach (Action raise in events) raise();
            }
        }

        // ------------------------------------------------------------------ the rule of §4.6

        /// <summary>
        /// One station per callsign: when nodes claim the same one, the lowest node id keeps it. A station of this node that
        /// loses is dropped (and announced as logged off); one that wins hides the other node's, which gives way when it hears us.
        /// Works out what the gateway has to be told and queues it for <see cref="DoWork"/>.
        /// </summary>
        void Reconcile()
        {
            var winners = new Dictionary<string, AtcRemoteStation>();
            foreach (Entry entry in table.Values)
            {
                AtcRemoteStation station = entry.Station;
                if (winners.TryGetValue(station.Callsign, out AtcRemoteStation best)
                    && (Bits(best.Node), best.Slot).CompareTo((Bits(station.Node), station.Slot)) <= 0) continue;
                winners[station.Callsign] = station;
            }

            ulong me = Bits(session.LocalId);
            for (int slot = 0; slot < Slots; slot++)
            {
                AtcStationInfo ours = local[slot];
                if (ours == null || !winners.TryGetValue(ours.Callsign, out AtcRemoteStation other)) continue;
                if (Bits(other.Node) < me)
                {
                    log.Event("ATC station " + ours.Callsign + " is also logged in on node " + other.Node + ", whose id is lower: that node keeps the callsign");
                    local[slot] = null;
                    Broadcast(Describe(slot, ours, false));
                    int lostSlot = slot;
                    string callsign = ours.Callsign;
                    pending.Add(() => LocalStationLost?.Invoke(lostSlot, callsign));
                }
                else
                {
                    winners.Remove(ours.Callsign);
                }
            }

            foreach (var pair in winners)
            {
                if (!published.TryGetValue(pair.Key, out AtcRemoteStation old) || old != pair.Value)
                {
                    AtcRemoteStation station = pair.Value;
                    published[pair.Key] = station;
                    // the callsign went to another node (its first holder left): a different station, not a change of the old one
                    if (old != null && (old.Node != station.Node || old.Slot != station.Slot))
                    {
                        pending.Add(() => StationGone?.Invoke(old));
                    }
                    pending.Add(() => StationChanged?.Invoke(station));
                }
            }
            List<string> gone = null;
            foreach (string callsign in published.Keys)
            {
                if (!winners.ContainsKey(callsign)) (gone ??= new()).Add(callsign);
            }
            if (gone != null)
            {
                foreach (string callsign in gone)
                {
                    AtcRemoteStation station = published[callsign];
                    published.Remove(callsign);
                    pending.Add(() => StationGone?.Invoke(station));
                }
            }
        }

        static ulong Bits(NodeId id)
        {
            Span<byte> bytes = stackalloc byte[NodeId.WireSize];
            id.WriteTo(bytes);
            return BinaryPrimitives.ReadUInt64LittleEndian(bytes);
        }
    }
}
