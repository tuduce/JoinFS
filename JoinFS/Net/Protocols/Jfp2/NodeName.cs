using System;
using System.Buffers.Binary;

namespace JoinFS.Net.Jfp2
{
    /// <summary>
    /// How the JFP2 wire refers to a node (docs/reference/jfp2-protocol.md §4.9): 8 bytes, a kind byte
    /// then 7 bytes whose meaning the kind gives. It appears in the handshake's Names extension and,
    /// twice, in the Forwarded extension.
    /// - Kind 0: a legacy node id, in the legacy header's layout (ip u32 LE, port u16 LE, local u8).
    ///   The only kind this build sends or resolves.
    /// - Kind 1 (a random key), kind 2 (a key-pair id) and kind 255 (a group name) are reserved for
    ///   later builds; 3-254 are unassigned.
    ///
    /// All zero is "no node". Two names are equal when their 8 bytes are, whatever the kind, so a
    /// name is compared without being understood; resolving one (finding the peer it belongs to) is
    /// <see cref="TryGetLegacy"/>, which only kind 0 passes.
    /// </summary>
    public readonly struct NodeName : IEquatable<NodeName>
    {
        public const int WireSize = 8;

        /// <summary>The kind of a name that holds a legacy node id.</summary>
        public const byte LegacyKind = 0;

        /// <summary>The 8 wire bytes read as a little-endian u64: the kind is the low byte.</summary>
        readonly ulong bytes;

        NodeName(ulong bytes) => this.bytes = bytes;

        public byte Kind => (byte)bytes;

        /// <summary>All zero: no node.</summary>
        public bool IsNone => bytes == 0;

        /// <summary>The kind-0 name of a legacy node id.</summary>
        public static NodeName FromLegacy(NodeId id) => new(LegacyKind | (ulong)id.ip << 8 | (ulong)id.port << 40 | (ulong)id.local << 56);

        /// <summary>The legacy node id a kind-0 name holds; false for every other kind, which this build cannot resolve.</summary>
        public bool TryGetLegacy(out NodeId id)
        {
            if (Kind != LegacyKind)
            {
                id = default;
                return false;
            }
            id = new NodeId((uint)(bytes >> 8), (ushort)(bytes >> 40), (byte)(bytes >> 56));
            return true;
        }

        public void WriteTo(Span<byte> dest) => BinaryPrimitives.WriteUInt64LittleEndian(dest, bytes);

        public static NodeName ReadFrom(ReadOnlySpan<byte> src) => new(BinaryPrimitives.ReadUInt64LittleEndian(src));

        public bool Equals(NodeName other) => bytes == other.bytes;
        public override bool Equals(object obj) => obj is NodeName other && Equals(other);
        public override int GetHashCode() => bytes.GetHashCode();
        public static bool operator ==(NodeName left, NodeName right) => left.Equals(right);
        public static bool operator !=(NodeName left, NodeName right) => !left.Equals(right);

        /// <summary>A kind-0 name as its legacy node id; any other as its kind and its 7 value bytes in wire order, for the log.</summary>
        public override string ToString()
        {
            if (TryGetLegacy(out NodeId id))
            {
                return id.ToString();
            }
            Span<byte> wire = stackalloc byte[WireSize];
            WriteTo(wire);
            return "kind " + Kind + " " + Convert.ToHexString(wire[1..]);
        }
    }
}
