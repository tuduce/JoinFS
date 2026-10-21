using System.Net;

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
                LegacyId = new LegacyNodeId(LegacyId.ip, LegacyId.port, value.GetAddressBytes()[3]);
            }
        }

        public IPAddress InternetAddress
        {
            get => internetAddress;
            set
            {
                internetAddress = value;
                LegacyId = new LegacyNodeId(value, LegacyId.port, LegacyId.local);
            }
        }

        public ushort Port
        {
            get => LegacyId.port;
            set => LegacyId = LegacyId.WithPort(value);
        }

        /// <summary>For tests and diagnostics: set the whole id directly.</summary>
        public void Set(NodeId id) => LegacyId = id.TryGetLegacy(out LegacyNodeId legacy) ? legacy : default;

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
