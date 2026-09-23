using System;
using System.Buffers.Binary;
using System.Collections.Generic;

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
    /// anticipated by the fixed Hello layout (a future auth token, a build string for diagnostics, a
    /// vendor-specific extension, ...) can be added here later without changing how any existing field
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
    /// </summary>
    public sealed class HandshakeMessage
    {
        public byte ProtoMajorMin;
        public byte ProtoMajorMax;
        public ulong Capabilities;
        public ushort SelfAssignedId; // the PeerId the sender wants to be addressed by from now on
        public List<SchemaOffer> Offers = new();
        public byte Result; // HelloAck only: 0 = Accepted, 1 = NoCompatibleProtoMajor. Ignored on Hello.
        public Dictionary<ushort, byte[]> Extensions = new();

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
            foreach (var kv in Extensions)
            {
                Tlv.Write(bytes, kv.Key, kv.Value);
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
        public ushort LocalAssignedId; // what WE call ourselves to this peer (goes in SenderPeerId when we send to them)
        public ushort RemoteAssignedId; // what THEY call themselves (goes in RecipientPeerId when we send to them)
        public ulong AgreedCapabilities;
        public readonly byte[] AgreedAppVersion = new byte[256];
        public readonly byte[] AgreedInternalVersion = new byte[256];
        public bool HandshakeComplete;

        /// <summary>
        /// True once a Hello has gone unanswered past a short timeout (a few retransmits of the
        /// Hello, same cadence as the legacy Pulse retry loop) - this peer is treated as legacy-only
        /// for the rest of the session and reached through the legacy plugin instead. No capability of this peer is ever assumed beyond what the legacy protocol
        /// already provides.
        /// </summary>
        public bool AssumedLegacy;

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
        /// either side never declared falls back to version 0, which by convention is always a
        /// baseline schema every build understands (roughly the wire-compatible equivalent of what
        /// the legacy protocol already carried for that concept) - so an unrecognized/newer class on
        /// either side degrades gracefully instead of failing the whole handshake.
        /// </summary>
        public static void Resolve(PeerSession session, ulong localCapabilities, IEnumerable<SchemaOffer> localOffers,
                                    ulong remoteCapabilities, IEnumerable<SchemaOffer> remoteOffers)
        {
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
        }
    }
}
