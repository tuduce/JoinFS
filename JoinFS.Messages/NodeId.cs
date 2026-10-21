using System;
using System.Buffers.Binary;
using System.Net;

namespace JoinFS.Net
{
    /// <summary>
    /// Identifies a JoinFS node in the core and the app: the 8-byte name JFP2 puts on the wire
    /// (docs/network/jfp2/protocol.md §4.3), a kind byte then 7 bytes whose meaning the kind gives.
    /// - Kind 0: a legacy node id (<see cref="LegacyNodeId"/>), in the legacy header's layout (ip u32 LE,
    ///   port u16 LE, local u8). <see cref="FromLegacy(LegacyNodeId)"/> and <see cref="TryGetLegacy"/>
    ///   convert both ways without a table.
    /// - Kind 2: a key-pair id (D3 of the stand-alone plan). Kind 1 (a random key) is not used; kind 255 (a group name) is
    ///   reserved; 3-254 are unassigned.
    ///
    /// All zero is "no node". Two ids are equal when their 8 bytes are, whatever the kind, so an id is
    /// compared without being understood. A peer's id is the name it was first learned by and is never
    /// re-keyed; its other names are aliases (PeerDirectory).
    /// </summary>
    public readonly struct NodeId : IEquatable<NodeId>
    {
        public const int WireSize = 8;

        /// <summary>The kind of a name that holds a legacy node id.</summary>
        public const byte LegacyKind = 0;

        /// <summary>The 8 wire bytes read as a little-endian u64: the kind is the low byte.</summary>
        readonly ulong bytes;

        NodeId(ulong bytes) => this.bytes = bytes;

        public byte Kind => (byte)bytes;

        /// <summary>The kind of a name that holds the id of a key pair.</summary>
        public const byte KeyPairKind = 2;

        /// <summary>All zero: no node.</summary>
        public bool IsNone => bytes == 0;

        /// <summary>Not "no node", and for kind 0 a non-zero ip (a legacy id without an address is no node).</summary>
        public bool Valid() => bytes != 0 && (Kind != LegacyKind || (uint)(bytes >> 8) != 0);
        public bool Invalid() => !Valid();

        /// <summary>The kind-0 id of a legacy node id.</summary>
        public static NodeId FromLegacy(LegacyNodeId id) => FromLegacy(id.ip, id.port, id.local);

        public static NodeId FromLegacy(uint ip, ushort port, byte local) => new(LegacyKind | (ulong)ip << 8 | (ulong)port << 40 | (ulong)local << 56);

        public static NodeId FromLegacy(IPAddress address, ushort port, byte local) => FromLegacy(new LegacyNodeId(address, port, local));

        public static NodeId FromLegacy(IPEndPoint endPoint, byte local) => FromLegacy(new LegacyNodeId(endPoint, local));

        /// <summary>The legacy node id a kind-0 id holds; false for every other kind.</summary>
        public bool TryGetLegacy(out LegacyNodeId id)
        {
            if (Kind != LegacyKind)
            {
                id = default;
                return false;
            }
            id = new LegacyNodeId((uint)(bytes >> 8), (ushort)(bytes >> 40), (byte)(bytes >> 56));
            return true;
        }

        /// <summary>Bytes of a public key as the wire carries it: X then Y, 32 bytes each, big-endian.</summary>
        public const int PublicKeySize = 64;

        /// <summary>
        /// The kind-2 id of a P-256 public key (X then Y, <see cref="PublicKeySize"/> bytes): the kind
        /// byte, then the first 7 bytes of SHA-256 over the key (plan D3).
        /// </summary>
        public static NodeId FromPublicKey(ReadOnlySpan<byte> publicKey)
        {
            if (publicKey.Length != PublicKeySize)
            {
                throw new ArgumentException("A public key is " + PublicKeySize + " bytes", nameof(publicKey));
            }
            Span<byte> hash = stackalloc byte[32];
            System.Security.Cryptography.SHA256.HashData(publicKey, hash);
            Span<byte> wire = stackalloc byte[WireSize];
            wire[0] = KeyPairKind;
            hash[..(WireSize - 1)].CopyTo(wire[1..]);
            return ReadFrom(wire);
        }

        /// <summary>
        /// Whether two ids are on one device (for MaxNodesPerDevice): legacy ids compare ip and LAN octet;
        /// an id without a legacy id is its own device.
        /// </summary>
        public static bool SameDevice(NodeId x, NodeId y) =>
            x.TryGetLegacy(out LegacyNodeId a) && y.TryGetLegacy(out LegacyNodeId b) ? LegacyNodeId.SameDevice(a, b) : x == y;

        public void WriteTo(Span<byte> dest) => BinaryPrimitives.WriteUInt64LittleEndian(dest, bytes);

        public static NodeId ReadFrom(ReadOnlySpan<byte> src) => new(BinaryPrimitives.ReadUInt64LittleEndian(src));

        public bool Equals(NodeId other) => bytes == other.bytes;
        public override bool Equals(object obj) => obj is NodeId other && Equals(other);
        public override int GetHashCode() => bytes.GetHashCode();
        public static bool operator ==(NodeId left, NodeId right) => left.Equals(right);
        public static bool operator !=(NodeId left, NodeId right) => !left.Equals(right);

        /// <summary>
        /// For the log: a kind-0 id as its legacy node id, a kind-2 id (key pair) as "k2:" and its 7 value
        /// bytes in wire order (14 hex digits), any other kind as "kind", the kind and the 7 bytes.
        /// </summary>
        public override string ToString()
        {
            if (TryGetLegacy(out LegacyNodeId id))
            {
                return id.ToString();
            }
            Span<byte> wire = stackalloc byte[WireSize];
            WriteTo(wire);
            string value = Convert.ToHexString(wire[1..]);
            return Kind == KeyPairKind ? "k2:" + value : "kind " + Kind + " " + value;
        }
    }
}
