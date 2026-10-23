using System;
using System.Collections.Generic;
using System.Buffers;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;

namespace JoinFS.Net
{
    /// <summary>
    /// The real UDP socket. Sends happen on the caller's (network) thread; receives run on a
    /// dedicated thread that blocks in ReceiveFrom and hands each datagram to
    /// <see cref="Received"/> in a pooled buffer, so an arriving datagram wakes the network thread
    /// immediately instead of waiting for a polling tick.
    /// </summary>
    public sealed class UdpTransport : IDatagramTransport, IDisposable
    {
        public const int MaxDatagram = 65536;

        Socket socket;
        Thread receiveThread;
        volatile bool closing;
        readonly HashSet<IPAddress> droppedIPv4 = [];

        public int LocalPort { get; private set; }
        public bool IsOpen => socket != null;

        /// <summary>
        /// Open a dual-mode socket (IPv6 and IPv4 on one port) rather than an IPv4 one. Set before
        /// <see cref="Open"/>; moving to another port re-evaluates it.
        /// </summary>
        public bool Ipv6Enabled { get; set; } = true;

        /// <summary>
        /// Serve IPv4. Off only for a test instance that behaves as an IPv6-only node (<c>-noipv4</c>): the socket
        /// is then IPv6 only, and a send to an IPv4 address is dropped as it would be with no IPv4 route.
        /// </summary>
        public bool Ipv4Enabled { get; set; } = true;

        /// <summary>The open socket serves IPv6 as well as IPv4.</summary>
        public bool IsDualStack { get; private set; }

        /// <summary>The open socket serves IPv6 only.</summary>
        public bool IsIPv6Only { get; private set; }

        /// <summary>Why <see cref="Ipv6Enabled"/> is set but the socket is IPv4-only (null when it is not that case).</summary>
        public string FallbackReason { get; private set; }

        /// <summary>Test seam: creates the dual-mode socket; a throw stands for an OS that cannot.</summary>
        internal Func<Socket> DualModeFactory { get; set; }

        public event Action<IPEndPoint, string> SendFailed;

        /// <summary>
        /// Raised on the receive thread for each datagram. The receiver owns <c>buffer</c> (rented
        /// from ArrayPool&lt;byte&gt;.Shared) and must return it.
        /// </summary>
        public event Action<IPEndPoint, byte[], int> Received;

        public bool Open(int port, out string error)
        {
            error = null;
            if (port < IPEndPoint.MinPort || port > IPEndPoint.MaxPort)
            {
                error = "invalid port " + port;
                return false;
            }
            if (socket != null && LocalPort == port)
            {
                return true;
            }
            Close();
            droppedIPv4.Clear();
            try
            {
                Socket s = CreateSocket(out bool dual, out bool v6Only);
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    // don't let an ICMP port-unreachable from one peer kill ReceiveFrom for everyone
                    const uint IOC_IN = 0x80000000;
                    const uint IOC_VENDOR = 0x18000000;
                    const uint SIO_UDP_CONNRESET = IOC_IN | IOC_VENDOR | 12;
                    s.IOControl(unchecked((int)SIO_UDP_CONNRESET), [0], null);
                }
                s.Bind(new IPEndPoint(dual || v6Only ? IPAddress.IPv6Any : IPAddress.Any, port));
                IsDualStack = dual;
                IsIPv6Only = v6Only;
                socket = s;
                LocalPort = ((IPEndPoint)s.LocalEndPoint).Port;
                closing = false;
                receiveThread = new Thread(ReceiveLoop) { IsBackground = true, Name = "JoinFS-UdpReceive" };
                receiveThread.Start(s);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message + ": port=" + port;
                socket = null;
                LocalPort = 0;
                IsDualStack = false;
                IsIPv6Only = false;
                return false;
            }
        }

        /// <summary>
        /// A dual-mode socket when both families are on and the OS can make one, else an IPv4 socket (the reason is kept in
        /// <see cref="FallbackReason"/>). An IPv6-only socket when IPv4 is off; there is no fallback from that.
        /// </summary>
        Socket CreateSocket(out bool dual, out bool v6Only)
        {
            FallbackReason = null;
            dual = false;
            v6Only = false;
            if (!Ipv4Enabled)
            {
                if (!Ipv6Enabled)
                {
                    throw new InvalidOperationException("IPv4 and IPv6 are both turned off");
                }
                var only = new Socket(AddressFamily.InterNetworkV6, SocketType.Dgram, ProtocolType.Udp);
                try
                {
                    only.DualMode = false;
                }
                catch
                {
                    only.Dispose();
                    throw;
                }
                v6Only = true;
                return only;
            }
            if (Ipv6Enabled)
            {
                try
                {
                    Socket d = DualModeFactory != null ? DualModeFactory()
                        : new Socket(AddressFamily.InterNetworkV6, SocketType.Dgram, ProtocolType.Udp);
                    try
                    {
                        d.DualMode = true;
                    }
                    catch
                    {
                        d.Dispose();
                        throw;
                    }
                    dual = true;
                    return d;
                }
                catch (Exception ex)
                {
                    FallbackReason = ex.Message;
                }
            }
            return new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        }

        public void Close()
        {
            Socket s = socket;
            if (s == null)
            {
                return;
            }
            closing = true;
            socket = null;
            LocalPort = 0;
            IsDualStack = false;
            IsIPv6Only = false;
            try { s.Close(); } catch { }
            receiveThread?.Join(1000);
            receiveThread = null;
        }

        public void Send(IPEndPoint to, ReadOnlySpan<byte> datagram)
        {
            Socket s = socket;
            if (s == null || to == null)
            {
                return;
            }
            if (IsIPv6Only && to.AddressFamily == AddressFamily.InterNetwork)
            {
                // no IPv4 route; say so once per address, so a join that goes nowhere is not silent
                if (droppedIPv4.Count < 64 && droppedIPv4.Add(to.Address))
                {
                    SendFailed?.Invoke(to, "Not sent: IPv4 is turned off (-noipv4)");
                }
                return;
            }
            try
            {
                // a dual-mode socket addresses IPv4 peers by their IPv4-mapped IPv6 address
                IPEndPoint target = IsDualStack && to.AddressFamily == AddressFamily.InterNetwork
                    ? new IPEndPoint(to.Address.MapToIPv6(), to.Port)
                    : to;
                s.SendTo(datagram, SocketFlags.None, target);
            }
            catch (Exception ex)
            {
                SendFailed?.Invoke(to, ex.Message);
            }
        }

        void ReceiveLoop(object state)
        {
            var s = (Socket)state;
            bool dual = s.AddressFamily == AddressFamily.InterNetworkV6;
            EndPoint any = new IPEndPoint(dual ? IPAddress.IPv6Any : IPAddress.Any, 0);
            while (!closing)
            {
                byte[] buffer = ArrayPool<byte>.Shared.Rent(MaxDatagram);
                try
                {
                    EndPoint from = any;
                    int length = s.ReceiveFrom(buffer, SocketFlags.None, ref from);
                    Action<IPEndPoint, byte[], int> received = Received;
                    if (received != null)
                    {
                        var source = (IPEndPoint)from;
                        if (source.Address.IsIPv4MappedToIPv6)
                        {
                            // nothing above the transport ever sees a mapped address
                            source = new IPEndPoint(source.Address.MapToIPv4(), source.Port);
                        }
                        received(source, buffer, length);
                        buffer = null; // ownership transferred
                    }
                }
                catch (SocketException) when (!closing)
                {
                    // transient (e.g. message too long); keep receiving
                }
                catch (Exception) when (closing)
                {
                    // socket closed under us
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                finally
                {
                    if (buffer != null)
                    {
                        ArrayPool<byte>.Shared.Return(buffer);
                    }
                }
            }
        }

        public void Dispose() => Close();
    }
}
