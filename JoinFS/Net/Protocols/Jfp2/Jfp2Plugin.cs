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
    /// each neighbor, versioned codecs, guaranteed delivery (<see cref="Jfp2Reliability"/>), and relay
    /// of Forwarded envelopes.
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
        const double IdentityHeartbeatInterval = 4.0;
        const double IdentityForgetAfter = 30.0;
        const int GuaranteedDoneSize = 3;
        /// <summary>VariableSync payload budget per datagram, to stay under a safe UDP MTU like legacy's variable messages (about 850 bytes).</summary>
        const int VariableSyncMaxPayload = 1000;
        const ulong LocalCapabilities = (ulong)Capability.None;

        /// <summary>A node seen answering at an endpoint, so other nodes claiming that endpoint are known to be behind it.</summary>
        readonly record struct Occupant(NodeId Node, double Expire);

        sealed class IdentitySent
        {
            public IdentityUpdate Last;
            public double Time;
        }

        IProtocolHost host;
        Jfp2Reliability reliability;
        readonly Jfp2Profile profile;
        readonly Encoder encoder;
        readonly Dictionary<NodeId, PeerSession> sessions = [];
        readonly Dictionary<ushort, PeerSession> sessionsById = [];
        readonly Dictionary<IPEndPoint, Occupant> occupants = [];
        readonly Random random = new();
        readonly Dictionary<(NodeId Owner, uint ObjectId, NodeId Peer), IdentitySent> identitySent = [];
        readonly List<NodeId> targets = [];
        readonly byte[] payloadBuffer = new byte[16384];
        readonly byte[] datagramBuffer = new byte[16384 + 64];
        /// <summary>Who a send is on behalf of: this node, or (translation at a relay) the message's author.</summary>
        NodeId sendOrigin;
        readonly ushort firstGuaranteedId;
        readonly string build;
        double nextIdentitySweep;

        /// <param name="firstGuaranteedId">Where guaranteed ids start; production seeds it from the clock (see <see cref="Jfp2Reliability"/>).</param>
        /// <param name="build">The build this node says it runs in its Hello/HelloAck (<see cref="HandshakeMessage.Build"/>, which cleans it on the way out); null says nothing.</param>
        /// <param name="profile">The message classes, versions and codecs this node speaks; null is this build's (<see cref="Jfp2Profile.Default"/>).</param>
        public Jfp2Plugin(ushort firstGuaranteedId = 1, string build = null, Jfp2Profile profile = null)
        {
            this.firstGuaranteedId = firstGuaranteedId;
            this.build = build;
            this.profile = profile ?? Jfp2Profile.Default;
            foreach (ClassDescriptor c in this.profile.Classes)
            {
                Type sender = OwnSender(c.Kind);
                if (!c.SentAsIs && c.GetType() != sender)
                {
                    // it would be advertised (CanCarry) and every message of it dropped, never sent over legacy
                    throw new ArgumentException("JFP2 profile: class " + c.MessageClass + " (" + c.Kind + ") is to be sent by the plugin, which has no sender for it");
                }
                if (c.SentAsIs && sender != null)
                {
                    // sent as is, it would skip its sender's rules (identity before position, chunking, one note per message)
                    throw new ArgumentException("JFP2 profile: class " + c.MessageClass + " (" + c.Kind + ") must be sent by the plugin's own sender, not as is");
                }
            }
            encoder = new Encoder(this);
        }

        /// <summary>The descriptor type of each kind the plugin sends its own way (<see cref="Encoder"/>, <see cref="EnsureIdentity"/>); null for every other kind.</summary>
        static Type OwnSender(MessageKind kind) => kind switch
        {
            MessageKind.Identity => typeof(ClassDescriptor<IdentityUpdate>),
            MessageKind.Position => typeof(ClassDescriptor<PositionUpdate>),
            MessageKind.VariableSync => typeof(ClassDescriptor<VariableSyncUpdate>),
            MessageKind.Notes => typeof(ClassDescriptor<NoteUpdate>),
            _ => null,
        };

        public string Name => "JFP2";
        public int Preference => 10;

        public void Attach(IProtocolHost host)
        {
            this.host = host;
            reliability = new Jfp2Reliability(host, TransmitGuaranteed, firstGuaranteedId);
        }

        /// <summary>For tests and diagnostics: guaranteed segments not yet acknowledged.</summary>
        public int GuaranteedPendingCount => reliability.PendingCount;

        /// <summary>For tests and diagnostics: (owner, object, peer) identities remembered as sent.</summary>
        public int IdentitySentCount => identitySent.Count;

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

        byte AgreedVersion(NodeId target, ClassDescriptor messageClass, out Peer peer, out PeerSession hop)
        {
            peer = null;
            hop = null;
            if (messageClass == null)
            {
                return 0;
            }
            hop = NextHop(target, out peer);
            return hop == null ? (byte)0 : hop.AgreedAppVersion[messageClass.MessageClass];
        }

        public bool CanCarry(NodeId peer, MessageKind kind) => peer.Valid() && AgreedVersion(peer, profile.ForKind(kind), out _, out _) > 0;

        static IPEndPoint Copy(IPEndPoint endPoint) => new(endPoint.Address, endPoint.Port);

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

        /// <summary>For tests and diagnostics: the schema version agreed for a kind with the peer's next hop (0: JFP2 does not carry it there).</summary>
        public byte VersionFor(NodeId peer, MessageKind kind) => AgreedVersion(peer, profile.ForKind(kind), out _, out _);

        /// <summary>For tests and diagnostics: the build a neighbor said it runs in its last handshake message, or null.</summary>
        public string BuildOf(NodeId neighbor) => sessions.TryGetValue(neighbor, out PeerSession session) ? session.Build : null;

        // ================================================================== periodic

        public void Tick()
        {
            DoHandshake();
            reliability.Tick();
            ForgetIdleIdentities();
        }

        /// <summary>
        /// Objects come and go (AI traffic) and nothing on this path is told when one is removed, so a
        /// record not refreshed for a while is dropped: were the object to be sent again, its identity
        /// simply goes out first, as for a new one.
        /// </summary>
        void ForgetIdleIdentities()
        {
            double now = Now;
            if (now < nextIdentitySweep || identitySent.Count == 0)
            {
                return;
            }
            nextIdentitySweep = now + IdentityForgetAfter;
            identitySent.RemoveWhere((_, sent) => now - sent.Time >= IdentityForgetAfter);
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

        public void OnPeerRemoved(Peer peer)
        {
            RemoveSession(peer.Id);
            reliability.RemovePeer(peer.Id);
            identitySent.RemoveWhere((k, _) => k.Peer == peer.Id || k.Owner == peer.Id);
            occupants.RemoveWhere((_, o) => o.Node == peer.Id);
        }

        public void OnSessionReset()
        {
            sessions.Clear();
            sessionsById.Clear();
            occupants.Clear();
            reliability.Clear();
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
            Build = build,
            Offers = new List<SchemaOffer>(profile.Offers),
        };

        // Internal and nothing else: no flag a peer could need a capability to read
        // (docs/reference/jfp2-protocol.md §4.2); the envelope goes out as Envelope.HandshakeProtoMajor
        void SendHello(IPEndPoint endPoint, PeerSession session) =>
            SendDatagram(endPoint, EnvelopeFlags.Internal, MessageClasses.Hello, session.LocalAssignedId, session.RemoteAssignedId, MakeHandshake(session, 0).Serialize());

        void SendHelloAck(IPEndPoint endPoint, PeerSession session, byte result) =>
            SendDatagram(endPoint, EnvelopeFlags.Internal, MessageClasses.HelloAck, session.LocalAssignedId, session.RemoteAssignedId, MakeHandshake(session, result).Serialize());

        /// <summary>
        /// Remember the build a neighbor says it runs (none: it restarted with a build that does not
        /// say), and log it: the first one at event level, later changes at network level only, so
        /// Hellos that keep changing it (buggy or forged) cannot flood the monitor.
        /// </summary>
        void LearnBuild(PeerSession session, string peerBuild)
        {
            if (peerBuild != null && peerBuild != session.Build)
            {
                host.Log(session.BuildLearned ? NetLogLevel.Network : NetLogLevel.Event, "JFP2: " + session.Peer + " runs build " + peerBuild);
                session.BuildLearned = true;
            }
            session.Build = peerBuild;
        }

        /// <summary>
        /// Send one application message toward <paramref name="target"/> through <paramref name="hop"/>:
        /// unreliably, or handed to <see cref="Jfp2Reliability"/>, which makes every attempt through
        /// <see cref="TransmitGuaranteed"/>.
        /// </summary>
        void SendApplication(Peer target, PeerSession hop, ClassDescriptor messageClass, ReadOnlySpan<byte> payload)
        {
            byte number = messageClass.MessageClass;
            if (messageClass.Guaranteed)
            {
                reliability.Send(target.Id, sendOrigin, number, hop.AgreedAppVersion[number], payload);
            }
            else
            {
                SendVia(hop, target.Id, sendOrigin, EnvelopeFlags.None, number, payload, 0, 0, 0);
            }
        }

        /// <summary>One attempt at a guaranteed segment, through the target's next hop as it is now.</summary>
        bool TransmitGuaranteed(NodeId target, NodeId origin, byte messageClass, byte version, ReadOnlySpan<byte> payload,
            ushort guaranteedId, byte guaranteedIndex, byte guaranteedCount, out NodeId hop)
        {
            PeerSession session = NextHop(target, out _);
            if (session == null || session.AgreedAppVersion[messageClass] != version)
            {
                hop = default;
                return false;
            }
            hop = session.Peer;
            SendVia(session, target, origin, EnvelopeFlags.Guaranteed, messageClass, payload, guaranteedId, guaranteedIndex, guaranteedCount);
            return true;
        }

        /// <summary>
        /// An application datagram to <paramref name="hop"/>: unaddressed when the hop is the target and
        /// we are the author, otherwise Forwarded with Origin and Target, to be passed on or translated.
        /// </summary>
        void SendVia(PeerSession hop, NodeId target, NodeId origin, EnvelopeFlags flags, byte messageClass, ReadOnlySpan<byte> payload,
            ushort guaranteedId, byte guaranteedIndex, byte guaranteedCount)
        {
            bool forwarded = hop.Peer != target || origin != Local;
            if (forwarded)
            {
                flags |= EnvelopeFlags.Forwarded;
            }
            SendDatagram(hop.Endpoint, flags, messageClass, hop.LocalAssignedId, hop.RemoteAssignedId, payload, guaranteedId, guaranteedIndex, guaranteedCount,
                forwarded ? ToRelay(origin) : default, forwarded ? ToRelay(target) : default);
        }

        /// <summary>
        /// Acknowledge one segment of a guaranteed message to the neighbor it came from. When it was
        /// relayed to us the ack is addressed to the true sender and travels back through that neighbor.
        /// Payload: GuaranteedId (u16), GuaranteedIndex (u8).
        /// </summary>
        void SendGuaranteedDone(IPEndPoint endPoint, PeerSession hop, ushort guaranteedId, byte guaranteedIndex, NodeId? relayTrueOrigin)
        {
            Span<byte> payload = stackalloc byte[GuaranteedDoneSize];
            BinaryPrimitives.WriteUInt16LittleEndian(payload, guaranteedId);
            payload[2] = guaranteedIndex;
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

        /// <summary>Read a GuaranteedDone payload. Builds before the segment index sent only the id, which acks segment 0.</summary>
        static bool TryReadGuaranteedDone(ReadOnlySpan<byte> payload, out ushort id, out byte index)
        {
            id = 0;
            index = 0;
            if (payload.Length < 2)
            {
                return false;
            }
            id = BinaryPrimitives.ReadUInt16LittleEndian(payload);
            if (payload.Length >= GuaranteedDoneSize)
            {
                index = payload[2];
            }
            return true;
        }

        // ================================================================== canonical → wire

        public void Send<T>(in MessageMeta meta, in T message, ReadOnlySpan<NodeId> recipients) where T : struct, IMessage
        {
            ClassDescriptor messageClass = profile.ForKind(T.Kind);
            if (messageClass == null)
            {
                return; // a kind JFP2 does not carry (CanCarry said so)
            }
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
            if (messageClass.SentAsIs && messageClass is ClassDescriptor<T> plain)
            {
                encoder.SendAsIs(plain, message);
            }
            else
            {
                message.Dispatch(encoder, meta);
            }
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
            ClassDescriptor<IdentityUpdate> identityClass = profile.ForKind<IdentityUpdate>(MessageKind.Identity);
            byte version = identityClass == null ? (byte)0 : session.AgreedAppVersion[identityClass.MessageClass];
            if (version == 0)
            {
                return true;
            }
            var key = (owner, objectId, peer.Id);
            double now = Now;
            if (identitySent.TryGetValue(key, out IdentitySent sent) && sent.Last.SameAs(identity) && now - sent.Time < IdentityHeartbeatInterval)
            {
                return true;
            }
            int length = identityClass.Codec(version).Encode(identity, payloadBuffer);
            SendApplication(peer, session, identityClass, payloadBuffer.AsSpan(0, length));
            identitySent[key] = new IdentitySent { Last = identity, Time = now };
            return true;
        }

        /// <summary>
        /// Canonical to JFP2. A class sent as it is goes through <see cref="SendAsIs"/>; each kind the
        /// plugin sends its own way has an overload here. Each loops the targets because versions are per peer.
        /// </summary>
        sealed class Encoder(Jfp2Plugin p) : IMessageHandler
        {
            public void SendAsIs<T>(ClassDescriptor<T> messageClass, in T message)
            {
                foreach (NodeId target in p.targets)
                {
                    byte version = p.AgreedVersion(target, messageClass, out Peer peer, out PeerSession session);
                    if (version == 0) continue;
                    int length = messageClass.Codec(version).Encode(message, p.payloadBuffer);
                    p.SendApplication(peer, session, messageClass, p.payloadBuffer.AsSpan(0, length));
                }
            }

            public void Handle(in MessageMeta meta, in PositionUpdate m)
            {
                if (p.profile.ForKind<PositionUpdate>(MessageKind.Position) is not { } messageClass) return;
                foreach (NodeId target in p.targets)
                {
                    byte version = p.AgreedVersion(target, messageClass, out Peer peer, out PeerSession session);
                    if (version == 0 || !p.EnsureIdentity(meta.Sender, m.ObjectId, peer, session)) continue;
                    int length = messageClass.Codec(version).Encode(m, p.payloadBuffer);
                    p.SendApplication(peer, session, messageClass, p.payloadBuffer.AsSpan(0, length));
                }
            }

            public void Handle(in MessageMeta meta, in VariableSyncUpdate m)
            {
                if (m.Entries == null || m.Entries.Count == 0) return;
                if (p.profile.ForKind<VariableSyncUpdate>(MessageKind.VariableSync) is not { } messageClass) return;
                List<VariableSyncUpdate> chunks = null;
                foreach (NodeId target in p.targets)
                {
                    byte version = p.AgreedVersion(target, messageClass, out Peer peer, out PeerSession session);
                    if (version == 0) continue;
                    chunks ??= Chunk(m);
                    ICodec<VariableSyncUpdate> codec = messageClass.Codec(version);
                    foreach (VariableSyncUpdate chunk in chunks)
                    {
                        int length = codec.Encode(chunk, p.payloadBuffer);
                        p.SendApplication(peer, session, messageClass, p.payloadBuffer.AsSpan(0, length));
                    }
                }
            }

            /// <summary>
            /// Split into messages whose payload fits <see cref="VariableSyncMaxPayload"/>: at least one
            /// entry each, at most 255 (the count is one byte). Sizes are v1's, the only VariableSync schema.
            /// </summary>
            static List<VariableSyncUpdate> Chunk(in VariableSyncUpdate m)
            {
                var chunks = new List<VariableSyncUpdate>(1);
                int start = 0;
                while (start < m.Entries.Count)
                {
                    int end = start;
                    int size = VariableSyncV1Codec.HeaderSize;
                    while (end < m.Entries.Count && end - start < byte.MaxValue)
                    {
                        int entry = VariableSyncV1Codec.EntrySize(m.Entries[end]);
                        if (end > start && size + entry > VariableSyncMaxPayload) break;
                        size += entry;
                        end++;
                    }
                    chunks.Add(start == 0 && end == m.Entries.Count
                        ? m
                        : new VariableSyncUpdate { ObjectId = m.ObjectId, Entries = m.Entries.GetRange(start, end - start) });
                    start = end;
                }
                return chunks;
            }

            public void Handle(in MessageMeta meta, in NotesBundle m)
            {
                // JFP2 carries one note per message: a bundle goes out as its notes
                if (p.profile.ForKind<NoteUpdate>(MessageKind.Notes) is not { } messageClass) return;
                foreach (NotesUser user in m.Users)
                {
                    foreach (CommsNote note in user.Notes)
                    {
                        var single = new NoteUpdate
                        {
                            Guid = user.Guid, Nickname = user.Nickname, Callsign = user.Callsign,
                            NoteId = note.NoteId, Age = note.Age, Channel = note.Channel, Text = note.Text,
                        };
                        SendAsIs(messageClass, single);
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
            if (!Envelope.TryReadFrom(datagram, out Envelope envelope, out int consumed, out string unsupported))
            {
                // well formed for a build newer than this one (another ProtoMajor, a flag we cannot
                // read): not an error, and it may arrive with every datagram from that peer
                host.Log(NetLogLevel.Network, "JFP2: datagram from " + from + " uses " + unsupported + ", which this build cannot read - dropped");
                return;
            }
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

            NodeId sender = envelope.IsForwarded ? ToNodeId(envelope.OriginNuid) : hop.Peer;
            if (envelope.IsInternal)
            {
                if (envelope.RawMessageClass == MessageClasses.GuaranteedDone && TryReadGuaranteedDone(payload, out ushort id, out byte index))
                {
                    reliability.Acknowledge(sender, hop.Peer, envelope.IsForwarded ? Local : null, id, index);
                }
                return;
            }

            // checked before acking: a sender must not take a message we cannot read as delivered
            byte version = hop.AgreedAppVersion[envelope.RawMessageClass];
            if (version == 0)
            {
                host.Log(NetLogLevel.Network, "JFP2: class " + envelope.RawMessageClass + " from " + hop.Peer + " was never agreed on - ignored");
                return;
            }
            if (envelope.IsGuaranteed)
            {
                // ack every copy: a duplicate means our ack was lost
                SendGuaranteedDone(from, hop, envelope.GuaranteedId, envelope.GuaranteedIndex, envelope.IsForwarded ? sender : null);
                if (reliability.IsDuplicate(sender, envelope.GuaranteedId))
                {
                    return;
                }
            }
            if (!TryComplete(sender, envelope, payload, out ReadOnlySpan<byte> message))
            {
                return;
            }
            var meta = new MessageMeta
            {
                Sender = sender,
                Recipient = Local,
                EndPoint = host.Peers.TryGet(sender, out Peer senderPeer) && senderPeer.SendEstablished ? senderPeer.RouteEndPoint : from,
                Guaranteed = envelope.IsGuaranteed,
                Forwarded = envelope.IsForwarded,
            };
            Decode(meta, envelope.RawMessageClass, version, message);
        }

        /// <summary>The whole message a datagram completes: its own payload, unless it is one segment of a guaranteed message.</summary>
        bool TryComplete(NodeId sender, in Envelope envelope, ReadOnlySpan<byte> payload, out ReadOnlySpan<byte> message)
        {
            if (!envelope.IsGuaranteed)
            {
                message = payload;
                return true;
            }
            return reliability.Reassemble(sender, envelope.GuaranteedId, envelope.GuaranteedIndex, envelope.GuaranteedCount, payload, out message);
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
            LearnBuild(session, hello.Build);
            // a range that includes ours (2..3 from a later build, say) is fine: we speak the common one
            if (hello.ProtoMajorMin > Envelope.ProtoMajor || hello.ProtoMajorMax < Envelope.ProtoMajor)
            {
                SendHelloAck(from, session, result: 1);
                return;
            }
            if (Negotiator.Resolve(session, LocalCapabilities, profile.Offers, hello.Capabilities, hello.Offers) && session.HandshakeComplete)
            {
                host.LinkChanged(sender); // it restarted with different offers: cached routes are stale
            }
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
            LearnBuild(session, ack.Build);
            bool agreementChanged = Negotiator.Resolve(session, LocalCapabilities, profile.Offers, ack.Capabilities, ack.Offers);
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
            else if (agreementChanged)
            {
                host.LinkChanged(session.Peer);
            }
        }

        /// <summary>
        /// A Forwarded envelope for another node. Only a direct neighbor of ours can be relayed to. If
        /// that neighbor agreed the same schema as the sender's hop used (or the datagram is internal,
        /// e.g. a relayed ack), pass the datagram on with the two hop ids rewritten. Otherwise - a
        /// legacy-only target, or a different schema version - decode it and let the core re-send it in
        /// the target's terms, the one place translation happens since every node speaks legacy. An
        /// Identity passed on is decoded for the core too, since a translated position needs it.
        /// </summary>
        void Relay(IPEndPoint from, PeerSession hop, in Envelope envelope, ReadOnlySpan<byte> datagram, ReadOnlySpan<byte> payload)
        {
            NodeId target = ToNodeId(envelope.TargetNuid);
            NodeId origin = ToNodeId(envelope.OriginNuid);
            if (envelope.IsInternal && envelope.RawMessageClass == MessageClasses.GuaranteedDone && TryReadGuaranteedDone(payload, out ushort id, out byte index)
                && reliability.Acknowledge(origin, hop.Peer, ackFor: target, id, index))
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
                if (version > 0 && !envelope.IsGuaranteed && profile.ForClass(messageClass)?.Kind == MessageKind.Identity
                    && profile.ForKind<IdentityUpdate>(MessageKind.Identity) is { } identityClass)
                {
                    // the core keeps identity as state for whoever it is for: a position of this object
                    // the target agreed another version of is translated here, and goes out only after
                    // it (EnsureIdentity), which now knows the target has it
                    IdentityUpdate identity = identityClass.Codec(version).Decode(payload);
                    host.Deliver(new MessageMeta { Sender = origin, Recipient = target, Forwarded = true }, identity);
                    identitySent[(origin, identity.ObjectId, target)] = new IdentitySent { Last = identity, Time = Now };
                }
                return;
            }
            if (envelope.IsInternal || version == 0)
            {
                host.Log(NetLogLevel.Network, "JFP2: relay from " + origin + " to " + target + " cannot be carried (class " + messageClass + ") - dropped");
                return;
            }
            if (envelope.IsGuaranteed)
            {
                // this hop is complete once we have it; the downstream protocol takes over delivery.
                // A retransmission means our ack was lost: ack again, but it was translated already.
                SendGuaranteedDone(from, hop, envelope.GuaranteedId, envelope.GuaranteedIndex, null);
                if (reliability.IsDuplicate(origin, envelope.GuaranteedId))
                {
                    return;
                }
            }
            if (!TryComplete(origin, envelope, payload, out ReadOnlySpan<byte> message))
            {
                return;
            }
            var meta = new MessageMeta { Sender = origin, Recipient = target, Guaranteed = envelope.IsGuaranteed, Forwarded = true };
            Decode(meta, messageClass, version, message);
        }

        /// <summary>A class agreed on (a version above 0) is one of the profile's: decode it and hand it to the core.</summary>
        void Decode(in MessageMeta meta, byte messageClass, byte version, ReadOnlySpan<byte> payload) =>
            profile.ForClass(messageClass).Deliver(host, meta, version, payload);
    }
}
