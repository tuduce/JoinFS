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
    /// each directly reachable peer, versioned codecs, single-datagram guaranteed delivery, and relay
    /// of Forwarded envelopes.
    ///
    /// JFP2 is a "link upgrader" (docs/network-plugin-architecture.md §2.4): it only negotiates with
    /// peers the mesh already knows and reaches directly (RouteIsOwnEndPoint, Finding 7), and it
    /// carries only the kinds it negotiated; everything else, and every indirect peer, goes through
    /// the legacy plugin. When a Forwarded JFP2 message has to reach a node that doesn't speak JFP2
    /// for that class, this plugin decodes it and hands it to the core, which re-sends it with the
    /// target's protocol (the generic translation path, design §2.6).
    ///
    /// Ported from the JFP2 regions of Node.cs and Network.cs, with two behaviour changes:
    /// relayed-JFP2 is no longer *originated* (the old sender only checked its hub's JFP2 support,
    /// not the target's, and silently lost guaranteed/unsupported classes at the hub - a peer
    /// reached through a hub now uses the legacy relay, as the CLAUDE.md notes describe); and
    /// guaranteed messages translated to legacy are now delivered hop-by-hop instead of dropped.
    /// </summary>
    public sealed class Jfp2Plugin : IProtocolPlugin, IDescribesLinks
    {
        const int HelloMaxAttempts = 5;
        const double HelloRetryInterval = 2.0;
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
            public byte[] Payload;
            public int Attempts;
            public double NextRetry;
        }

        sealed class IdentitySent
        {
            public IdentityUpdate Last;
            public double Time;
        }

        IProtocolHost host;
        readonly Encoder encoder;
        readonly Dictionary<NodeId, PeerSession> sessions = [];
        readonly Dictionary<(NodeId Peer, ushort Id), Pending> pending = [];
        readonly Dictionary<(NodeId Peer, ushort Id), double> recentlySeen = [];
        readonly Dictionary<(uint ObjectId, NodeId Peer), IdentitySent> identitySent = [];
        readonly List<NodeId> targets = [];
        readonly List<(NodeId, ushort)> scratchKeys = [];
        readonly byte[] payloadBuffer = new byte[16384];
        readonly byte[] datagramBuffer = new byte[16384 + 64];
        ushort nextPeerId = 1;
        ushort nextGuaranteedId = 1;
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

        // ================================================================== negotiation state

        bool TryGetSession(NodeId peer, out Peer p, out PeerSession session)
        {
            session = null;
            return host.Peers.TryGet(peer, out p) && p.RouteIsOwnEndPoint
                && sessions.TryGetValue(peer, out session) && session.HandshakeComplete && !session.AssumedLegacy;
        }

        byte AgreedVersion(NodeId peer, int messageClass, out Peer p, out PeerSession session)
        {
            if (messageClass >= 0 && TryGetSession(peer, out p, out session))
            {
                return session.AgreedAppVersion[messageClass];
            }
            p = null;
            session = null;
            return 0;
        }

        public bool CanCarry(NodeId peer, MessageKind kind) => peer.Valid() && AgreedVersion(peer, ClassFor(kind), out _, out _) > 0;

        /// <summary>The session peer a datagram came from: whose own or route endpoint it is.</summary>
        Peer FindPeer(IPEndPoint endPoint)
        {
            foreach (Peer peer in host.Peers.All)
            {
                if (peer.EndPoint.Equals(endPoint) || peer.RouteEndPoint.Equals(endPoint)) return peer;
            }
            return null;
        }

        ushort NextPeerId()
        {
            ushort id = nextPeerId++;
            if (nextPeerId == 0) nextPeerId = 1;
            return id;
        }

        ushort NextGuaranteedId()
        {
            ushort id = nextGuaranteedId++;
            if (nextGuaranteedId == 0) nextGuaranteedId = 1;
            return id;
        }

        public PeerLinkState? DescribeLink(Peer peer)
        {
            if (sessions.TryGetValue(peer.Id, out PeerSession session))
            {
                if (session.HandshakeComplete && !session.AssumedLegacy) return PeerLinkState.Negotiated;
                if (session.AssumedLegacy) return PeerLinkState.Legacy;
                return PeerLinkState.Negotiating;
            }
            return peer.RouteIsOwnEndPoint ? PeerLinkState.Negotiating : PeerLinkState.Legacy;
        }

        /// <summary>For tests and diagnostics.</summary>
        public bool IsNegotiated(NodeId peer) => TryGetSession(peer, out _, out _);

        // ================================================================== periodic

        public void Tick()
        {
            DoHandshake();
            DoGuaranteedRetry();
        }

        void DoHandshake()
        {
            double now = Now;
            scratchKeys.Clear();
            foreach (Peer peer in host.Peers.All)
            {
                if (!sessions.TryGetValue(peer.Id, out PeerSession session))
                {
                    // negotiation is two-party and direct-only: a Hello to an indirect peer would land
                    // on its relay (see the Finding 7 notes in the implementation review)
                    if (!peer.RouteIsOwnEndPoint) continue;
                    session = new PeerSession { LocalAssignedId = NextPeerId(), HelloAttempts = 1, NextHelloAttempt = now + HelloRetryInterval };
                    sessions[peer.Id] = session;
                    SendHello(peer, session);
                }
                else if (!session.HandshakeComplete && !session.AssumedLegacy)
                {
                    if (!peer.RouteIsOwnEndPoint)
                    {
                        // went indirect mid-negotiation: forget it; negotiation resumes if it comes back
                        scratchKeys.Add((peer.Id, 0));
                    }
                    else if (now > session.NextHelloAttempt)
                    {
                        if (session.HelloAttempts >= HelloMaxAttempts)
                        {
                            session.AssumedLegacy = true;
                            host.LinkChanged(peer.Id);
                            host.Log(NetLogLevel.Network, "JFP2: " + peer.Id + " did not answer Hello after " + session.HelloAttempts + " attempts - assuming legacy-only peer");
                        }
                        else
                        {
                            session.HelloAttempts++;
                            session.NextHelloAttempt = now + HelloRetryInterval;
                            SendHello(peer, session);
                        }
                    }
                }
            }
            foreach (var (id, _) in scratchKeys) sessions.Remove(id);
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
                    SendDatagram(p.EndPoint, p.Flags, p.MessageClass, p.SenderPeerId, p.RecipientPeerId, p.Payload, kv.Key.Id, 0, 1);
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
            sessions.Remove(peer.Id);
            RemoveWhere(pending, k => k.Peer == peer.Id);
            RemoveWhere(recentlySeen, k => k.Peer == peer.Id);
            RemoveWhere(identitySent, k => k.Peer == peer.Id);
        }

        static void RemoveWhere<TKey, TValue>(Dictionary<TKey, TValue> map, Func<TKey, bool> predicate)
        {
            List<TKey> doomed = null;
            foreach (TKey key in map.Keys)
            {
                if (predicate(key)) (doomed ??= []).Add(key);
            }
            if (doomed != null) foreach (TKey key in doomed) map.Remove(key);
        }

        public void OnSessionReset()
        {
            sessions.Clear();
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

        void SendHello(Peer peer, PeerSession session)
        {
            var hello = new HandshakeMessage
            {
                ProtoMajorMin = Envelope.ProtoMajor,
                ProtoMajorMax = Envelope.ProtoMajor,
                Capabilities = LocalCapabilities,
                SelfAssignedId = session.LocalAssignedId,
                Offers = new List<SchemaOffer>(LocalOffers),
            };
            SendDatagram(peer.RouteEndPoint, EnvelopeFlags.Internal, MessageClasses.Hello, session.LocalAssignedId, session.RemoteAssignedId, hello.Serialize());
            host.Log(NetLogLevel.Network, "JFP2: Send Hello to " + peer.Id + " (attempt " + session.HelloAttempts + ")");
        }

        void SendHelloAck(IPEndPoint endPoint, PeerSession session, byte result)
        {
            var ack = new HandshakeMessage
            {
                ProtoMajorMin = Envelope.ProtoMajor,
                ProtoMajorMax = Envelope.ProtoMajor,
                Capabilities = LocalCapabilities,
                SelfAssignedId = session.LocalAssignedId,
                Result = result,
                Offers = new List<SchemaOffer>(LocalOffers),
            };
            SendDatagram(endPoint, EnvelopeFlags.Internal, MessageClasses.HelloAck, session.LocalAssignedId, session.RemoteAssignedId, ack.Serialize());
        }

        /// <summary>Send one application message to a negotiated peer, reliably if asked.</summary>
        void SendApplication(Peer peer, PeerSession session, byte messageClass, ReadOnlySpan<byte> payload, bool guaranteed)
        {
            if (!guaranteed)
            {
                SendDatagram(peer.RouteEndPoint, EnvelopeFlags.None, messageClass, session.LocalAssignedId, session.RemoteAssignedId, payload);
                return;
            }
            ushort id = NextGuaranteedId();
            SendDatagram(peer.RouteEndPoint, EnvelopeFlags.Guaranteed, messageClass, session.LocalAssignedId, session.RemoteAssignedId, payload, id, 0, 1);
            pending[(peer.Id, id)] = new Pending
            {
                EndPoint = peer.RouteEndPoint,
                Flags = EnvelopeFlags.Guaranteed,
                MessageClass = messageClass,
                SenderPeerId = session.LocalAssignedId,
                RecipientPeerId = session.RemoteAssignedId,
                Payload = payload.ToArray(),
                Attempts = 1,
                NextRetry = Now + GuaranteedRetryInterval,
            };
        }

        void SendGuaranteedDone(IPEndPoint endPoint, PeerSession session, ushort guaranteedId, NodeId? relayTrueOrigin)
        {
            Span<byte> payload = stackalloc byte[2];
            BinaryPrimitives.WriteUInt16LittleEndian(payload, guaranteedId);
            if (relayTrueOrigin.HasValue)
            {
                // the acked message came through a relay: route the ack back through it to the true sender
                SendDatagram(endPoint, EnvelopeFlags.Internal | EnvelopeFlags.Forwarded, MessageClasses.GuaranteedDone, 0, 0, payload,
                    origin: ToRelay(Local), target: ToRelay(relayTrueOrigin.Value));
            }
            else
            {
                SendDatagram(endPoint, EnvelopeFlags.Internal, MessageClasses.GuaranteedDone, session.LocalAssignedId, session.RemoteAssignedId, payload);
            }
        }

        // ================================================================== canonical → wire

        public void Send<T>(in MessageMeta meta, in T message, ReadOnlySpan<NodeId> recipients) where T : struct, IMessage
        {
            targets.Clear();
            if (meta.EndPoint != null)
            {
                NodeId known = meta.Recipient.Valid() ? meta.Recipient : FindPeer(meta.EndPoint)?.Id ?? default;
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

            if (envelope.IsForwarded && ToNodeId(envelope.TargetNuid) != Local)
            {
                Relay(from, envelope, datagram, payload);
                return;
            }

            Peer sessionPeer = FindPeer(from);
            if (envelope.IsGuaranteed && !AckGuaranteed(from, sessionPeer, envelope))
            {
                return; // duplicate of something already delivered
            }

            if (!envelope.IsInternal)
            {
                if (sessionPeer == null || !sessions.TryGetValue(sessionPeer.Id, out PeerSession session) || !session.HandshakeComplete)
                {
                    host.Log(NetLogLevel.Network, "JFP2: application message from " + from + " with no completed handshake - ignored");
                    return;
                }
                byte version = session.AgreedAppVersion[envelope.RawMessageClass];
                if (version == 0)
                {
                    host.Log(NetLogLevel.Network, "JFP2: class " + envelope.RawMessageClass + " from " + sessionPeer.Id + " was never agreed on - ignored");
                    return;
                }
                var meta = new MessageMeta
                {
                    Sender = envelope.IsForwarded ? ToNodeId(envelope.OriginNuid) : sessionPeer.Id,
                    Recipient = Local,
                    EndPoint = sessionPeer.SendEstablished ? sessionPeer.RouteEndPoint : from,
                    Guaranteed = envelope.IsGuaranteed,
                    Forwarded = envelope.IsForwarded,
                };
                Decode(meta, envelope.RawMessageClass, version, payload);
                return;
            }

            switch (envelope.RawMessageClass)
            {
                case MessageClasses.Hello: HandleHello(from, payload); break;
                case MessageClasses.HelloAck: HandleHelloAck(from, payload); break;
                case MessageClasses.GuaranteedDone:
                    if (sessionPeer != null && payload.Length >= 2)
                    {
                        pending.Remove((sessionPeer.Id, BinaryPrimitives.ReadUInt16LittleEndian(payload)));
                    }
                    break;
            }
        }

        /// <summary>Ack a guaranteed datagram (always - a duplicate means our ack was lost); false if already delivered.</summary>
        bool AckGuaranteed(IPEndPoint from, Peer sessionPeer, in Envelope envelope)
        {
            if (sessionPeer == null || !sessions.TryGetValue(sessionPeer.Id, out PeerSession session))
            {
                return true;
            }
            NodeId dedup = envelope.IsForwarded ? ToNodeId(envelope.OriginNuid) : sessionPeer.Id;
            var key = (dedup, envelope.GuaranteedId);
            bool duplicate = recentlySeen.ContainsKey(key);
            recentlySeen[key] = Now;
            SendGuaranteedDone(from, session, envelope.GuaranteedId, envelope.IsForwarded ? dedup : null);
            return !duplicate;
        }

        void HandleHello(IPEndPoint from, ReadOnlySpan<byte> payload)
        {
            HandshakeMessage hello = HandshakeMessage.Deserialize(payload);
            // only peers the mesh already knows (the legacy Join always happens first)
            Peer peer = FindPeer(from);
            if (peer == null)
            {
                host.Log(NetLogLevel.Network, "JFP2: Hello from unknown endpoint " + from + " - ignored");
                return;
            }
            if (!sessions.TryGetValue(peer.Id, out PeerSession session))
            {
                session = new PeerSession { LocalAssignedId = NextPeerId() };
                sessions[peer.Id] = session;
            }
            session.RemoteAssignedId = hello.SelfAssignedId;
            if (hello.ProtoMajorMin > Envelope.ProtoMajor || hello.ProtoMajorMax < Envelope.ProtoMajor)
            {
                SendHelloAck(from, session, result: 1);
                return;
            }
            Negotiator.Resolve(session, LocalCapabilities, LocalOffers, hello.Capabilities, hello.Offers);
            session.HandshakeComplete = true;
            session.AssumedLegacy = false;
            SendHelloAck(from, session, result: 0);
            host.LinkChanged(peer.Id);
            host.Log(NetLogLevel.Network, "JFP2: Hello from " + peer.Id + " - handshake complete");
        }

        void HandleHelloAck(IPEndPoint from, ReadOnlySpan<byte> payload)
        {
            HandshakeMessage ack = HandshakeMessage.Deserialize(payload);
            Peer peer = FindPeer(from);
            if (peer == null || !sessions.TryGetValue(peer.Id, out PeerSession session))
            {
                return;
            }
            if (ack.Result != 0)
            {
                session.AssumedLegacy = true;
                host.LinkChanged(peer.Id);
                return;
            }
            session.RemoteAssignedId = ack.SelfAssignedId;
            Negotiator.Resolve(session, LocalCapabilities, LocalOffers, ack.Capabilities, ack.Offers);
            session.HandshakeComplete = true;
            host.LinkChanged(peer.Id);
            host.Log(NetLogLevel.Network, "JFP2: HelloAck from " + peer.Id + " - handshake complete");
        }

        /// <summary>
        /// A Forwarded envelope for another node. If the target speaks JFP2 for this class (or it's
        /// internal, e.g. a relayed ack), forward the bytes unchanged. Otherwise decode it and let the
        /// core re-send it in the target's protocol - the one place translation actually happens,
        /// since every node speaks legacy.
        /// </summary>
        void Relay(IPEndPoint from, in Envelope envelope, ReadOnlySpan<byte> datagram, ReadOnlySpan<byte> payload)
        {
            NodeId target = ToNodeId(envelope.TargetNuid);
            NodeId origin = ToNodeId(envelope.OriginNuid);
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
            if (envelope.IsInternal || AgreedVersion(target, envelope.RawMessageClass, out _, out _) > 0)
            {
                host.Transport.Send(targetPeer.RouteEndPoint, datagram);
                return;
            }
            if (!sessions.TryGetValue(origin, out PeerSession originSession) || originSession.AgreedAppVersion[envelope.RawMessageClass] == 0)
            {
                host.Log(NetLogLevel.Network, "JFP2: relay origin " + origin + " has no agreed class " + envelope.RawMessageClass + " with me - dropped");
                return;
            }
            if (envelope.IsGuaranteed)
            {
                // this hop is complete once we have it; the downstream protocol takes over delivery
                SendGuaranteedDone(from, originSession, envelope.GuaranteedId, null);
            }
            var meta = new MessageMeta { Sender = origin, Recipient = target, Guaranteed = envelope.IsGuaranteed, Forwarded = true };
            Decode(meta, envelope.RawMessageClass, originSession.AgreedAppVersion[envelope.RawMessageClass], payload);
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
