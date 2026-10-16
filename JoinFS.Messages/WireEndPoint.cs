using System;
using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;

namespace JoinFS.Net.Jfp2
{
    /// <summary>
    /// How the JFP2 wire writes an address and port (docs/jfp2/protocol.md §4.4): a family
    /// byte, the address in network byte order, the port as u16 little-endian.
    ///
    /// | Family | Address | Size |
    /// |---|---|---|
    /// | 4 | 4 bytes, IPv4 | 7 |
    /// | 6 | 16 bytes, IPv6 | 19 |
    ///
    /// Other families are unassigned: a reader ignores the value. The size follows from the family,
    /// so the container bounds it (a TLV's length today; a u8 length before each entry in a list).
    /// An IPv4-mapped IPv6 address (what a dual-mode socket reports for an IPv4 peer) is written as
    /// family 4, and every endpoint this plugin compares or writes goes through <see cref="Normalize"/>,
    /// so one peer never appears under two forms. In memory the type is <see cref="IPEndPoint"/>; an
    /// address is only an address, a node's identity is its name (<see cref="NodeName"/>).
    /// </summary>
    public static class WireEndPoint
    {
        public const byte FamilyIPv4 = 4;
        public const byte FamilyIPv6 = 6;

        /// <summary>Wire size of an IPv4 endpoint: family, 4 address bytes, port.</summary>
        public const int IPv4Size = 1 + 4 + 2;

        /// <summary>Wire size of an IPv6 endpoint: family, 16 address bytes, port.</summary>
        public const int IPv6Size = 1 + 16 + 2;

        /// <summary>
        /// The endpoint with an IPv4-mapped IPv6 address as plain IPv4; any other endpoint (null too)
        /// as it is, without allocating.
        /// </summary>
        public static IPEndPoint Normalize(IPEndPoint endPoint) =>
            endPoint != null && endPoint.Address.IsIPv4MappedToIPv6 ? new IPEndPoint(endPoint.Address.MapToIPv4(), endPoint.Port) : endPoint;

        /// <summary>Bytes <see cref="WriteTo"/> writes for <paramref name="endPoint"/>.</summary>
        public static int SizeOf(IPEndPoint endPoint) => IsIPv4(endPoint.Address) ? IPv4Size : IPv6Size;

        /// <summary>Writes <paramref name="endPoint"/> (normalized: IPv4-mapped as family 4) and returns the bytes written.</summary>
        public static int WriteTo(IPEndPoint endPoint, Span<byte> dest)
        {
            IPAddress address = endPoint.Address;
            bool v4 = IsIPv4(address);
            if (v4 && address.AddressFamily != AddressFamily.InterNetwork)
            {
                address = address.MapToIPv4();
            }
            dest[0] = v4 ? FamilyIPv4 : FamilyIPv6;
            if (!address.TryWriteBytes(dest[1..], out int length) || length != (v4 ? 4 : 16))
            {
                throw new ArgumentException("destination too small for a JFP2 endpoint", nameof(dest));
            }
            BinaryPrimitives.WriteUInt16LittleEndian(dest.Slice(1 + length, 2), (ushort)endPoint.Port);
            return 1 + length + 2;
        }

        /// <summary><paramref name="endPoint"/> as its wire bytes.</summary>
        public static byte[] ToBytes(IPEndPoint endPoint)
        {
            byte[] bytes = new byte[SizeOf(endPoint)];
            WriteTo(endPoint, bytes);
            return bytes;
        }

        /// <summary>
        /// Reads an endpoint from the start of <paramref name="src"/>. False, and null, for an
        /// unassigned family or a value shorter than its family needs; bytes after the endpoint are
        /// not read (a later build may extend the value). The result is normalized.
        /// </summary>
        public static bool TryReadFrom(ReadOnlySpan<byte> src, out IPEndPoint endPoint)
        {
            endPoint = null;
            if (src.IsEmpty)
            {
                return false;
            }
            int length = src[0] switch
            {
                FamilyIPv4 => 4,
                FamilyIPv6 => 16,
                _ => 0,
            };
            if (length == 0 || src.Length < 1 + length + 2)
            {
                return false;
            }
            var address = new IPAddress(src.Slice(1, length));
            ushort port = BinaryPrimitives.ReadUInt16LittleEndian(src.Slice(1 + length, 2));
            endPoint = Normalize(new IPEndPoint(address, port));
            return true;
        }

        static bool IsIPv4(IPAddress address) => address.AddressFamily == AddressFamily.InterNetwork || address.IsIPv4MappedToIPv6;
    }
}
