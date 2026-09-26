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
    /// legacy transport header's own "plenty of spare bits" headroom.
    /// </summary>
    [Flags]
    public enum EnvelopeFlags : byte
    {
        None = 0,
        /// <summary>This datagram wants acknowledgement/retransmission - see the 4-byte extended
        /// header appended right after the fixed 8 bytes whenever this bit is set.</summary>
        Guaranteed = 1 << 0,
        /// <summary>A 14-byte extension follows immediately after the (optional) Guaranteed
        /// extension: OriginNuid (7 bytes, who this really came from) then TargetNuid (7 bytes, who
        /// it's ultimately meant for) - always both, regardless of which leg of a relay hop this
        /// datagram is on. SenderPeerId/RecipientPeerId are HOP-scoped: they name the session
        /// between the two nodes that exchange THIS datagram (sender to relay, then relay to
        /// target), exactly as on a direct datagram, so the receiver finds the neighbor session by id
        /// and never by source endpoint. A relay rewrites the two ids (they sit at fixed offsets) when
        /// it passes the datagram on; OriginNuid/TargetNuid are the end-to-end addressing.
        ///
        /// Any node receiving a Forwarded datagram applies one uniform rule regardless of whether
        /// it's acting as the hub or the final recipient for this particular message (there is no
        /// separate "hub mode" - every node runs identical logic): if TargetNuid is this node's own
        /// Nuid, consume it, attributing the payload to OriginNuid instead of the physical sender's
        /// endpoint; otherwise, forward the datagram to TargetNuid, but only if
        /// TargetNuid is itself a direct neighbor of this node (RouteIsOwnEndPoint) - refuse (drop)
        /// otherwise. That direct-neighbor check is what caps relay at exactly one hop, matching the
        /// legacy mesh's own FLAG_FORWARD policy: a second hub would have to find its own direct
        /// route to TargetNuid, which by construction it doesn't have if the original sender needed
        /// this hub's relay in the first place. The payload is forwarded byte for byte only when the
        /// target agreed the same schema version as the origin's hop used; otherwise the relay
        /// decodes it and the core re-sends it in the target's own terms. See docs/reference/jfp2-protocol.md §7.7.
        ///
        /// A single Nuid field whose meaning flips by direction was considered and rejected: it
        /// leaves the receiving node unable to tell, from the datagram alone, whether it should relay
        /// further or consume the message, since in neither role does that lone field ever equal the
        /// receiver's own Nuid. Carrying both fields always removes the ambiguity at a modest fixed
        /// cost (14 bytes instead of 7, only ever paid when relay is actually happening).</summary>
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
        /// The legacy header's version constant (LegacyWire.Version, 0x520B written little-endian,
        /// so byte 0 on the wire is 0x0B) - duplicated here so the two plugins stay independent
        /// (docs/reference/jfp2-protocol.md §3).
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
        /// messages (see Jfp2Plugin.SendApplication) are never segmented, so
        /// GuaranteedIndex/Count are always 0/1 in practice - the fields exist because the wire format
        /// spec's §4.4 reserves room for segmentation the same way the legacy protocol's guaranteed
        /// messages support it, not because anything in this codebase currently segments a JFP2 message.
        /// </summary>
        public readonly ushort GuaranteedId;
        public readonly byte GuaranteedIndex;
        public readonly byte GuaranteedCount;

        /// <summary>Meaningful only when Flags.Forwarded is set - see EnvelopeFlags.Forwarded's doc
        /// comment. Default/unset otherwise.</summary>
        public readonly RelayNuid OriginNuid;

        /// <summary>Meaningful only when Flags.Forwarded is set - see EnvelopeFlags.Forwarded's doc
        /// comment. Default/unset otherwise.</summary>
        public readonly RelayNuid TargetNuid;

        public Envelope(EnvelopeFlags flags, ushort senderPeerId, ushort recipientPeerId, byte rawMessageClass)
            : this(flags, senderPeerId, recipientPeerId, rawMessageClass, 0, 0, 0, default, default)
        {
        }

        public Envelope(EnvelopeFlags flags, ushort senderPeerId, ushort recipientPeerId, byte rawMessageClass, ushort guaranteedId, byte guaranteedIndex, byte guaranteedCount)
            : this(flags, senderPeerId, recipientPeerId, rawMessageClass, guaranteedId, guaranteedIndex, guaranteedCount, default, default)
        {
        }

        /// <summary>Constructs a relayed envelope (Flags must include Forwarded). SenderPeerId/
        /// RecipientPeerId should be 0 - see EnvelopeFlags.Forwarded's doc comment.</summary>
        public Envelope(EnvelopeFlags flags, ushort senderPeerId, ushort recipientPeerId, byte rawMessageClass, ushort guaranteedId, byte guaranteedIndex, byte guaranteedCount, RelayNuid originNuid, RelayNuid targetNuid)
        {
            Flags = flags;
            SenderPeerId = senderPeerId;
            RecipientPeerId = recipientPeerId;
            RawMessageClass = rawMessageClass;
            GuaranteedId = guaranteedId;
            GuaranteedIndex = guaranteedIndex;
            GuaranteedCount = guaranteedCount;
            OriginNuid = originNuid;
            TargetNuid = targetNuid;
        }

        public bool IsInternal => (Flags & EnvelopeFlags.Internal) != 0;
        public bool IsGuaranteed => (Flags & EnvelopeFlags.Guaranteed) != 0;
        public bool IsForwarded => (Flags & EnvelopeFlags.Forwarded) != 0;

        /// <summary>Total header size on the wire for this envelope: the fixed 8 bytes, plus the 4-byte
        /// guaranteed-delivery extension when IsGuaranteed, plus the 14-byte Origin+TargetNuid
        /// extension when IsForwarded - the two extensions are independent and, when both present,
        /// appear in that order (Guaranteed's 4 bytes, then Origin+TargetNuid's 14), immediately
        /// before the payload.</summary>
        public int WireSize => FixedSize + (IsGuaranteed ? GuaranteedExtraSize : 0) + (IsForwarded ? RelayNuid.WireSize * 2 : 0);

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
            dest[1] = ProtoMajor;
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
                OriginNuid.WriteTo(dest.Slice(offset, RelayNuid.WireSize));
                offset += RelayNuid.WireSize;
                TargetNuid.WriteTo(dest.Slice(offset, RelayNuid.WireSize));
                offset += RelayNuid.WireSize;
            }
            return offset;
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

            RelayNuid originNuid = default;
            RelayNuid targetNuid = default;
            if ((flags & EnvelopeFlags.Forwarded) != 0)
            {
                if (src.Length < offset + RelayNuid.WireSize * 2)
                    throw new ArgumentException("datagram shorter than the JFP2 relay-addressing extension it claims to carry");
                originNuid = RelayNuid.ReadFrom(src.Slice(offset, RelayNuid.WireSize));
                offset += RelayNuid.WireSize;
                targetNuid = RelayNuid.ReadFrom(src.Slice(offset, RelayNuid.WireSize));
                offset += RelayNuid.WireSize;
            }

            bytesConsumed = offset;
            return new Envelope(flags, sender, recipient, msgClass, guaranteedId, guaranteedIndex, guaranteedCount, originNuid, targetNuid);
        }
    }

    /// <summary>
    /// One half (Origin or Target) of a relayed JFP2 datagram's addressing extension
    /// (EnvelopeFlags.Forwarded) - see that flag's doc comment for the full addressing scheme. Same
    /// 7-byte wire shape as the legacy transport's LocalNode.Nuid (ip: uint, port: ushort, local:
    /// byte, each little-endian - matching BinaryWriter's default, which is what LocalNode.Nuid.Write
    /// uses) so the two types are trivially interconvertible, but defined independently here rather
    /// than referencing LocalNode.Nuid directly, to avoid a reverse dependency from JoinFS.Jfp2 back
    /// into the top-level LocalNode type. Reusing the legacy Nuid's identity (rather than PeerKey, or
    /// a new hub-assigned id) needs no new synchronization: every mesh member already learns every
    /// other member's Nuid via the existing legacy Join/AddNode propagation, regardless of direct
    /// reachability, and JFP2 sessions are always layered on top of an already-established legacy
    /// Node entry - see docs/reference/jfp2-protocol.md §7.7.
    /// </summary>
    public readonly struct RelayNuid
    {
        public const int WireSize = 7;

        public readonly uint Ip;
        public readonly ushort Port;
        public readonly byte Local;

        public RelayNuid(uint ip, ushort port, byte local)
        {
            Ip = ip;
            Port = port;
            Local = local;
        }

        public void WriteTo(Span<byte> dest)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(dest.Slice(0, 4), Ip);
            BinaryPrimitives.WriteUInt16LittleEndian(dest.Slice(4, 2), Port);
            dest[6] = Local;
        }

        public static RelayNuid ReadFrom(ReadOnlySpan<byte> src)
        {
            uint ip = BinaryPrimitives.ReadUInt32LittleEndian(src.Slice(0, 4));
            ushort port = BinaryPrimitives.ReadUInt16LittleEndian(src.Slice(4, 2));
            byte local = src[6];
            return new RelayNuid(ip, port, local);
        }

        public override bool Equals(object obj) => obj is RelayNuid other && Ip == other.Ip && Port == other.Port && Local == other.Local;
        public override int GetHashCode() => System.HashCode.Combine(Ip, Port, Local);
        public static bool operator ==(RelayNuid left, RelayNuid right) => left.Equals(right);
        public static bool operator !=(RelayNuid left, RelayNuid right) => !left.Equals(right);
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
