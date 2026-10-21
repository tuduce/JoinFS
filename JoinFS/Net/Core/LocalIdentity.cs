using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace JoinFS.Net
{
    /// <summary>
    /// Who this node is on the network: its public address (learned from a my-IP service), LAN
    /// address and UDP port, which together make up its <see cref="LegacyNodeId"/> (and so its
    /// <see cref="NodeId"/>, until it has a key). Also turns a peer's legacy id into the endpoint to
    /// reach it, taking the shared-NAT case into account (a peer behind our own public IP is reached
    /// on its LAN address instead).
    /// </summary>
    public sealed class LocalIdentity
    {
        IPAddress localAddress = IPAddress.Loopback;
        IPAddress internetAddress = IPAddress.None;
        IReadOnlyList<IPAddress> ipv6Addresses = [];
        IReadOnlyList<IPEndPoint> candidates;

        /// <summary>The most IPv6 addresses this node advertises as candidates.</summary>
        public const int MaxIPv6Candidates = 4;

        /// <summary>This node's address as the legacy wire names it.</summary>
        public LegacyNodeId LegacyId { get; private set; }

        /// <summary>This node's id in the core: the kind-0 id of <see cref="LegacyId"/>.</summary>
        public NodeId Id => NodeId.FromLegacy(LegacyId);

        /// <summary>This instance's key pair, or null before the app has posted it. No wire carries it yet.</summary>
        public NodeKey Key { get; set; }

        /// <summary>The kind-2 id of <see cref="Key"/>: this node's alias beside its legacy id, none without a key.</summary>
        public NodeId KeyId => Key?.Id ?? default;

        /// <summary>The node knows its public address, so it can take part in a session.</summary>
        public bool Ready => LegacyId.Valid();

        public IPAddress LocalAddress
        {
            get => localAddress;
            set
            {
                localAddress = value;
                candidates = null;
                LegacyId = new LegacyNodeId(LegacyId.ip, LegacyId.port, value.GetAddressBytes()[3]);
            }
        }

        public IPAddress InternetAddress
        {
            get => internetAddress;
            set
            {
                internetAddress = value;
                candidates = null;
                LegacyId = new LegacyNodeId(value, LegacyId.port, LegacyId.local);
            }
        }

        public ushort Port
        {
            get => LegacyId.port;
            set
            {
                candidates = null;
                LegacyId = LegacyId.WithPort(value);
            }
        }

        /// <summary>
        /// This node's global unicast IPv6 addresses, stable ones before temporary ones, at most <see cref="MaxIPv6Candidates"/>
        /// (<see cref="DetectGlobalIPv6"/>). Empty when the node has none or does not use IPv6 (the IPv6 setting is off).
        /// </summary>
        public IReadOnlyList<IPAddress> Ipv6Addresses
        {
            get => ipv6Addresses;
            set
            {
                ipv6Addresses = value ?? [];
                candidates = null;
            }
        }

        /// <summary>This node can be reached, and reach others, over IPv6.</summary>
        public bool HasIPv6 => ipv6Addresses.Count > 0;

        /// <summary>
        /// The endpoints this node can be tried at, preferred first (docs/network/jfp2/standalone-ipv6-plan.md §5.4): its global IPv6
        /// addresses, its public IPv4 address, its LAN IPv4 address, each on the socket's port. Advertised in every JFP2 Hello and in
        /// member entries. Empty until the node knows its port.
        /// </summary>
        public IReadOnlyList<IPEndPoint> Candidates => candidates ??= BuildCandidates();

        IReadOnlyList<IPEndPoint> BuildCandidates()
        {
            var list = new List<IPEndPoint>();
            int port = LegacyId.port;
            if (port == 0)
            {
                return list;
            }
            foreach (IPAddress address in ipv6Addresses)
            {
                list.Add(new IPEndPoint(address, port));
            }
            AddIPv4(list, internetAddress, port);
            AddIPv4(list, localAddress, port);
            return list;
        }

        static void AddIPv4(List<IPEndPoint> list, IPAddress address, int port)
        {
            if (address.AddressFamily == AddressFamily.InterNetwork && !address.Equals(IPAddress.Any) && !address.Equals(IPAddress.None) && !IPAddress.IsLoopback(address))
            {
                var endPoint = new IPEndPoint(address, port);
                if (!list.Contains(endPoint))
                {
                    list.Add(endPoint);
                }
            }
        }

        /// <summary>
        /// This machine's global unicast IPv6 addresses (2000::/3: never link-local, unique-local or loopback) on interfaces that are up,
        /// at most <see cref="MaxIPv6Candidates"/>. Addresses of a temporary (privacy) kind come after the stable ones, since a stable address
        /// is the one that keeps answering. Empty when there are none or the interfaces cannot be read.
        /// </summary>
        public static List<IPAddress> DetectGlobalIPv6()
        {
            var stable = new List<IPAddress>();
            var temporary = new List<IPAddress>();
            try
            {
                foreach (NetworkInterface adapter in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (adapter.OperationalStatus != OperationalStatus.Up || adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                    {
                        continue;
                    }
                    foreach (UnicastIPAddressInformation info in adapter.GetIPProperties().UnicastAddresses)
                    {
                        if (info.Address.AddressFamily != AddressFamily.InterNetworkV6 || !IsGlobalUnicast(info.Address))
                        {
                            continue;
                        }
                        // Windows reports a privacy address with a random suffix
                        (info.SuffixOrigin == SuffixOrigin.Random ? temporary : stable).Add(info.Address);
                    }
                }
            }
            catch
            {
            }
            stable.AddRange(temporary);
            return stable.GetRange(0, Math.Min(stable.Count, MaxIPv6Candidates));
        }

        /// <summary>An IPv6 address in 2000::/3: reachable from the internet, unlike link-local (fe80::/10) and unique-local (fc00::/7).</summary>
        public static bool IsGlobalUnicast(IPAddress address)
        {
            if (address.AddressFamily != AddressFamily.InterNetworkV6)
            {
                return false;
            }
            Span<byte> bytes = stackalloc byte[16];
            return address.TryWriteBytes(bytes, out _) && (bytes[0] & 0xE0) == 0x20;
        }

        /// <summary>For tests and diagnostics: set the whole id directly.</summary>
        public void Set(NodeId id)
        {
            candidates = null;
            LegacyId = id.TryGetLegacy(out LegacyNodeId legacy) ? legacy : default;
        }

        public IPEndPoint MakeEndPoint(LegacyNodeId node, ushort port)
        {
            IPEndPoint endPoint = node.ToEndPoint(port);
            if (endPoint.Address.Equals(internetAddress))
            {
                // same NAT as us: reach it on the LAN
                byte[] bytes = localAddress.GetAddressBytes();
                bytes[3] = node.local;
                endPoint.Address = new IPAddress(bytes);
                endPoint.Port = node.port;
            }
            return endPoint;
        }

        /// <summary>This machine's (last listed) IPv4 LAN address, or loopback if it has none.</summary>
        public static IPAddress DetectLocalAddress()
        {
            IPAddress local = IPAddress.Loopback;
            try
            {
                foreach (IPAddress ip in Dns.GetHostEntry(Dns.GetHostName()).AddressList)
                {
                    if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork) local = ip;
                }
            }
            catch
            {
            }
            return local;
        }

        /// <summary>Same /24 as our LAN address.</summary>
        public bool IsLocalAddress(IPAddress address)
        {
            byte[] a = address.GetAddressBytes();
            byte[] b = localAddress.GetAddressBytes();
            return a[0] == b[0] && a[1] == b[1] && a[2] == b[2];
        }
    }
}
