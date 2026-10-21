namespace JoinFS.Net.Jfp2
{
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
        // 7 and 8 are free: they were reserved for a mesh whose message set the mesh over JFP2 designs
        // afresh, and are assigned by that design (docs/network/jfp2/protocol.md §3.3).
        public const byte Hello = 0;
        public const byte HelloAck = 1;
        /// <summary>A node in a non-member session asks to become a member (§5.10).</summary>
        public const byte Join = 2;
        /// <summary>The admitting node's answer to a Join (§5.10).</summary>
        public const byte JoinResult = 3;
        /// <summary>Members the admitting node knows, for a node that just joined (§5.10).</summary>
        public const byte MemberList = 4;
        /// <summary>A node tells its neighbours of a member that just joined (§5.11).</summary>
        public const byte MemberAnnounce = 5;
        /// <summary>A node tells its neighbours that a member is gone (§5.11).</summary>
        public const byte MemberLeave = 6;
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
        /// docs/network/jfp2/history/protocol-v2-implementation-plan.md Phase 2's addition: the design doc's message
        /// catalog (§4.3) only reserved one slot ("Status") for this whole exchange, but the legacy
        /// protocol has two distinct messages here (StatusRequest and Status - network-protocol.md
        /// §9.8) with different shapes. Rather than overload one class with a discriminator field,
        /// this follows the catalog's own stated evolution rule ("always append a new class at the
        /// next unused number") and gives the request its own slot, mirroring Hello/HelloAck's split
        /// in the internal partition.
        /// </summary>
        public const byte StatusRequest = 8;
        /// <summary>
        /// docs/network/jfp2/history/protocol-v2-implementation-plan.md Phase 5's addition, same reasoning as
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
        /// Reserved, never assigned, and not implemented: a class beyond 255 would also need an offer
        /// form other than the u8 of an offer entry, and a sender sends only classes the receiver
        /// offered, so this build never receives one.
        /// </summary>
        public const byte Extended = 255;
    }
}
