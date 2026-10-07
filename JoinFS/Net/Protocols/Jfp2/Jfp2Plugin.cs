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
    /// Today JFP2 is a per-hop "link upgrader" (docs/network-plugin-architecture.md §2.4): the legacy
    /// mesh holds membership and JFP2 carries what it negotiated with each neighbor. It is to become
    /// the successor of legacy (§2.13 there). A session is with a NEIGHBOR: a node we exchange
    /// datagrams with directly, identified by the name it states in the handshake and never by the
    /// endpoint it answers from (two nodes can share one public endpoint). A peer that is not a
    /// neighbor is reached through the neighbor that carries its traffic (<see cref="NextHop"/>): the
    /// datagram goes to that hop in the hop's negotiated schema, addressed end to end with Forwarded
    /// Origin/Target names (<see cref="NodeName"/>), and the hop either forwards it
    /// byte for byte (the target agreed the same schema) or decodes it and lets the core re-send it in
    /// the target's own terms (the generic translation path, design §2.6). Whatever a peer's hop
    /// cannot carry goes through the legacy plugin.
    ///
    /// Sessions stay honest with a keepalive: a Hello every few seconds to the verified endpoint,
    /// whose answer must again name the same node. A session that stops answering, or is answered by
    /// someone else (the network now steers the endpoint elsewhere), falls back to legacy until it is
    /// verified again.
    ///
    /// This file holds the state, the periodic work and the datagrams out; the rest is in
    /// Jfp2Plugin.Handshake.cs (sessions, handshake, keepalive, occupants), Jfp2Plugin.Relay.cs (next
    /// hop, relay) and Jfp2Plugin.Codec.cs (encoder, decode).
    ///
    /// Ported from the JFP2 regions of Node.cs and Network.cs.
    /// </summary>
    public sealed partial class Jfp2Plugin : IProtocolPlugin, IDescribesLinks
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

        /// <summary>
        /// The name this node writes for a node: its kind-0 name, since every node has a legacy id
        /// until the mesh runs over JFP2 (docs/reference/jfp2-protocol.md §4.9).
        /// </summary>
        static NodeName NameOf(NodeId node) => NodeName.FromLegacy(node);

        /// <summary>
        /// The node a name belongs to. This build resolves kind 0 only, and only to a valid node id: an
        /// all-zero or ip-0 name would read as "this node" in the app. What carries any other name is
        /// dropped (§4.9).
        /// </summary>
        static bool TryResolve(NodeName name, out NodeId node) => name.TryGetLegacy(out node) && node.Valid();

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
            ushort guaranteedId = 0, byte guaranteedIndex = 0, byte guaranteedCount = 0, NodeName origin = default, NodeName target = default)
        {
            if (endPoint == null) return;
            var envelope = new Envelope(flags, senderPeerId, recipientPeerId, messageClass, guaranteedId, guaranteedIndex, guaranteedCount, origin, target);
            int header = envelope.WriteTo(datagramBuffer);
            payload.CopyTo(datagramBuffer.AsSpan(header));
            host.Transport.Send(endPoint, datagramBuffer.AsSpan(0, header + payload.Length));
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
                forwarded ? NameOf(origin) : default, forwarded ? NameOf(target) : default);
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
                    origin: NameOf(Local), target: NameOf(relayTrueOrigin.Value));
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
    }
}
