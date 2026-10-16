using System;
using System.Net;
using System.Net.Sockets;

namespace JoinFS.Net
{
    /// <summary>What counts as an IPv6 address a peer on the internet could be reached at (endpoint candidates, docs/jfp2/design-future.md §4.1).</summary>
    public static class Ipv6Address
    {
        /// <summary>A real IPv6 address: not unspecified, loopback, link-local, multicast or IPv4-mapped.</summary>
        public static bool IsGlobal(IPAddress address) =>
            address.AddressFamily == AddressFamily.InterNetworkV6 && !address.IsIPv4MappedToIPv6
            && !address.Equals(IPAddress.IPv6Any) && !IPAddress.IsLoopback(address)
            && !address.IsIPv6LinkLocal && !address.IsIPv6Multicast;

        /// <summary>A usable candidate: a global address and a port.</summary>
        public static bool IsUsableCandidate(IPEndPoint endPoint) => endPoint != null && endPoint.Port > 0 && IsGlobal(endPoint.Address);

        /// <summary>Both addresses are in the same /64 (the part of an address a provider hands out; a device's temporary addresses rotate inside it).</summary>
        public static bool SamePrefix64(IPAddress a, IPAddress b)
        {
            Span<byte> x = stackalloc byte[16];
            Span<byte> y = stackalloc byte[16];
            return a.TryWriteBytes(x, out int nx) && b.TryWriteBytes(y, out int ny) && nx == 16 && ny == 16 && x[..8].SequenceEqual(y[..8]);
        }
    }
}
