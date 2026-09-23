using System.Net;

namespace JoinFS.Net
{
    /// <summary>
    /// Who this node is on the network: its public address (learned from a my-IP service), LAN
    /// address and UDP port, which together make up its <see cref="NodeId"/>. Also turns a peer's
    /// NodeId into the endpoint to reach it, taking the shared-NAT case into account (a peer
    /// behind our own public IP is reached on its LAN address instead).
    /// </summary>
    public sealed class LocalIdentity
    {
        IPAddress localAddress = IPAddress.Loopback;
        IPAddress internetAddress = IPAddress.None;

        public NodeId Id { get; private set; }

        /// <summary>The node knows its public address, so it can take part in a session.</summary>
        public bool Ready => Id.Valid();

        public IPAddress LocalAddress
        {
            get => localAddress;
            set
            {
                localAddress = value;
                Id = new NodeId(Id.ip, Id.port, value.GetAddressBytes()[3]);
            }
        }

        public IPAddress InternetAddress
        {
            get => internetAddress;
            set
            {
                internetAddress = value;
                Id = new NodeId(value, Id.port, Id.local);
            }
        }

        public ushort Port
        {
            get => Id.port;
            set => Id = Id.WithPort(value);
        }

        /// <summary>For tests and diagnostics: set the whole id directly.</summary>
        public void Set(NodeId id) => Id = id;

        public IPEndPoint MakeEndPoint(NodeId node, ushort port)
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
