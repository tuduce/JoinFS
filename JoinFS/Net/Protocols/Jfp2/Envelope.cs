using System;
using System.Buffers.Binary;

// Ported from ProtocolV2Reference/Wire.cs (docs/reference/jfp2-protocol.md §4) as part of
// docs/protocol-v2-implementation-plan.md Phase 1. This is the fixed 8-byte JFP2 envelope that
// starts every JFP2 datagram, plus the message-class constants and the IPv4/IPv6 PeerKey payload
// type. Used by Jfp2Plugin (docs/network-plugin-architecture.md).

namespace JoinFS.Net.Jfp2
{
    /// <summary>
    /// Bit flags carried in every JFP2 envelope. Four bits reserved for future use, matching the
    /// legacy transport header's own "plenty of spare bits" headroom. A receiver drops a datagram
    /// with any bit outside <see cref="Envelope.SupportedFlags"/>, so a sender sets another bit only
    /// toward a neighbor that agreed the capability defining it, and never on Hello/HelloAck
    /// (docs/reference/jfp2-protocol.md §4.2).
    /// </summary>
    [Flags]
    public enum EnvelopeFlags : byte
    {
        None = 0,
        /// <summary>This datagram wants acknowledgement/retransmission - see the 4-byte extended
        /// header appended right after the fixed 8 bytes whenever this bit is set.</summary>
        Guaranteed = 1 << 0,
        /// <summary>A 16-byte extension follows immediately after the (optional) Guaranteed
        /// extension: Origin (an 8-byte <see cref="NodeName"/>, who this really came from) then Target
        /// (an 8-byte NodeName, who it's ultimately meant for) - always both, regardless of which leg
        /// of a relay hop this datagram is on. SenderPeerId/RecipientPeerId are HOP-scoped: they name
        /// the session between the two nodes that exchange THIS datagram (sender to relay, then relay
        /// to target), exactly as on a direct datagram, so the receiver finds the neighbor session by
        /// id and never by source endpoint. A relay rewrites the two ids (they sit at fixed offsets)
        /// when it passes the datagram on; Origin/Target are the end-to-end addressing, and a relay
        /// never rewrites them.
        ///
        /// Any node receiving a Forwarded datagram applies one uniform rule regardless of whether
        /// it's acting as the hub or the final recipient for this particular message (there is no
        /// separate "hub mode" - every node runs identical logic): if Target is one of this node's own
        /// names, consume it, attributing the payload to Origin instead of the physical sender's
        /// endpoint; otherwise, forward the datagram to Target, but only if
        /// Target is itself a direct neighbor of this node (RouteIsOwnEndPoint) - refuse (drop)
        /// otherwise. That direct-neighbor check is what caps relay at exactly one hop, matching the
        /// legacy mesh's own FLAG_FORWARD policy: a second hub would have to find its own direct
        /// route to Target, which by construction it doesn't have if the original sender needed
        /// this hub's relay in the first place. The payload is forwarded byte for byte only when the
        /// target agreed the same schema version as the origin's hop used; otherwise the relay
        /// decodes it and the core re-sends it in the target's own terms. See docs/reference/jfp2-protocol.md §7.7.
        /// A name this build cannot resolve (any kind but 0) is dropped, and never acknowledged.
        ///
        /// A single name field whose meaning flips by direction was considered and rejected: it
        /// leaves the receiving node unable to tell, from the datagram alone, whether it should relay
        /// further or consume the message, since in neither role does that lone field ever equal the
        /// receiver's own name. Carrying both fields always removes the ambiguity at a modest fixed
        /// cost (16 bytes instead of 8, only ever paid when relay is actually happening).</summary>
        Forwarded = 1 << 1,
        /// <summary>Payload is a sequence of coalesced sub-messages (each prefixed with its own
        /// MessageClass byte and a u16 length) rather than a single message body. Specified, not
        /// implemented: no build sends it, and this one drops a datagram that carries it.</summary>
        Coalesced = 1 << 2,
        /// <summary>RawMessageClass indexes the internal/session-management partition rather than
        /// the application partition - see MessageClasses.</summary>
        Internal = 1 << 3,
    }

    /// <summary>
    /// Message class identifiers. Every value is an explicit constant - never implicit declaration
    /// order, unlike the legacy MESSAGE_ID enum - so reordering source lines or inserting a new class
    /// can never silently renumber another, already-shipped class. The same numeric space (0-254,
    /// with 255 reserved as an escape hatch) is reused for both the Internal and Application
    /// partitions; EnvelopeFlags.Internal says which partition a given envelope's RawMessageClass
    /// indexes into. Always append a new class at the next unused number in the right partition;
    /// never renumber or reuse a value that has ever shipped.
    /// </summary>
    public static class MessageClasses
    {
        // -- Internal / session-management partition (EnvelopeFlags.Internal set) --
        public const byte Hello = 0;
        public const byte HelloAck = 1;
        public const byte Join = 2;
        public const byte JoinReply = 3;
        public const byte Leave = 4;
        public const byte Pulse = 5;
        public const byte PulseResponse = 6;
        public const byte Pathfinder = 7;
        public const byte PathfinderResponse = 8;
        public const byte GuaranteedDone = 9;

        // -- Application partition (EnvelopeFlags.Internal clear) --
        public const byte Position = 0;
        public const byte Identity = 1;
        public const byte VariableSync = 2;
        public const byte Event = 3;
        public const byte FlightPlan = 4;
        public const byte Notes = 5;
        public const byte Weather = 6;
        public const byte Status = 7;
        /// <summary>
        /// docs/protocol-v2-implementation-plan.md Phase 2's addition: the design doc's message
        /// catalog (§4.3) only reserved one slot ("Status") for this whole exchange, but the legacy
        /// protocol has two distinct messages here (StatusRequest and Status - network-protocol.md
        /// §8.6) with different shapes. Rather than overload one class with a discriminator field,
        /// this follows the catalog's own stated evolution rule ("always append a new class at the
        /// next unused number") and gives the request its own slot, mirroring Hello/HelloAck's split
        /// in the internal partition.
        /// </summary>
        public const byte StatusRequest = 8;
        /// <summary>
        /// docs/protocol-v2-implementation-plan.md Phase 5's addition, same reasoning as
        /// StatusRequest above: the design catalog reserved one slot ("Weather") for the legacy
        /// WeatherReply/WeatherUpdate pair, which share a wire shape ({ Metar: string }) but need
        /// independent negotiation (different reliability/receive semantics - see
        /// JoinFS/Jfp2/Codecs/WeatherCodec.cs). `Weather` (6) serves WeatherUpdate (broadcast, peer
        /// weather); this appended slot serves WeatherReply (unicast reply to a request). WeatherRequest
        /// itself is not ported - see the implementation plan.
        /// </summary>
        public const byte WeatherReply = 9;

        /// <summary>
        /// A class byte of 255 in either partition means "the real class id is a two-byte little-
        /// endian value immediately following this byte" - headroom past 255 classes per partition
        /// without ever widening the fixed 8-byte header for the other 255 already in daily use.
        /// </summary>
        public const byte Extended = 255;
    }

    /// <summary>
    /// The fixed 8-byte JFP2 envelope that starts every JFP2 datagram. An additional 4-byte block
    /// (GuaranteedId: ushort, GuaranteedIndex: byte, GuaranteedCount: byte) follows immediately when
    /// EnvelopeFlags.Guaranteed is set - unreliable traffic (Position, VariableSync, and everything
    /// else on the hot path) never allocates or transmits those bytes at all.
    ///
    /// Compare to the legacy transport header (docs/network-protocol.md §2): 21 fixed bytes, always
    /// present, including 14 bytes of Sender+Recipient Nuid and 4 bytes of guaranteed-delivery fields
    /// whether or not the message is guaranteed. JFP2's fixed cost for the common (unreliable,
    /// unicast-or-broadcast) case is 8 bytes - a 62% reduction before a single payload byte is sent.
    /// </summary>
    public readonly struct Envelope
    {
        public const byte Magic = 0xFA;
        public const byte ProtoMajor = 2;

        /// <summary>
        /// The ProtoMajor Hello and HelloAck always travel with, whatever <see cref="ProtoMajor"/>
        /// becomes. The handshake is the permanent entry point: its envelope and fixed fields never
        /// change, so every build ever released can start one with every later build, and a later
        /// major version is agreed inside it (HandshakeMessage.ProtoMajorMin/Max) rather than by
        /// changing it (docs/reference/jfp2-protocol.md §5.2).
        /// </summary>
        public const byte HandshakeProtoMajor = 2;

        /// <summary>
        /// The flags this build can read. Any other bit may announce a header extension or a payload
        /// framing this build does not know, which would shift what follows, so a datagram carrying one
        /// is dropped rather than misparsed (docs/reference/jfp2-protocol.md §4.2). Coalesced is
        /// specified but not implemented, so it is not here.
        /// </summary>
        public const EnvelopeFlags SupportedFlags = EnvelopeFlags.Guaranteed | EnvelopeFlags.Forwarded | EnvelopeFlags.Internal;

        public const int FixedSize = 8;
        public const int GuaranteedExtraSize = 4;

        /// <summary>The Forwarded extension: the origin's and the target's <see cref="NodeName"/>.</summary>
        public const int ForwardedExtraSize = NodeName.WireSize * 2;

        /// <summary>
        /// The legacy header's version constant (LegacyWire.Version, 0x520B written little-endian,
        /// so byte 0 on the wire is 0x0B) - duplicated here so the two plugins stay independent
        /// (docs/reference/jfp2-protocol.md §3).
        /// </summary>
        public const ushort LegacyVersionConstant = 0x520B;

        public readonly EnvelopeFlags Flags;
        public readonly ushort SenderPeerId;
        public readonly ushort RecipientPeerId; // 0 = not known yet: the first Hello to a peer, before its id is learned. No session has id 0, so nothing else is addressed by it
        public readonly byte RawMessageClass;

        /// <summary>
        /// Guaranteed-delivery extension fields (§4.4) - meaningful only when Flags.Guaranteed is set,
        /// in which case WriteTo/ReadFrom read/write 4 extra bytes immediately after the fixed 8-byte
        /// header: GuaranteedId (u16), GuaranteedIndex (u8), GuaranteedCount (u8). JFP2's guaranteed
        /// messages are not segmented yet (Jfp2Reliability.Send), so GuaranteedIndex/Count are always
        /// 0/1 in practice - §4.4 reserves them for segmentation, like the legacy protocol's.
        /// </summary>
        public readonly ushort GuaranteedId;
        public readonly byte GuaranteedIndex;
        public readonly byte GuaranteedCount;

        /// <summary>Meaningful only when Flags.Forwarded is set - see EnvelopeFlags.Forwarded's doc
        /// comment. Default/unset otherwise.</summary>
        public readonly NodeName Origin;

        /// <summary>Meaningful only when Flags.Forwarded is set - see EnvelopeFlags.Forwarded's doc
        /// comment. Default/unset otherwise.</summary>
        public readonly NodeName Target;

        public Envelope(EnvelopeFlags flags, ushort senderPeerId, ushort recipientPeerId, byte rawMessageClass)
            : this(flags, senderPeerId, recipientPeerId, rawMessageClass, 0, 0, 0, default, default)
        {
        }

        public Envelope(EnvelopeFlags flags, ushort senderPeerId, ushort recipientPeerId, byte rawMessageClass, ushort guaranteedId, byte guaranteedIndex, byte guaranteedCount)
            : this(flags, senderPeerId, recipientPeerId, rawMessageClass, guaranteedId, guaranteedIndex, guaranteedCount, default, default)
        {
        }

        /// <summary>Constructs a relayed envelope (Flags must include Forwarded). SenderPeerId/
        /// RecipientPeerId stay hop-scoped, as on any datagram (the session with the neighbor it goes
        /// to); Origin/Target are end to end - see EnvelopeFlags.Forwarded's doc comment.</summary>
        public Envelope(EnvelopeFlags flags, ushort senderPeerId, ushort recipientPeerId, byte rawMessageClass, ushort guaranteedId, byte guaranteedIndex, byte guaranteedCount, NodeName origin, NodeName target)
        {
            Flags = flags;
            SenderPeerId = senderPeerId;
            RecipientPeerId = recipientPeerId;
            RawMessageClass = rawMessageClass;
            GuaranteedId = guaranteedId;
            GuaranteedIndex = guaranteedIndex;
            GuaranteedCount = guaranteedCount;
            Origin = origin;
            Target = target;
        }

        public bool IsInternal => (Flags & EnvelopeFlags.Internal) != 0;
        public bool IsGuaranteed => (Flags & EnvelopeFlags.Guaranteed) != 0;
        public bool IsForwarded => (Flags & EnvelopeFlags.Forwarded) != 0;

        /// <summary>Total header size on the wire for this envelope: the fixed 8 bytes, plus the 4-byte
        /// guaranteed-delivery extension when IsGuaranteed, plus the 16-byte Origin+Target
        /// extension when IsForwarded - the two extensions are independent and, when both present,
        /// appear in that order (Guaranteed's 4 bytes, then Origin+Target's 16), immediately
        /// before the payload.</summary>
        public int WireSize => FixedSize + (IsGuaranteed ? GuaranteedExtraSize : 0) + (IsForwarded ? ForwardedExtraSize : 0);

        /// <summary>
        /// True if the first two bytes of a received datagram are the legacy magic (0x520B, written
        /// little-endian, so byte 0 on the wire is 0x0B). A JFP2 datagram's byte 0 is always 0xFA,
        /// which can never collide with the legacy constant's fixed byte 0 - so a single byte compare
        /// routes an incoming datagram to the right decoder before anything else is parsed, and both
        /// stacks can share one UDP socket/port. The production dispatch (NetworkCore.OnDatagram asking
        /// each plugin's Accepts) only needs the single-byte magic check; this
        /// helper exists mainly for tests/symmetry with the reference implementation.
        /// </summary>
        public static bool IsLegacyDatagram(ReadOnlySpan<byte> datagram) =>
            datagram.Length >= 2 && BinaryPrimitives.ReadUInt16LittleEndian(datagram) == LegacyVersionConstant;

        public static bool IsJfp2Datagram(ReadOnlySpan<byte> datagram) =>
            datagram.Length >= 1 && datagram[0] == Magic;

        public int WriteTo(Span<byte> dest)
        {
            if (dest.Length < WireSize)
                throw new ArgumentException("destination buffer smaller than the JFP2 header (fixed + guaranteed/relay extensions, if any)");
            dest[0] = Magic;
            dest[1] = ProtoMajorFor(Flags, RawMessageClass);
            dest[2] = (byte)Flags;
            BinaryPrimitives.WriteUInt16LittleEndian(dest.Slice(3, 2), SenderPeerId);
            BinaryPrimitives.WriteUInt16LittleEndian(dest.Slice(5, 2), RecipientPeerId);
            dest[7] = RawMessageClass;
            int offset = FixedSize;
            if (IsGuaranteed)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(dest.Slice(offset, 2), GuaranteedId);
                dest[offset + 2] = GuaranteedIndex;
                dest[offset + 3] = GuaranteedCount;
                offset += GuaranteedExtraSize;
            }
            if (IsForwarded)
            {
                Origin.WriteTo(dest.Slice(offset, NodeName.WireSize));
                offset += NodeName.WireSize;
                Target.WriteTo(dest.Slice(offset, NodeName.WireSize));
                offset += NodeName.WireSize;
            }
            return offset;
        }

        /// <summary>
        /// The ProtoMajor a datagram is written with: <see cref="HandshakeProtoMajor"/> for Hello/HelloAck,
        /// <see cref="ProtoMajor"/> for everything else (the same value today). Once a second major
        /// version exists, a non-handshake datagram's major comes from the version agreed with that
        /// neighbor in the handshake (per session, not per class), not from a constant.
        /// </summary>
        static byte ProtoMajorFor(EnvelopeFlags flags, byte rawMessageClass) =>
            (flags & EnvelopeFlags.Internal) != 0 && (rawMessageClass == MessageClasses.Hello || rawMessageClass == MessageClasses.HelloAck)
                ? HandshakeProtoMajor
                : ProtoMajor;

        /// <summary>Read the envelope of a datagram this build can read; throws on any other (see <see cref="TryReadFrom"/>).</summary>
        public static Envelope ReadFrom(ReadOnlySpan<byte> src, out int bytesConsumed) =>
            TryReadFrom(src, out Envelope envelope, out bytesConsumed, out string unsupported)
                ? envelope
                : throw new InvalidOperationException("JFP2 datagram this build cannot read: " + unsupported);

        /// <summary>
        /// Read the envelope of a received JFP2 datagram, or return false, saying why in
        /// <paramref name="unsupported"/> (for the log), when this build must not read it: another
        /// ProtoMajor (a later major version, whose header need not look like this one), or a flag
        /// outside <see cref="SupportedFlags"/> (it may shift the payload). Such a datagram is well
        /// formed for the build that sent it, so it is not an error; it is simply dropped. A datagram
        /// too short for what it claims to carry still throws.
        /// </summary>
        public static bool TryReadFrom(ReadOnlySpan<byte> src, out Envelope envelope, out int bytesConsumed, out string unsupported)
        {
            envelope = default;
            bytesConsumed = 0;
            unsupported = null;
            if (src.Length < 2)
                throw new ArgumentException("datagram shorter than the JFP2 fixed header");
            if (src[0] != Magic)
                throw new InvalidOperationException("not a JFP2 datagram (bad magic byte)");
            // src[1] (ProtoMajor) is where a later breaking version branches to its own header layout,
            // so nothing past it is read unless it is one this build knows
            byte major = src[1];
            if (major != ProtoMajor && major != HandshakeProtoMajor)
            {
                unsupported = "ProtoMajor " + major;
                return false;
            }
            if (src.Length < FixedSize)
                throw new ArgumentException("datagram shorter than the JFP2 fixed header");
            var flags = (EnvelopeFlags)src[2];
            ushort sender = BinaryPrimitives.ReadUInt16LittleEndian(src.Slice(3, 2));
            ushort recipient = BinaryPrimitives.ReadUInt16LittleEndian(src.Slice(5, 2));
            byte msgClass = src[7];
            if ((flags & ~SupportedFlags) != 0)
            {
                unsupported = "flag bits 0x" + ((byte)(flags & ~SupportedFlags)).ToString("X2");
                return false;
            }

            int offset = FixedSize;
            ushort guaranteedId = 0;
            byte guaranteedIndex = 0;
            byte guaranteedCount = 0;
            if ((flags & EnvelopeFlags.Guaranteed) != 0)
            {
                if (src.Length < offset + GuaranteedExtraSize)
                    throw new ArgumentException("datagram shorter than the JFP2 guaranteed-delivery extension it claims to carry");
                guaranteedId = BinaryPrimitives.ReadUInt16LittleEndian(src.Slice(offset, 2));
                guaranteedIndex = src[offset + 2];
                guaranteedCount = src[offset + 3];
                offset += GuaranteedExtraSize;
            }

            NodeName origin = default;
            NodeName target = default;
            if ((flags & EnvelopeFlags.Forwarded) != 0)
            {
                if (src.Length < offset + ForwardedExtraSize)
                    throw new ArgumentException("datagram shorter than the JFP2 relay-addressing extension it claims to carry");
                origin = NodeName.ReadFrom(src.Slice(offset, NodeName.WireSize));
                offset += NodeName.WireSize;
                target = NodeName.ReadFrom(src.Slice(offset, NodeName.WireSize));
                offset += NodeName.WireSize;
            }

            bytesConsumed = offset;
            envelope = new Envelope(flags, sender, recipient, msgClass, guaranteedId, guaranteedIndex, guaranteedCount, origin, target);
            return true;
        }
    }

    /// <summary>
    /// A self-describing peer address, used inside PAYLOADS that need to describe a peer other than
    /// the immediate sender - membership lists (the JoinReply equivalent), Pathfinder targets, hub
    /// lists. It is deliberately never part of the hot envelope itself, which only ever carries small
    /// negotiated PeerIds (see PeerSession). Supporting IPv6 here is purely additive: an existing
    /// reader that only knows Family==4 entries can still correctly skip over a Family==6 entry it
    /// doesn't care about, because the entry declares its own size - this directly fixes the legacy
    /// protocol's IPv4-only Nuid limitation (docs/network-protocol.md §9.5) without requiring a
    /// mesh-wide flag day, since it's an additive payload concern, not a framing concern.
    /// Not yet used anywhere in Phase 1 - reserved for the membership/Pathfinder-equivalent messages
    /// added in a later phase.
    /// </summary>
    public readonly struct PeerKey
    {
        public readonly byte Family; // 4 = IPv4, 6 = IPv6
        public readonly byte[] Address; // 4 or 16 bytes, network byte order
        public readonly ushort Port;
        public readonly byte Local; // last octet of the LAN address - disambiguates instances behind one NAT, same role as the legacy Nuid.local field

        public PeerKey(byte family, byte[] address, ushort port, byte local)
        {
            Family = family;
            Address = address;
            Port = port;
            Local = local;
        }

        public int WireSize => 1 + Address.Length + 2 + 1;

        public int WriteTo(Span<byte> dest)
        {
            int i = 0;
            dest[i++] = Family;
            Address.AsSpan().CopyTo(dest.Slice(i));
            i += Address.Length;
            BinaryPrimitives.WriteUInt16LittleEndian(dest.Slice(i, 2), Port);
            i += 2;
            dest[i++] = Local;
            return i;
        }

        public static PeerKey ReadFrom(ReadOnlySpan<byte> src, out int bytesConsumed)
        {
            byte family = src[0];
            int addrLen = family == 6 ? 16 : 4;
            byte[] address = src.Slice(1, addrLen).ToArray();
            ushort port = BinaryPrimitives.ReadUInt16LittleEndian(src.Slice(1 + addrLen, 2));
            byte local = src[1 + addrLen + 2];
            bytesConsumed = 1 + addrLen + 2 + 1;
            return new PeerKey(family, address, port, local);
        }
    }
}
