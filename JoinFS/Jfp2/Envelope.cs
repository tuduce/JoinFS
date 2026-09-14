using System;
using System.Buffers.Binary;

// Ported from ProtocolV2Reference/Wire.cs (docs/protocol-v2-design.md §4) as part of
// docs/protocol-v2-implementation-plan.md Phase 1. This is the fixed 8-byte JFP2 envelope that
// starts every JFP2 datagram, plus the message-class constants and the IPv4/IPv6 PeerKey payload
// type. See docs/protocol-v2-architecture.md for how this plugs into JoinFS/Node.cs.

namespace JoinFS.Jfp2
{
    /// <summary>
    /// Bit flags carried in every JFP2 envelope. Four bits reserved for future use, matching the
    /// legacy transport header's own "plenty of spare bits" headroom.
    /// </summary>
    [Flags]
    public enum EnvelopeFlags : byte
    {
        None = 0,
        /// <summary>This datagram wants acknowledgement/retransmission - see the 4-byte extended
        /// header appended right after the fixed 8 bytes whenever this bit is set.</summary>
        Guaranteed = 1 << 0,
        /// <summary>Already relayed once by an intermediate mesh node (one-hop relay, same idea as
        /// the legacy FLAG_FORWARD).</summary>
        Forwarded = 1 << 1,
        /// <summary>Payload is a sequence of coalesced sub-messages (each prefixed with its own
        /// MessageClass byte and a u16 length) rather than a single message body.</summary>
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
        public const int FixedSize = 8;
        public const int GuaranteedExtraSize = 4;

        /// <summary>
        /// LocalNode.VERSION from the legacy stack (the fixed 0x520B constant, written little-endian
        /// by BinaryWriter, so byte 0 on the wire is 0x0B) - duplicated here as a plain constant
        /// rather than referencing the private LocalNode.VERSION field, since the two stacks are
        /// deliberately independent (docs/protocol-v2-design.md §3).
        /// </summary>
        public const ushort LegacyVersionConstant = 0x520B;

        public readonly EnvelopeFlags Flags;
        public readonly ushort SenderPeerId;
        public readonly ushort RecipientPeerId; // 0 = broadcast to the whole mesh, matching the legacy null-Nuid convention
        public readonly byte RawMessageClass;

        /// <summary>
        /// Guaranteed-delivery extension fields (§4.4) - meaningful only when Flags.Guaranteed is set,
        /// in which case WriteTo/ReadFrom read/write 4 extra bytes immediately after the fixed 8-byte
        /// header: GuaranteedId (u16), GuaranteedIndex (u8), GuaranteedCount (u8). JFP2's guaranteed
        /// messages (see LocalNode.SendJfp2Application's `guaranteed` overload) are never segmented, so
        /// GuaranteedIndex/Count are always 0/1 in practice - the fields exist because the wire format
        /// spec's §4.4 reserves room for segmentation the same way the legacy protocol's guaranteed
        /// messages support it, not because anything in this codebase currently segments a JFP2 message.
        /// </summary>
        public readonly ushort GuaranteedId;
        public readonly byte GuaranteedIndex;
        public readonly byte GuaranteedCount;

        public Envelope(EnvelopeFlags flags, ushort senderPeerId, ushort recipientPeerId, byte rawMessageClass)
            : this(flags, senderPeerId, recipientPeerId, rawMessageClass, 0, 0, 0)
        {
        }

        public Envelope(EnvelopeFlags flags, ushort senderPeerId, ushort recipientPeerId, byte rawMessageClass, ushort guaranteedId, byte guaranteedIndex, byte guaranteedCount)
        {
            Flags = flags;
            SenderPeerId = senderPeerId;
            RecipientPeerId = recipientPeerId;
            RawMessageClass = rawMessageClass;
            GuaranteedId = guaranteedId;
            GuaranteedIndex = guaranteedIndex;
            GuaranteedCount = guaranteedCount;
        }

        public bool IsInternal => (Flags & EnvelopeFlags.Internal) != 0;
        public bool IsGuaranteed => (Flags & EnvelopeFlags.Guaranteed) != 0;

        /// <summary>Total header size on the wire for this envelope: the fixed 8 bytes, plus the 4-byte
        /// guaranteed-delivery extension when IsGuaranteed.</summary>
        public int WireSize => FixedSize + (IsGuaranteed ? GuaranteedExtraSize : 0);

        /// <summary>
        /// True if the first two bytes of a received datagram are the legacy magic (0x520B, written
        /// little-endian, so byte 0 on the wire is 0x0B). A JFP2 datagram's byte 0 is always 0xFA,
        /// which can never collide with the legacy constant's fixed byte 0 - so a single byte compare
        /// routes an incoming datagram to the right decoder before anything else is parsed, and both
        /// stacks can share one UDP socket/port. See LocalNode.ReceiveMessages for the actual
        /// production dispatch, which only needs the single-byte magic check (IsJfp2Datagram); this
        /// helper exists mainly for tests/symmetry with the reference implementation.
        /// </summary>
        public static bool IsLegacyDatagram(ReadOnlySpan<byte> datagram) =>
            datagram.Length >= 2 && BinaryPrimitives.ReadUInt16LittleEndian(datagram) == LegacyVersionConstant;

        public static bool IsJfp2Datagram(ReadOnlySpan<byte> datagram) =>
            datagram.Length >= 1 && datagram[0] == Magic;

        public int WriteTo(Span<byte> dest)
        {
            if (dest.Length < WireSize)
                throw new ArgumentException("destination buffer smaller than the JFP2 header (fixed + guaranteed extension, if any)");
            dest[0] = Magic;
            dest[1] = ProtoMajor;
            dest[2] = (byte)Flags;
            BinaryPrimitives.WriteUInt16LittleEndian(dest.Slice(3, 2), SenderPeerId);
            BinaryPrimitives.WriteUInt16LittleEndian(dest.Slice(5, 2), RecipientPeerId);
            dest[7] = RawMessageClass;
            if (!IsGuaranteed)
                return FixedSize;
            BinaryPrimitives.WriteUInt16LittleEndian(dest.Slice(FixedSize, 2), GuaranteedId);
            dest[FixedSize + 2] = GuaranteedIndex;
            dest[FixedSize + 3] = GuaranteedCount;
            return WireSize;
        }

        public static Envelope ReadFrom(ReadOnlySpan<byte> src, out int bytesConsumed)
        {
            if (src.Length < FixedSize)
                throw new ArgumentException("datagram shorter than the JFP2 fixed header");
            if (src[0] != Magic)
                throw new InvalidOperationException("not a JFP2 datagram (bad magic byte)");
            // src[1] (ProtoMajor) is where a future breaking redesign (JFP3) would branch to a
            // completely different header layout; this only implements ProtoMajor 2.
            var flags = (EnvelopeFlags)src[2];
            ushort sender = BinaryPrimitives.ReadUInt16LittleEndian(src.Slice(3, 2));
            ushort recipient = BinaryPrimitives.ReadUInt16LittleEndian(src.Slice(5, 2));
            byte msgClass = src[7];

            if ((flags & EnvelopeFlags.Guaranteed) == 0)
            {
                bytesConsumed = FixedSize;
                return new Envelope(flags, sender, recipient, msgClass);
            }

            if (src.Length < FixedSize + GuaranteedExtraSize)
                throw new ArgumentException("datagram shorter than the JFP2 guaranteed-delivery extension it claims to carry");
            ushort guaranteedId = BinaryPrimitives.ReadUInt16LittleEndian(src.Slice(FixedSize, 2));
            byte guaranteedIndex = src[FixedSize + 2];
            byte guaranteedCount = src[FixedSize + 3];
            bytesConsumed = FixedSize + GuaranteedExtraSize;
            return new Envelope(flags, sender, recipient, msgClass, guaranteedId, guaranteedIndex, guaranteedCount);
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
