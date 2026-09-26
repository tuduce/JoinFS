using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Net;
using JoinFS.Net.Jfp2;
using JoinFS.Net.Jfp2.Codecs;

namespace JoinFS.Net.Jfp2
{
    /// <summary>
    /// The JFP2 protocol as a plugin (docs/reference/jfp2-protocol.md): an 8-byte envelope starting with
    /// magic 0xFA, a Hello/HelloAck handshake that agrees a schema version per message class with
    /// each neighbor, versioned codecs, single-datagram guaranteed delivery, and relay of Forwarded
    /// envelopes.
    ///
    /// JFP2 is a per-hop "link upgrader" (docs/network-plugin-architecture.md §2.4). A session is
    /// with a NEIGHBOR: a node we exchange datagrams with directly, identified by the node id it
    /// states in the handshake and never by the endpoint it answers from (two nodes can share one
    /// public endpoint). A peer that is not a neighbor is reached through the neighbor that carries
    /// its traffic (<see cref="NextHop"/>): the datagram goes to that hop in the hop's negotiated
    /// schema, addressed end to end with Forwarded Origin/Target, and the hop either forwards it
    /// byte for byte (the target agreed the same schema) or decodes it and lets the core re-send it in
    /// the target's own terms (the generic translation path, design §2.6). Whatever a peer's hop
    /// cannot carry goes through the legacy plugin.
    ///
    /// Sessions stay honest with a keepalive: a Hello every few seconds to the verified endpoint,
    /// whose answer must again name the same node. A session that stops answering, or is answered by
    /// someone else (the network now steers the endpoint elsewhere), falls back to legacy until it is
    /// verified again.
    ///
    /// Ported from the JFP2 regions of Node.cs and Network.cs.
    /// </summary>
    public sealed class Jfp2Plugin : IProtocolPlugin, IDescribesLinks
    {
        const int HelloMaxAttempts = 5;
        const double HelloRetryInterval = 2.0;
        const double HelloCooldown = 30.0;
        const double KeepAliveInterval = 5.0;
        const double SessionTimeout = 15.0;
        const double OccupantTtl = 30.0;
        const double GuaranteedRetryInterval = 2.0;
        const int GuaranteedMaxAttempts = 5;
        const double GuaranteedDedupWindow = 30.0;
        const double IdentityHeartbeatInterval = 4.0;
        const int VariableSyncChunkSize = 200;
        const ulong LocalCapabilities = (ulong)Capability.None;

        static readonly List<SchemaOffer> LocalOffers =
        [
            new SchemaOffer(false, MessageClasses.Status, 1, 1),
            new SchemaOffer(false, MessageClasses.StatusRequest, 1, 1),
            new SchemaOffer(false, MessageClasses.Identity, 1, 1),
            new SchemaOffer(false, MessageClasses.VariableSync, 1, 1),
            new SchemaOffer(false, MessageClasses.Position, 1, 1),
            new SchemaOffer(false, MessageClasses.Event, 1, 1),
            new SchemaOffer(false, MessageClasses.FlightPlan, 1, 1),
            new SchemaOffer(false, MessageClasses.Notes, 1, 1),
            new SchemaOffer(false, MessageClasses.Weather, 1, 1),
            new SchemaOffer(false, MessageClasses.WeatherReply, 1, 1),
        ];

        static Jfp2Plugin()
        {
            CodecRegistry.Register(new StatusV1Codec());
            CodecRegistry.Register(new StatusRequestV1Codec());
            CodecRegistry.Register(new IdentityV1Codec());
            CodecRegistry.Register(new VariableSyncV1Codec());
            CodecRegistry.Register(new PositionV1Codec());
            CodecRegistry.Register(new EventV1Codec());
            CodecRegistry.Register(new FlightPlanV1Codec());
            CodecRegistry.Register(new NotesV1Codec());
            CodecRegistry.Register(new WeatherUpdateV1Codec());
            CodecRegistry.Register(new WeatherReplyV1Codec());
        }

        /// <summary>Which JFP2 application class carries a canonical kind (-1: not carried by JFP2).</summary>
        static int ClassFor(MessageKind kind) => kind switch
        {
            MessageKind.Position => MessageClasses.Position,
            MessageKind.Identity => MessageClasses.Identity,
            MessageKind.VariableSync => MessageClasses.VariableSync,
            MessageKind.Event => MessageClasses.Event,
            MessageKind.FlightPlan => MessageClasses.FlightPlan,
            MessageKind.Notes => MessageClasses.Notes,
            MessageKind.WeatherUpdate => MessageClasses.Weather,
            MessageKind.WeatherReply => MessageClasses.WeatherReply,
            MessageKind.Status => MessageClasses.Status,
            MessageKind.StatusRequest => MessageClasses.StatusRequest,
            _ => -1,
        };

        sealed class Pending
        {
            public IPEndPoint EndPoint;
            public EnvelopeFlags Flags;
            public byte MessageClass;
            public ushort SenderPeerId;
            public ushort RecipientPeerId;
            public RelayNuid Origin;
            public RelayNuid Target;
            /// <summary>The neighbor the datagram went to (differs from the key's peer when relayed).</summary>
            public NodeId Hop;
            public byte[] Payload;
            public int Attempts;
            public double NextRetry;
        }

        /// <summary>A node seen answering at an endpoint, so other nodes claiming that endpoint are known to be behind it.</summary>
        readonly record struct Occupant(NodeId Node, double Expire);

        sealed class IdentitySent
        {
            public IdentityUpdate Last;
            public double Time;
        }

        IProtocolHost host;
        readonly Encoder encoder;
        readonly Dictionary<NodeId, PeerSession> sessions = [];
        readonly Dictionary<ushort, PeerSession> sessionsById = [];
        readonly Dictionary<IPEndPoint, Occupant> occupants = [];
        readonly Random random = new();
        readonly Dictionary<(NodeId Peer, ushort Id), Pending> pending = [];
        readonly Dictionary<(NodeId Peer, ushort Id), double> recentlySeen = [];
        readonly Dictionary<(uint ObjectId, NodeId Peer), IdentitySent> identitySent = [];
        readonly List<NodeId> targets = [];
        readonly List<(NodeId, ushort)> scratchKeys = [];
        readonly byte[] payloadBuffer = new byte[16384];
        readonly byte[] datagramBuffer = new byte[16384 + 64];
        ushort nextGuaranteedId = 1;
        /// <summary>Who a send is on behalf of: this node, or (translation at a relay) the message's author.</summary>
        NodeId sendOrigin;
        double nextDedupSweep;

        public Jfp2Plugin()
        {
            encoder = new Encoder(this);
        }

        public string Name => "JFP2";
        public int Preference => 10;

        public void Attach(IProtocolHost host) => this.host = host;

        public bool Accepts(ReadOnlySpan<byte> datagram) => datagram[0] == Envelope.Magic;

        NodeId Local => host.Identity.Id;
        double Now => host.Clock.Now;

        static NodeId ToNodeId(RelayNuid r) => new(r.Ip, r.Port, r.Local);
        static RelayNuid ToRelay(NodeId n) => new(n.ip, n.port, n.local);

        // ================================================================== sessions and next hop

        PeerSession CreateSession(NodeId peer)
        {
            ushort id;
            do
            {
                // random, not sequential: every node counts from 1, so a datagram meant for another
                // node would otherwise match a session here by coincidence
                id = (ushort)random.Next(1, 65536);
            }
            while (sessionsById.ContainsKey(id));
            var session = new PeerSession { Peer = peer, LocalAssignedId = id };
            sessions[peer] = session;
            sessionsById[id] = session;
            return session;
        }

        void RemoveSession(NodeId peer)
        {
            if (sessions.Remove(peer, out PeerSession session))
            {
                sessionsById.Remove(session.LocalAssignedId);
            }
        }

        /// <summary>The session can carry datagrams to <paramref name="route"/>: its peer answered there, and the route has not moved.</summary>
        static bool IsUsable(PeerSession session, IPEndPoint route) => session.Verified && session.HandshakeComplete && session.Endpoint.Equals(route);

        /// <summary>
        /// The session through which datagrams for <paramref name="target"/> go, or null when JFP2 has
        /// no way to reach it (then the legacy plugin carries everything for it).
        /// </summary>
        PeerSession NextHop(NodeId target, out Peer targetPeer)
        {
            if (!host.Peers.TryGet(target, out targetPeer))
            {
                return null;
            }
            IPEndPoint route = targetPeer.RouteEndPoint;
            // the peer itself answered at the endpoint we currently reach it on
            if (sessions.TryGetValue(target, out PeerSession session) && IsUsable(session, route))
            {
                return session;
            }
            // the relay the mesh routes it through
            if (targetPeer.Relayed && host.Peers.TryGet(targetPeer.RouteVia, out Peer via)
                && sessions.TryGetValue(via.Id, out session) && IsUsable(session, via.RouteEndPoint))
            {
                return session;
            }
            // some other node answered at this very endpoint: the target shares it and is behind that node
            foreach (PeerSession other in sessions.Values)
            {
                if (other.Peer != target && IsUsable(other, route))
                {
                    return other;
                }
            }
            return null;
        }

        byte AgreedVersion(NodeId target, int messageClass, out Peer peer, out PeerSession hop)
        {
            peer = null;
            hop = null;
            if (messageClass < 0)
            {
                return 0;
            }
            hop = NextHop(target, out peer);
            return hop == null ? (byte)0 : hop.AgreedAppVersion[messageClass];
        }

        public bool CanCarry(NodeId peer, MessageKind kind) => peer.Valid() && AgreedVersion(peer, ClassFor(kind), out _, out _) > 0;

        static IPEndPoint Copy(IPEndPoint endPoint) => new(endPoint.Address, endPoint.Port);

        ushort NextGuaranteedId()
        {
            ushort id = nextGuaranteedId++;
            if (nextGuaranteedId == 0) nextGuaranteedId = 1;
            return id;
        }

        public PeerLinkState? DescribeLink(Peer peer)
        {
            if (NextHop(peer.Id, out _) != null) return PeerLinkState.Negotiated;
            if (sessions.TryGetValue(peer.Id, out PeerSession session) && session.AssumedLegacy) return PeerLinkState.Legacy;
            if (peer.Relayed && sessions.TryGetValue(peer.RouteVia, out session) && session.AssumedLegacy) return PeerLinkState.Legacy;
            return PeerLinkState.Negotiating;
        }

        /// <summary>For tests and diagnostics: some hop carries JFP2 to the peer.</summary>
        public bool IsNegotiated(NodeId peer) => NextHop(peer, out _) != null;

        /// <summary>For tests and diagnostics: the neighbor JFP2 datagrams for the peer go to (invalid when JFP2 cannot reach it).</summary>
        public NodeId NextHopNode(NodeId peer) => NextHop(peer, out _)?.Peer ?? default;

        /// <summary>For tests and diagnostics: the ids of the session with a neighbor.</summary>
        public bool TryGetHopIds(NodeId neighbor, out ushort local, out ushort remote)
        {
            if (sessions.TryGetValue(neighbor, out PeerSession session))
            {
                local = session.LocalAssignedId;
                remote = session.RemoteAssignedId;
                return session.HandshakeComplete;
            }
            local = remote = 0;
            return false;
        }

        // ================================================================== periodic

        public void Tick()
        {
            DoHandshake();
            DoGuaranteedRetry();
        }

        void DoHandshake()
        {
            double now = Now;
            foreach (Peer peer in host.Peers.All)
            {
                sessions.TryGetValue(peer.Id, out PeerSession session);
                if (session != null && session.Verified)
                {
                    KeepAlive(peer, session, now);
                    continue;
                }
                // negotiation is with a neighbor: a peer behind a relay is served by the relay's session,
                // and a peer sharing an endpoint with a node that answers there is behind that node
                if (peer.Relayed || IsBehindOccupant(peer, now))
                {
                    continue;
                }
                // one Hello at a time per endpoint: each Hello tells the node that answers which id to
                // address us by, so two in flight to one endpoint would leave it holding the wrong one
                if (now >= (session?.NextHelloAttempt ?? 0) && EndPointBusy(peer))
                {
                    continue;
                }
                session ??= CreateSession(peer.Id);
                if (session.AssumedLegacy && now < session.RetryAt)
                {
                    continue;
                }
                if (now < session.NextHelloAttempt)
                {
                    continue;
                }
                if (session.HelloAttempts >= HelloMaxAttempts)
                {
                    bool first = !session.AssumedLegacy;
                    session.AssumedLegacy = true;
                    session.RetryAt = now + HelloCooldown;
                    session.HelloAttempts = 0;
                    host.Log(NetLogLevel.Network, "JFP2: " + peer.Id + " did not answer Hello after " + HelloMaxAttempts + " attempts - assuming legacy-only peer" + (first ? "" : " (again)"));
                    continue;
                }
                session.HelloAttempts++;
                session.NextHelloAttempt = now + HelloRetryInterval;
                session.ProbeEndPoint = Copy(peer.RouteEndPoint);
                SendHello(session.ProbeEndPoint, session);
                host.Log(NetLogLevel.Network, "JFP2: Send Hello to " + peer.Id + " at " + session.ProbeEndPoint + " (attempt " + session.HelloAttempts + ")");
            }
        }

        /// <summary>Another peer reached at the same endpoint has a Hello outstanding there.</summary>
        bool EndPointBusy(Peer peer)
        {
            foreach (Peer other in host.Peers.All)
            {
                if (other != peer && other.RouteEndPoint.Equals(peer.RouteEndPoint)
                    && sessions.TryGetValue(other.Id, out PeerSession session)
                    && !session.Verified && !session.AssumedLegacy && session.ProbeEndPoint != null && session.HelloAttempts > 0)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>A verified session must keep being answered, by the same node, at the same endpoint.</summary>
        void KeepAlive(Peer peer, PeerSession session, double now)
        {
            if (!session.Endpoint.Equals(peer.RouteEndPoint))
            {
                Demote(session, "its route moved to " + peer.RouteEndPoint);
            }
            else if (now > session.LastAck + SessionTimeout)
            {
                Demote(session, "no answer for " + SessionTimeout + " s");
            }
            else if (now >= session.NextKeepAlive)
            {
                session.NextKeepAlive = now + KeepAliveInterval;
                session.ProbeEndPoint = session.Endpoint;
                SendHello(session.Endpoint, session);
            }
        }

        /// <summary>The session no longer proves that our datagrams reach the peer: send it through legacy until verified again.</summary>
        void Demote(PeerSession session, string reason)
        {
            host.Log(NetLogLevel.Event, "JFP2: session with " + session.Peer + " is no longer verified: " + reason);
            if (occupants.TryGetValue(session.Endpoint, out Occupant occupant) && occupant.Node == session.Peer)
            {
                occupants.Remove(session.Endpoint);
            }
            session.Endpoint = null;
            session.ProbeEndPoint = null;
            session.HelloAttempts = 0;
            session.NextHelloAttempt = 0;
            host.LinkChanged(session.Peer);
        }

        /// <summary>Another node answers at this peer's endpoint, so the peer is not there.</summary>
        bool IsBehindOccupant(Peer peer, double now)
        {
            IPEndPoint route = peer.RouteEndPoint;
            if (!occupants.TryGetValue(route, out Occupant occupant))
            {
                return false;
            }
            if (now > occupant.Expire)
            {
                occupants.Remove(route);
                return false;
            }
            return occupant.Node != peer.Id;
        }

        void DoGuaranteedRetry()
        {
            double now = Now;
            if (pending.Count > 0)
            {
                scratchKeys.Clear();
                foreach (var kv in pending)
                {
                    Pending p = kv.Value;
                    if (now <= p.NextRetry) continue;
                    if (p.Attempts >= GuaranteedMaxAttempts)
                    {
                        host.Log(NetLogLevel.Event, "JFP2: giving up on guaranteed message class " + p.MessageClass + " to " + kv.Key.Peer + " after " + p.Attempts + " attempts");
                        scratchKeys.Add(kv.Key);
                        continue;
                    }
                    p.Attempts++;
                    p.NextRetry = now + GuaranteedRetryInterval;
                    SendDatagram(p.EndPoint, p.Flags, p.MessageClass, p.SenderPeerId, p.RecipientPeerId, p.Payload, kv.Key.Id, 0, 1, p.Origin, p.Target);
                }
                foreach (var key in scratchKeys) pending.Remove(key);
            }
            if (now >= nextDedupSweep && recentlySeen.Count > 0)
            {
                nextDedupSweep = now + GuaranteedDedupWindow;
                scratchKeys.Clear();
                foreach (var kv in recentlySeen)
                {
                    if (now - kv.Value >= GuaranteedDedupWindow) scratchKeys.Add(kv.Key);
                }
                foreach (var key in scratchKeys) recentlySeen.Remove(key);
            }
        }

        public void OnPeerRemoved(Peer peer)
        {
            RemoveSession(peer.Id);
            RemoveWhere(pending, (k, p) => k.Peer == peer.Id || p.Hop == peer.Id);
            RemoveWhere(recentlySeen, (k, _) => k.Peer == peer.Id);
            RemoveWhere(identitySent, (k, _) => k.Peer == peer.Id);
            RemoveWhere(occupants, (_, o) => o.Node == peer.Id);
        }

        static void RemoveWhere<TKey, TValue>(Dictionary<TKey, TValue> map, Func<TKey, TValue, bool> predicate)
        {
            List<TKey> doomed = null;
            foreach (var kv in map)
            {
                if (predicate(kv.Key, kv.Value)) (doomed ??= []).Add(kv.Key);
            }
            if (doomed != null) foreach (TKey key in doomed) map.Remove(key);
        }

        public void OnSessionReset()
        {
            sessions.Clear();
            sessionsById.Clear();
            occupants.Clear();
            pending.Clear();
            recentlySeen.Clear();
            identitySent.Clear();
        }

        // ================================================================== datagrams out

        void SendDatagram(IPEndPoint endPoint, EnvelopeFlags flags, byte messageClass, ushort senderPeerId, ushort recipientPeerId, ReadOnlySpan<byte> payload,
            ushort guaranteedId = 0, byte guaranteedIndex = 0, byte guaranteedCount = 0, RelayNuid origin = default, RelayNuid target = default)
        {
            if (endPoint == null) return;
            var envelope = new Envelope(flags, senderPeerId, recipientPeerId, messageClass, guaranteedId, guaranteedIndex, guaranteedCount, origin, target);
            int header = envelope.WriteTo(datagramBuffer);
            payload.CopyTo(datagramBuffer.AsSpan(header));
            host.Transport.Send(endPoint, datagramBuffer.AsSpan(0, header + payload.Length));
        }

        HandshakeMessage MakeHandshake(PeerSession session, byte result) => new()
        {
            ProtoMajorMin = Envelope.ProtoMajor,
            ProtoMajorMax = Envelope.ProtoMajor,
            Capabilities = LocalCapabilities,
            SelfAssignedId = session.LocalAssignedId,
            Result = result,
            Node = ToRelay(Local),
            Offers = new List<SchemaOffer>(LocalOffers),
        };

        void SendHello(IPEndPoint endPoint, PeerSession session) =>
            SendDatagram(endPoint, EnvelopeFlags.Internal, MessageClasses.Hello, session.LocalAssignedId, session.RemoteAssignedId, MakeHandshake(session, 0).Serialize());

        void SendHelloAck(IPEndPoint endPoint, PeerSession session, byte result) =>
            SendDatagram(endPoint, EnvelopeFlags.Internal, MessageClasses.HelloAck, session.LocalAssignedId, session.RemoteAssignedId, MakeHandshake(session, result).Serialize());

        /// <summary>
        /// Send one application message toward <paramref name="target"/> through <paramref name="hop"/>,
        /// reliably if asked: unaddressed when the hop is the target itself, otherwise (or when relaying
        /// for the message's author) Forwarded with Origin and Target, to be passed on or translated.
        /// </summary>
        void SendApplication(Peer target, PeerSession hop, byte messageClass, ReadOnlySpan<byte> payload, bool guaranteed)
        {
            bool forwarded = hop.Peer != target.Id || sendOrigin != Local;
            EnvelopeFlags flags = forwarded ? EnvelopeFlags.Forwarded : EnvelopeFlags.None;
            RelayNuid origin = forwarded ? ToRelay(sendOrigin) : default;
            RelayNuid destination = forwarded ? ToRelay(target.Id) : default;
            if (!guaranteed)
            {
                SendDatagram(hop.Endpoint, flags, messageClass, hop.LocalAssignedId, hop.RemoteAssignedId, payload, origin: origin, target: destination);
                return;
            }
            ushort id = NextGuaranteedId();
            flags |= EnvelopeFlags.Guaranteed;
            SendDatagram(hop.Endpoint, flags, messageClass, hop.LocalAssignedId, hop.RemoteAssignedId, payload, id, 0, 1, origin, destination);
            pending[(target.Id, id)] = new Pending
            {
                EndPoint = hop.Endpoint,
                Flags = flags,
                MessageClass = messageClass,
                SenderPeerId = hop.LocalAssignedId,
                RecipientPeerId = hop.RemoteAssignedId,
                Origin = origin,
                Target = destination,
                Hop = hop.Peer,
                Payload = payload.ToArray(),
                Attempts = 1,
                NextRetry = Now + GuaranteedRetryInterval,
            };
        }

        /// <summary>
        /// Acknowledge a guaranteed datagram to the neighbor it came from. When it was relayed to us the
        /// ack is addressed to the true sender and travels back through that neighbor.
        /// </summary>
        void SendGuaranteedDone(IPEndPoint endPoint, PeerSession hop, ushort guaranteedId, NodeId? relayTrueOrigin)
        {
            Span<byte> payload = stackalloc byte[2];
            BinaryPrimitives.WriteUInt16LittleEndian(payload, guaranteedId);
            if (relayTrueOrigin.HasValue)
            {
                SendDatagram(endPoint, EnvelopeFlags.Internal | EnvelopeFlags.Forwarded, MessageClasses.GuaranteedDone, hop.LocalAssignedId, hop.RemoteAssignedId, payload,
                    origin: ToRelay(Local), target: ToRelay(relayTrueOrigin.Value));
            }
            else
            {
                SendDatagram(endPoint, EnvelopeFlags.Internal, MessageClasses.GuaranteedDone, hop.LocalAssignedId, hop.RemoteAssignedId, payload);
            }
        }

        // ================================================================== canonical → wire

        public void Send<T>(in MessageMeta meta, in T message, ReadOnlySpan<NodeId> recipients) where T : struct, IMessage
        {
            targets.Clear();
            sendOrigin = meta.Sender.Valid() ? meta.Sender : Local;
            if (meta.EndPoint != null)
            {
                NodeId known = meta.Recipient.Valid() ? meta.Recipient : host.Peers.FindByEndPoint(meta.EndPoint)?.Id ?? default;
                if (known.Valid()) targets.Add(known);
            }
            else
            {
                foreach (NodeId recipient in recipients) targets.Add(recipient);
            }
            message.Dispatch(encoder, meta);
        }

        /// <summary>
        /// Make sure <paramref name="peer"/> has this object's current identity before a position:
        /// sent the first time, whenever it changes, and every few seconds as a heartbeat (a peer that
        /// joins late or lost a datagram converges). False when no identity is known yet.
        /// </summary>
        bool EnsureIdentity(NodeId owner, uint objectId, Peer peer, PeerSession session)
        {
            if (objectId == uint.MaxValue)
            {
                return true; // shared cockpit: the recipient's own aircraft, no identity involved
            }
            if (!host.Objects.TryGetIdentity(owner, objectId, out IdentityUpdate identity))
            {
                return false;
            }
            byte version = session.AgreedAppVersion[MessageClasses.Identity];
            if (version == 0)
            {
                return true;
            }
            var key = (objectId, peer.Id);
            double now = Now;
            if (identitySent.TryGetValue(key, out IdentitySent sent) && sent.Last.SameAs(identity) && now - sent.Time < IdentityHeartbeatInterval)
            {
                return true;
            }
            int length = CodecRegistry.Resolve<IdentityUpdate>(MessageClasses.Identity, version).Encode(identity, payloadBuffer);
            SendApplication(peer, session, MessageClasses.Identity, payloadBuffer.AsSpan(0, length), false);
            identitySent[key] = new IdentitySent { Last = identity, Time = now };
            return true;
        }

        /// <summary>Canonical to JFP2, one overload per carried type; each loops the targets because versions are per peer.</summary>
        sealed class Encoder(Jfp2Plugin p) : IMessageHandler
        {
            void SendSimple<T>(byte messageClass, in T message, bool guaranteed)
            {
                foreach (NodeId target in p.targets)
                {
                    byte version = p.AgreedVersion(target, messageClass, out Peer peer, out PeerSession session);
                    if (version == 0) continue;
                    int length = CodecRegistry.Resolve<T>(messageClass, version).Encode(message, p.payloadBuffer);
                    p.SendApplication(peer, session, messageClass, p.payloadBuffer.AsSpan(0, length), guaranteed);
                }
            }

            public void Handle(in MessageMeta meta, in PositionUpdate m)
            {
                foreach (NodeId target in p.targets)
                {
                    byte version = p.AgreedVersion(target, MessageClasses.Position, out Peer peer, out PeerSession session);
                    if (version == 0 || !p.EnsureIdentity(meta.Sender, m.ObjectId, peer, session)) continue;
                    int length = CodecRegistry.Resolve<PositionUpdate>(MessageClasses.Position, version).Encode(m, p.payloadBuffer);
                    p.SendApplication(peer, session, MessageClasses.Position, p.payloadBuffer.AsSpan(0, length), false);
                }
            }

            public void Handle(in MessageMeta meta, in VariableSyncUpdate m)
            {
                if (m.Entries == null || m.Entries.Count == 0) return;
                foreach (NodeId target in p.targets)
                {
                    byte version = p.AgreedVersion(target, MessageClasses.VariableSync, out Peer peer, out PeerSession session);
                    if (version == 0) continue;
                    ICodec<VariableSyncUpdate> codec = CodecRegistry.Resolve<VariableSyncUpdate>(MessageClasses.VariableSync, version);
                    for (int offset = 0; offset < m.Entries.Count; offset += VariableSyncChunkSize)
                    {
                        int count = Math.Min(VariableSyncChunkSize, m.Entries.Count - offset);
                        var chunk = new VariableSyncUpdate { ObjectId = m.ObjectId, Entries = m.Entries.GetRange(offset, count) };
                        int length = codec.Encode(chunk, p.payloadBuffer);
                        p.SendApplication(peer, session, MessageClasses.VariableSync, p.payloadBuffer.AsSpan(0, length), false);
                    }
                }
            }

            public void Handle(in MessageMeta meta, in EventUpdate m) => SendSimple(MessageClasses.Event, m, true);
            public void Handle(in MessageMeta meta, in FlightPlanUpdate m) => SendSimple(MessageClasses.FlightPlan, m, false);
            public void Handle(in MessageMeta meta, in WeatherUpdate m) => SendSimple(MessageClasses.Weather, m, false);
            public void Handle(in MessageMeta meta, in WeatherReply m) => SendSimple(MessageClasses.WeatherReply, m, true);
            public void Handle(in MessageMeta meta, in StatusUpdate m) => SendSimple(MessageClasses.Status, m, false);
            public void Handle(in MessageMeta meta, in StatusRequestUpdate m) => SendSimple(MessageClasses.StatusRequest, m, false);

            public void Handle(in MessageMeta meta, in NotesBundle m)
            {
                // JFP2 carries one note per message: a bundle goes out as its notes
                foreach (NotesUser user in m.Users)
                {
                    foreach (CommsNote note in user.Notes)
                    {
                        var single = new NoteUpdate
                        {
                            Guid = user.Guid, Nickname = user.Nickname, Callsign = user.Callsign,
                            NoteId = note.NoteId, Age = note.Age, Channel = note.Channel, Text = note.Text,
                        };
                        SendSimple(MessageClasses.Notes, single, true);
                    }
                }
            }
        }

        // ================================================================== wire → canonical

        public void OnDatagram(IPEndPoint from, ReadOnlySpan<byte> datagram)
        {
            try
            {
                Receive(from, datagram);
            }
            catch (Exception ex)
            {
                host.Log(NetLogLevel.Event, "ERROR: Failed to read JFP2 message: " + ex.Message);
            }
        }

        void Receive(IPEndPoint from, ReadOnlySpan<byte> datagram)
        {
            Envelope envelope = Envelope.ReadFrom(datagram, out int consumed);
            ReadOnlySpan<byte> payload = datagram[consumed..];

            // the handshake is the only traffic that has no session yet; it says who is speaking itself
            if (envelope.IsInternal && envelope.RawMessageClass == MessageClasses.Hello)
            {
                HandleHello(from, payload);
                return;
            }
            if (envelope.IsInternal && envelope.RawMessageClass == MessageClasses.HelloAck)
            {
                HandleHelloAck(envelope, payload);
                return;
            }

            // everything else names our end of the hop's session, and the neighbor's end; the source
            // endpoint plays no part (several nodes can share one)
            if (!sessionsById.TryGetValue(envelope.RecipientPeerId, out PeerSession hop) || !hop.HandshakeComplete || hop.RemoteAssignedId != envelope.SenderPeerId)
            {
                host.Log(NetLogLevel.Network, "JFP2: datagram from " + from + " belongs to no session here (ids " + envelope.SenderPeerId + " -> " + envelope.RecipientPeerId + ") - ignored");
                return;
            }

            if (envelope.IsForwarded && ToNodeId(envelope.TargetNuid) != Local)
            {
                Relay(from, hop, envelope, datagram, payload);
                return;
            }

            if (envelope.IsGuaranteed && !AckGuaranteed(from, hop, envelope))
            {
                return; // duplicate of something already delivered
            }

            if (envelope.IsInternal)
            {
                if (envelope.RawMessageClass == MessageClasses.GuaranteedDone && payload.Length >= 2)
                {
                    ClearPending(hop, envelope, BinaryPrimitives.ReadUInt16LittleEndian(payload));
                }
                return;
            }

            byte version = hop.AgreedAppVersion[envelope.RawMessageClass];
            if (version == 0)
            {
                host.Log(NetLogLevel.Network, "JFP2: class " + envelope.RawMessageClass + " from " + hop.Peer + " was never agreed on - ignored");
                return;
            }
            NodeId sender = envelope.IsForwarded ? ToNodeId(envelope.OriginNuid) : hop.Peer;
            var meta = new MessageMeta
            {
                Sender = sender,
                Recipient = Local,
                EndPoint = host.Peers.TryGet(sender, out Peer senderPeer) && senderPeer.SendEstablished ? senderPeer.RouteEndPoint : from,
                Guaranteed = envelope.IsGuaranteed,
                Forwarded = envelope.IsForwarded,
            };
            Decode(meta, envelope.RawMessageClass, version, payload);
        }

        /// <summary>An ack arrived: from the node that received the message, or (relayed on our behalf, translated) from the neighbor that took it over.</summary>
        void ClearPending(PeerSession hop, in Envelope envelope, ushort guaranteedId)
        {
            NodeId acker = envelope.IsForwarded ? ToNodeId(envelope.OriginNuid) : hop.Peer;
            if (pending.Remove((acker, guaranteedId)))
            {
                return;
            }
            if (envelope.IsForwarded)
            {
                return;
            }
            scratchKeys.Clear();
            foreach (var kv in pending)
            {
                if (kv.Key.Id == guaranteedId && kv.Value.Hop == hop.Peer) scratchKeys.Add(kv.Key);
            }
            foreach (var key in scratchKeys) pending.Remove(key);
        }

        /// <summary>Ack a guaranteed datagram (always - a duplicate means our ack was lost); false if already delivered.</summary>
        bool AckGuaranteed(IPEndPoint from, PeerSession hop, in Envelope envelope)
        {
            NodeId dedup = envelope.IsForwarded ? ToNodeId(envelope.OriginNuid) : hop.Peer;
            var key = (dedup, envelope.GuaranteedId);
            bool duplicate = recentlySeen.ContainsKey(key);
            recentlySeen[key] = Now;
            SendGuaranteedDone(from, hop, envelope.GuaranteedId, envelope.IsForwarded ? dedup : null);
            return !duplicate;
        }

        void HandleHello(IPEndPoint from, ReadOnlySpan<byte> payload)
        {
            HandshakeMessage hello = HandshakeMessage.Deserialize(payload);
            if (!hello.Node.HasValue)
            {
                host.Log(NetLogLevel.Network, "JFP2: Hello from " + from + " does not say who it is - ignored (legacy-only peer)");
                return;
            }
            NodeId sender = ToNodeId(hello.Node.Value);
            // only nodes the mesh already knows (the legacy Join always happens first)
            if (!host.Peers.Contains(sender))
            {
                host.Log(NetLogLevel.Network, "JFP2: Hello from unknown node " + sender + " at " + from + " - ignored");
                return;
            }
            if (!sessions.TryGetValue(sender, out PeerSession session))
            {
                session = CreateSession(sender);
            }
            session.RemoteAssignedId = hello.SelfAssignedId;
            if (hello.ProtoMajorMin > Envelope.ProtoMajor || hello.ProtoMajorMax < Envelope.ProtoMajor)
            {
                SendHelloAck(from, session, result: 1);
                return;
            }
            Negotiator.Resolve(session, LocalCapabilities, LocalOffers, hello.Capabilities, hello.Offers);
            // we can decode what it sends; whether ours reaches it is for our own Hello to prove
            session.HandshakeComplete = true;
            if (session.AssumedLegacy)
            {
                session.RetryAt = 0; // it speaks JFP2 after all: try it now
            }
            SendHelloAck(from, session, result: 0);
            host.Log(NetLogLevel.Network, "JFP2: Hello from " + sender + " at " + from + " - answered");
        }

        void HandleHelloAck(in Envelope envelope, ReadOnlySpan<byte> payload)
        {
            HandshakeMessage ack = HandshakeMessage.Deserialize(payload);
            // addressed to the id we gave the peer the Hello was aimed at
            if (!sessionsById.TryGetValue(envelope.RecipientPeerId, out PeerSession session) || session.ProbeEndPoint == null)
            {
                return;
            }
            double now = Now;
            if (ack.Result != 0 || !ack.Node.HasValue)
            {
                session.AssumedLegacy = true;
                session.RetryAt = now + HelloCooldown;
                session.HelloAttempts = 0;
                host.Log(NetLogLevel.Network, "JFP2: " + session.Peer + " refused or cannot identify itself - assuming legacy-only peer");
                return;
            }
            NodeId responder = ToNodeId(ack.Node.Value);
            if (!host.Peers.Contains(responder))
            {
                host.Log(NetLogLevel.Network, "JFP2: HelloAck from unknown node " + responder + " - ignored");
                return;
            }
            IPEndPoint probe = session.ProbeEndPoint;
            occupants[probe] = new Occupant(responder, now + OccupantTtl);
            if (responder != session.Peer)
            {
                // the node at that endpoint is not the one we asked for: two nodes share the endpoint
                // (or the network now steers it elsewhere). The datagrams for our peer go through that
                // node, which negotiates for itself (see NextHop).
                host.Log(NetLogLevel.Network, "JFP2: " + session.Peer + " is not the node answering at " + probe + " - " + responder + " is");
                if (session.Verified)
                {
                    Demote(session, "its endpoint " + probe + " is now answered by " + responder);
                }
                session.ProbeEndPoint = null;
                session.HelloAttempts = 0;
                if (sessions.TryGetValue(responder, out PeerSession occupantSession))
                {
                    occupantSession.NextHelloAttempt = 0;
                    occupantSession.NextKeepAlive = 0;
                    occupantSession.RetryAt = 0;
                }
                host.LinkChanged(session.Peer);
                return;
            }
            session.RemoteAssignedId = ack.SelfAssignedId;
            Negotiator.Resolve(session, LocalCapabilities, LocalOffers, ack.Capabilities, ack.Offers);
            bool wasVerified = session.Verified;
            session.HandshakeComplete = true;
            session.Endpoint = probe;
            session.LastAck = now;
            session.NextKeepAlive = now + KeepAliveInterval;
            session.HelloAttempts = 0;
            session.AssumedLegacy = false;
            if (!wasVerified)
            {
                host.LinkChanged(session.Peer);
                host.Log(NetLogLevel.Network, "JFP2: HelloAck from " + session.Peer + " at " + probe + " - verified");
            }
        }

        /// <summary>
        /// A Forwarded envelope for another node. Only a direct neighbor of ours can be relayed to. If
        /// that neighbor agreed the same schema as the sender's hop used (or the datagram is internal,
        /// e.g. a relayed ack), pass the datagram on with the two hop ids rewritten. Otherwise - a
        /// legacy-only target, or a different schema version - decode it and let the core re-send it in
        /// the target's terms, the one place translation happens since every node speaks legacy.
        /// </summary>
        void Relay(IPEndPoint from, PeerSession hop, in Envelope envelope, ReadOnlySpan<byte> datagram, ReadOnlySpan<byte> payload)
        {
            NodeId target = ToNodeId(envelope.TargetNuid);
            NodeId origin = ToNodeId(envelope.OriginNuid);
            if (envelope.IsInternal && envelope.RawMessageClass == MessageClasses.GuaranteedDone && payload.Length >= 2
                && pending.Remove((origin, BinaryPrimitives.ReadUInt16LittleEndian(payload))))
            {
                return; // the ack of a message this node re-sent on the origin's behalf ends here
            }
            if (!host.Peers.TryGet(target, out Peer targetPeer) || !targetPeer.RouteIsOwnEndPoint)
            {
                host.Log(NetLogLevel.Network, "JFP2: relay target " + target + " is not a direct neighbor - dropped");
                return;
            }
            if (!host.TryAcquireRelay(origin))
            {
                host.Log(NetLogLevel.Network, "JFP2: relay capacity reached - dropped datagram from " + origin);
                return;
            }
            byte messageClass = envelope.RawMessageClass;
            byte version = envelope.IsInternal ? (byte)0 : hop.AgreedAppVersion[messageClass];
            if (sessions.TryGetValue(target, out PeerSession targetSession) && IsUsable(targetSession, targetPeer.RouteEndPoint)
                && (envelope.IsInternal || (version > 0 && targetSession.AgreedAppVersion[messageClass] == version)))
            {
                datagram.CopyTo(datagramBuffer);
                BinaryPrimitives.WriteUInt16LittleEndian(datagramBuffer.AsSpan(3, 2), targetSession.LocalAssignedId);
                BinaryPrimitives.WriteUInt16LittleEndian(datagramBuffer.AsSpan(5, 2), targetSession.RemoteAssignedId);
                host.Transport.Send(targetSession.Endpoint, datagramBuffer.AsSpan(0, datagram.Length));
                return;
            }
            if (envelope.IsInternal || version == 0)
            {
                host.Log(NetLogLevel.Network, "JFP2: relay from " + origin + " to " + target + " cannot be carried (class " + messageClass + ") - dropped");
                return;
            }
            if (envelope.IsGuaranteed)
            {
                // this hop is complete once we have it; the downstream protocol takes over delivery
                SendGuaranteedDone(from, hop, envelope.GuaranteedId, null);
            }
            var meta = new MessageMeta { Sender = origin, Recipient = target, Guaranteed = envelope.IsGuaranteed, Forwarded = true };
            Decode(meta, messageClass, version, payload);
        }

        void Decode(in MessageMeta meta, byte messageClass, byte version, ReadOnlySpan<byte> payload)
        {
            switch (messageClass)
            {
                case MessageClasses.Position:
                    host.Deliver(meta, CodecRegistry.Resolve<PositionUpdate>(messageClass, version).Decode(payload));
                    break;
                case MessageClasses.Identity:
                    host.Deliver(meta, CodecRegistry.Resolve<IdentityUpdate>(messageClass, version).Decode(payload));
                    break;
                case MessageClasses.VariableSync:
                    host.Deliver(meta, CodecRegistry.Resolve<VariableSyncUpdate>(messageClass, version).Decode(payload));
                    break;
                case MessageClasses.Event:
                    host.Deliver(meta, CodecRegistry.Resolve<EventUpdate>(messageClass, version).Decode(payload));
                    break;
                case MessageClasses.FlightPlan:
                    {
                        FlightPlanUpdate m = CodecRegistry.Resolve<FlightPlanUpdate>(messageClass, version).Decode(payload);
                        m.Owner = meta.Sender;
                        host.Deliver(meta, m);
                    }
                    break;
                case MessageClasses.Notes:
                    {
                        NoteUpdate note = CodecRegistry.Resolve<NoteUpdate>(messageClass, version).Decode(payload);
                        host.Deliver(meta, new NotesBundle
                        {
                            Scope = CommsScope.Single,
                            Users =
                            [
                                new NotesUser
                                {
                                    Guid = note.Guid, Nickname = note.Nickname, Callsign = note.Callsign,
                                    Notes = [new CommsNote { NoteId = note.NoteId, Age = note.Age, Channel = note.Channel, Text = note.Text }],
                                },
                            ],
                        });
                    }
                    break;
                case MessageClasses.Weather:
                    host.Deliver(meta, CodecRegistry.Resolve<WeatherUpdate>(messageClass, version).Decode(payload));
                    break;
                case MessageClasses.WeatherReply:
                    host.Deliver(meta, CodecRegistry.Resolve<WeatherReply>(messageClass, version).Decode(payload));
                    break;
                case MessageClasses.Status:
                    host.Deliver(meta, CodecRegistry.Resolve<StatusUpdate>(messageClass, version).Decode(payload));
                    break;
                case MessageClasses.StatusRequest:
                    host.Deliver(meta, CodecRegistry.Resolve<StatusRequestUpdate>(messageClass, version).Decode(payload));
                    break;
            }
        }
    }
}
