using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Net;
using System.Text;

// Ported from ProtocolV2Reference/Negotiation.cs (docs/reference/jfp2-protocol.md §5) as part of
// docs/protocol-v2-implementation-plan.md Phase 1. PeerSession gained two fields
// (HelloAttempts/NextHelloAttempt) beyond the reference implementation, needed to drive the actual
// Hello-retry timer in Jfp2Plugin.DoHandshake.

namespace JoinFS.Net.Jfp2
{
    /// <summary>
    /// One peer's declared support range for a single message class: the inclusive [MinVersion,
    /// MaxVersion] schema versions it can both encode and decode for that class. A peer that has
    /// never heard of a class simply omits it from its offer list - see Negotiator.Resolve for how
    /// that's handled on the other side.
    /// </summary>
    public readonly struct SchemaOffer
    {
        public readonly bool Internal;
        public readonly byte MessageClass;
        public readonly byte MinVersion;
        public readonly byte MaxVersion;

        public SchemaOffer(bool isInternal, byte messageClass, byte minVersion, byte maxVersion)
        {
            Internal = isInternal;
            MessageClass = messageClass;
            MinVersion = minVersion;
            MaxVersion = maxVersion;
        }
    }

    /// <summary>
    /// Optional boolean capabilities, independent of any one message class's schema version. A
    /// capability is only usable with a given peer once BOTH sides have set the bit - see
    /// Negotiator.Resolve, which ANDs the two capability masks together.
    /// </summary>
    [Flags]
    public enum Capability : ulong
    {
        None = 0,
        Coalescing = 1UL << 0,
        QuantizedPosition = 1UL << 1,
        Ipv6Peers = 1UL << 2,
        SelectiveAck = 1UL << 3,
    }

    /// <summary>
    /// A minimal length-prefixed, tag-value extension area appended to Hello/HelloAck. Anything not
    /// anticipated by the fixed Hello layout (the node names and build that came first, a future auth
    /// token, a vendor-specific extension, ...) can be added here without changing how any existing field
    /// is parsed - an unrecognized tag is simply skipped by its declared length instead of desyncing
    /// the rest of the message. This generalizes the one place the legacy protocol already does this
    /// (the Notes message's length-prefixed inner records, docs/network-protocol.md §8.9/§9.2) to the
    /// handshake message itself, so the negotiation protocol can evolve the same way application
    /// messages can.
    /// </summary>
    public static class Tlv
    {
        public static void Write(List<byte> dest, ushort tag, ReadOnlySpan<byte> payload)
        {
            Span<byte> header = stackalloc byte[4];
            BinaryPrimitives.WriteUInt16LittleEndian(header.Slice(0, 2), tag);
            BinaryPrimitives.WriteUInt16LittleEndian(header.Slice(2, 2), (ushort)payload.Length);
            dest.AddRange(header.ToArray());
            dest.AddRange(payload.ToArray());
        }

        public static Dictionary<ushort, byte[]> ReadAll(ReadOnlySpan<byte> src)
        {
            var result = new Dictionary<ushort, byte[]>();
            int i = 0;
            while (i + 4 <= src.Length)
            {
                ushort tag = BinaryPrimitives.ReadUInt16LittleEndian(src.Slice(i, 2));
                ushort len = BinaryPrimitives.ReadUInt16LittleEndian(src.Slice(i + 2, 2));
                i += 4;
                if (i + len > src.Length) break; // malformed/truncated - stop rather than throw; the handshake extension area is best-effort
                result[tag] = src.Slice(i, len).ToArray();
                i += len;
            }
            return result;
        }
    }

    /// <summary>
    /// The Hello / HelloAck handshake payload. Sent using the JFP2 envelope with
    /// EnvelopeFlags.Internal set and RawMessageClass = MessageClasses.Hello or HelloAck. Because
    /// PeerId assignment IS the subject of this exchange, the very first Hello a node sends a new
    /// peer has no meaningful RecipientPeerId yet - the reply is simply addressed back to the UDP
    /// source IPEndPoint, exactly like the legacy Join/JoinReply exchange already does today.
    ///
    /// This is the permanent entry point of JFP2: its envelope (Envelope.HandshakeProtoMajor), its
    /// fixed fields and its offer list layout never change. Anything new goes into the extension
    /// area, and a later major version is agreed through ProtoMajorMin/Max inside it
    /// (docs/reference/jfp2-protocol.md §5.2; pinned by HandshakeGoldenTests).
    /// </summary>
    public sealed class HandshakeMessage
    {
        /// <summary>Extension tag carrying the sender's own names (<see cref="Names"/>).</summary>
        public const ushort NamesTag = 1;

        /// <summary>Extension tag carrying the sender's build (<see cref="Build"/>).</summary>
        public const ushort BuildTag = 2;

        /// <summary>The longest <see cref="Build"/> sent or kept, in UTF-8 bytes.</summary>
        public const int BuildMaxBytes = 64;

        public byte ProtoMajorMin;
        public byte ProtoMajorMax;
        public ulong Capabilities;
        public ushort SelfAssignedId; // the PeerId the sender wants to be addressed by from now on
        public List<SchemaOffer> Offers = new();
        public byte Result; // HelloAck only: 0 = Accepted, 1 = NoCompatibleProtoMajor. Ignored on Hello.
        public Dictionary<ushort, byte[]> Extensions = new();

        /// <summary>
        /// Who is speaking: the sending node's own names, preferred first (in a HelloAck, the node that
        /// actually answered). The one thing an endpoint cannot tell you when several nodes share it
        /// (two nodes behind one NAT port forward, a hub and a client), so a receiver binds a session
        /// to a name and never to the datagram's source. This build sends one, kind 0 (its legacy id),
        /// and binds by the kind-0 names it reads, skipping the others. Travels as an extension of
        /// 8 bytes per name; a value that is not a whole number of names is ignored. Empty when the
        /// sender named none (a peer that names none it can resolve is treated as legacy-only).
        /// </summary>
        public List<NodeName> Names = new();

        /// <summary>
        /// Which build is speaking, as text (JoinFS sends its version and variant), so a hub can count
        /// which builds speak JFP2. Diagnostic only: nothing about the protocol depends on it,
        /// negotiation alone does. Written and read through <see cref="CleanBuild"/>, so what goes on the
        /// wire is always within its limits and what a peer sent is always safe to log. Null when the
        /// sender did not say.
        /// </summary>
        public string Build;

        /// <summary>
        /// The build text as it may travel: printable ASCII only (0x20-0x7E; it comes from
        /// unauthenticated Hellos and ends up in the log, so no control, bidi or zero-width
        /// characters), at most <see cref="BuildMaxBytes"/> long. Null when nothing is left.
        /// </summary>
        public static string CleanBuild(ReadOnlySpan<char> text)
        {
            Span<char> clean = stackalloc char[BuildMaxBytes];
            int length = 0;
            foreach (char c in text)
            {
                if (c < 0x20 || c > 0x7E) continue;
                clean[length++] = c;
                if (length == BuildMaxBytes) break;
            }
            return length > 0 ? new string(clean[..length]) : null;
        }

        public byte[] Serialize()
        {
            var bytes = new List<byte>();
            bytes.Add(ProtoMajorMin);
            bytes.Add(ProtoMajorMax);
            Span<byte> caps = stackalloc byte[8];
            BinaryPrimitives.WriteUInt64LittleEndian(caps, Capabilities);
            bytes.AddRange(caps.ToArray());
            Span<byte> selfId = stackalloc byte[2];
            BinaryPrimitives.WriteUInt16LittleEndian(selfId, SelfAssignedId);
            bytes.AddRange(selfId.ToArray());
            bytes.Add(Result);
            Span<byte> count = stackalloc byte[2];
            BinaryPrimitives.WriteUInt16LittleEndian(count, (ushort)Offers.Count);
            bytes.AddRange(count.ToArray());
            foreach (var offer in Offers)
            {
                bytes.Add((byte)(offer.Internal ? 1 : 0));
                bytes.Add(offer.MessageClass);
                bytes.Add(offer.MinVersion);
                bytes.Add(offer.MaxVersion);
            }
            if (Names.Count > 0)
            {
                var names = new byte[Names.Count * NodeName.WireSize];
                for (int n = 0; n < Names.Count; n++)
                {
                    Names[n].WriteTo(names.AsSpan(n * NodeName.WireSize, NodeName.WireSize));
                }
                Tlv.Write(bytes, NamesTag, names);
            }
            string build = CleanBuild(Build);
            if (build != null)
            {
                Tlv.Write(bytes, BuildTag, Encoding.UTF8.GetBytes(build));
            }
            foreach (var kv in Extensions)
            {
                if (kv.Key != NamesTag && kv.Key != BuildTag) Tlv.Write(bytes, kv.Key, kv.Value);
            }
            return bytes.ToArray();
        }

        public static HandshakeMessage Deserialize(ReadOnlySpan<byte> src)
        {
            var msg = new HandshakeMessage();
            int i = 0;
            msg.ProtoMajorMin = src[i++];
            msg.ProtoMajorMax = src[i++];
            msg.Capabilities = BinaryPrimitives.ReadUInt64LittleEndian(src.Slice(i, 8)); i += 8;
            msg.SelfAssignedId = BinaryPrimitives.ReadUInt16LittleEndian(src.Slice(i, 2)); i += 2;
            msg.Result = src[i++];
            ushort count = BinaryPrimitives.ReadUInt16LittleEndian(src.Slice(i, 2)); i += 2;
            for (int n = 0; n < count; n++)
            {
                bool isInternal = src[i++] != 0;
                byte messageClass = src[i++];
                byte min = src[i++];
                byte max = src[i++];
                msg.Offers.Add(new SchemaOffer(isInternal, messageClass, min, max));
            }
            msg.Extensions = Tlv.ReadAll(src.Slice(i));
            if (msg.Extensions.Remove(NamesTag, out byte[] names) && names.Length > 0 && names.Length % NodeName.WireSize == 0)
            {
                for (int n = 0; n < names.Length; n += NodeName.WireSize)
                {
                    msg.Names.Add(NodeName.ReadFrom(names.AsSpan(n, NodeName.WireSize)));
                }
            }
            if (msg.Extensions.Remove(BuildTag, out byte[] build))
            {
                msg.Build = CleanBuild(Encoding.UTF8.GetString(build, 0, Math.Min(build.Length, BuildMaxBytes)));
            }
            return msg;
        }
    }

    /// <summary>
    /// Per-remote-peer negotiated state: which PeerId to use when addressing them, which PeerId they
    /// use when addressing us, and - the actual performance payoff - a flat per-message-class version
    /// table resolved ONCE when the handshake completes. The hot send/receive path never branches on
    /// version or capability again for the lifetime of the session with this peer: it just indexes
    /// AgreedAppVersion[MessageClasses.Position] (an O(1) array read) to know which codec to use.
    /// </summary>
    public sealed class PeerSession
    {
        /// <summary>The neighbor this session is with: the node whose datagrams carry
        /// <see cref="RemoteAssignedId"/>. Not necessarily the node a Hello was aimed at.</summary>
        public NodeId Peer;

        public ushort LocalAssignedId; // what WE call ourselves to this peer (goes in SenderPeerId when we send to them)
        public ushort RemoteAssignedId; // what THEY call themselves (goes in RecipientPeerId when we send to them)
        public ulong AgreedCapabilities;

        /// <summary>The build the peer said it runs in its last handshake message (<see cref="HandshakeMessage.Build"/>), or null.</summary>
        public string Build;

        /// <summary>A build was learned for this session (and logged at event level) at least once; later changes are logged at network level only.</summary>
        public bool BuildLearned;

        public readonly byte[] AgreedAppVersion = new byte[256];
        public readonly byte[] AgreedInternalVersion = new byte[256];

        /// <summary>
        /// We know the peer's id and schema offers (its Hello arrived, or it answered ours), so we can
        /// decode what it sends. Says nothing about whether OUR datagrams reach it - that is
        /// <see cref="Verified"/>.
        /// </summary>
        public bool HandshakeComplete;

        /// <summary>
        /// The endpoint at which the peer itself answered one of our Hellos, or null. Only a peer that
        /// answered can be sent to: receiving its Hello proves its datagrams reach us, not ours it.
        /// </summary>
        public IPEndPoint Endpoint;

        public bool Verified => Endpoint != null;

        /// <summary>IClock.Now of the last HelloAck from the peer; the session is dropped to unverified
        /// when it gets too old (see Jfp2Plugin's keepalive).</summary>
        public double LastAck;

        /// <summary>When the next keepalive Hello is due (verified sessions).</summary>
        public double NextKeepAlive;

        /// <summary>The endpoint the outstanding Hello was sent to; becomes <see cref="Endpoint"/> when answered.</summary>
        public IPEndPoint ProbeEndPoint;

        /// <summary>
        /// True once a Hello has gone unanswered past a short timeout (a few retransmits of the
        /// Hello, same cadence as the legacy Pulse retry loop) - this peer is treated as legacy-only
        /// until <see cref="RetryAt"/> and reached through the legacy plugin instead. No capability of
        /// this peer is ever assumed beyond what the legacy protocol already provides.
        /// </summary>
        public bool AssumedLegacy;

        /// <summary>When an <see cref="AssumedLegacy"/> peer is tried again (a peer may upgrade, or the route may change).</summary>
        public double RetryAt;

        /// <summary>
        /// How many Hello attempts have been made to this peer so far. Not part of the original
        /// reference implementation (which never drove a real retry loop) - added for
        /// Jfp2Plugin.DoHandshake's retry/AssumedLegacy-timeout logic.
        /// </summary>
        public int HelloAttempts;

        /// <summary>
        /// IClock.Now value at which Jfp2Plugin.DoHandshake should retry the Hello (or give
        /// up and set AssumedLegacy), mirroring the polled-interval idiom already used elsewhere in
        /// this codebase (see JoinFS.Timer) but tracked per-peer here instead of as a singleton.
        /// </summary>
        public double NextHelloAttempt;
    }

    public static class Negotiator
    {
        /// <summary>
        /// Combine a local and a remote offer set into a per-class agreed version table. A class
        /// either side never declared, or whose ranges do not overlap, gets version 0, which means
        /// "don't send this class to this peer" (docs/reference/jfp2-protocol.md §5.3): no codec has
        /// version 0, and the message goes through the legacy plugin instead - so an
        /// unrecognized/newer class on either side degrades gracefully instead of failing the whole
        /// handshake.
        ///
        /// Every handshake (keepalives included) resolves from scratch, so a class the peer stopped
        /// offering (it restarted with another build) is no longer agreed. Returns true when the result
        /// differs from the previous one.
        /// </summary>
        public static bool Resolve(PeerSession session, ulong localCapabilities, IEnumerable<SchemaOffer> localOffers,
                                    ulong remoteCapabilities, IEnumerable<SchemaOffer> remoteOffers)
        {
            Span<byte> previousApp = stackalloc byte[session.AgreedAppVersion.Length];
            Span<byte> previousInternal = stackalloc byte[session.AgreedInternalVersion.Length];
            session.AgreedAppVersion.CopyTo(previousApp);
            session.AgreedInternalVersion.CopyTo(previousInternal);
            ulong previousCapabilities = session.AgreedCapabilities;
            Array.Clear(session.AgreedAppVersion);
            Array.Clear(session.AgreedInternalVersion);

            session.AgreedCapabilities = localCapabilities & remoteCapabilities;

            var localByClass = new Dictionary<(bool Internal, byte Class), (byte Min, byte Max)>();
            foreach (var o in localOffers) localByClass[(o.Internal, o.MessageClass)] = (o.MinVersion, o.MaxVersion);
            var remoteByClass = new Dictionary<(bool Internal, byte Class), (byte Min, byte Max)>();
            foreach (var o in remoteOffers) remoteByClass[(o.Internal, o.MessageClass)] = (o.MinVersion, o.MaxVersion);

            var allKeys = new HashSet<(bool Internal, byte Class)>(localByClass.Keys);
            allKeys.UnionWith(remoteByClass.Keys);

            foreach (var key in allKeys)
            {
                localByClass.TryGetValue(key, out var l);
                remoteByClass.TryGetValue(key, out var r);
                byte lo = Math.Max(l.Min, r.Min);
                byte hi = Math.Min(l.Max, r.Max);
                byte agreed = hi >= lo ? hi : (byte)0;
                var table = key.Internal ? session.AgreedInternalVersion : session.AgreedAppVersion;
                table[key.Class] = agreed;
            }

            return session.AgreedCapabilities != previousCapabilities
                || !previousApp.SequenceEqual(session.AgreedAppVersion)
                || !previousInternal.SequenceEqual(session.AgreedInternalVersion);
        }
    }
}
