using System;
using System.Buffers.Binary;
using System.IO;
using System.Net;

namespace JoinFS.Net
{
    /// <summary>
    /// Identifies a JoinFS node on the network: its public IPv4 address, UDP port, and the last octet
    /// of its LAN address (which disambiguates several nodes behind one NAT). Seven bytes on the
    /// legacy wire. The protocol-neutral replacement for LocalNode.Nuid (same layout, same equality,
    /// same encoding), so the core and every protocol plugin agree on peer identity.
    ///
    /// An all-zero ip means "no node" (unaddressed / broadcast / not yet known).
    /// </summary>
    public readonly struct NodeId : IEquatable<NodeId>
    {
        public const int WireSize = 7;

        public readonly uint ip;
        public readonly ushort port;
        public readonly byte local;

        public NodeId(uint ip, ushort port, byte local)
        {
            this.ip = ip;
            this.port = port;
            this.local = local;
        }

        public NodeId(IPAddress address, ushort port, byte local)
        {
            byte[] bytes = address.GetAddressBytes();
            ip = (uint)bytes[0] << 24 | (uint)bytes[1] << 16 | (uint)bytes[2] << 8 | bytes[3];
            this.port = port;
            this.local = local;
        }

        public NodeId(IPEndPoint endPoint, byte local) : this(endPoint.Address, (ushort)endPoint.Port, local)
        {
        }

        public bool Valid() => ip != 0;
        public bool Invalid() => ip == 0;

        public NodeId WithPort(ushort newPort) => new(ip, newPort, local);
        public NodeId WithAddress(IPAddress address) => new(address, port, local);

        public IPAddress ToAddress() => new([(byte)(ip >> 24), (byte)(ip >> 16), (byte)(ip >> 8), (byte)ip]);
        public IPEndPoint ToEndPoint(ushort endPointPort) => new(ToAddress(), endPointPort);

        public static bool SameDevice(NodeId x, NodeId y) => x.ip == y.ip && x.local == y.local;

        public static NodeId Read(BinaryReader reader) => new(reader.ReadUInt32(), reader.ReadUInt16(), reader.ReadByte());

        public void Write(BinaryWriter writer)
        {
            writer.Write(ip);
            writer.Write(port);
            writer.Write(local);
        }

        public static NodeId Read(ReadOnlySpan<byte> src) =>
            new(BinaryPrimitives.ReadUInt32LittleEndian(src), BinaryPrimitives.ReadUInt16LittleEndian(src[4..]), src[6]);

        public void Write(Span<byte> dest)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(dest, ip);
            BinaryPrimitives.WriteUInt16LittleEndian(dest[4..], port);
            dest[6] = local;
        }

        public bool Equals(NodeId other) => ip == other.ip && port == other.port && local == other.local;
        public override bool Equals(object obj) => obj is NodeId other && Equals(other);
        public override int GetHashCode() => ip.GetHashCode() ^ port.GetHashCode() ^ local.GetHashCode();
        public static bool operator ==(NodeId x, NodeId y) => x.Equals(y);
        public static bool operator !=(NodeId x, NodeId y) => !x.Equals(y);

        public override string ToString() => AddressCodec.EncodeIP(ToEndPoint(port).ToString()) + "/" + local;
    }
}
