using System;
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

        public int LocalPort { get; private set; }
        public bool IsOpen => socket != null;

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
            try
            {
                var s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    // don't let an ICMP port-unreachable from one peer kill ReceiveFrom for everyone
                    const uint IOC_IN = 0x80000000;
                    const uint IOC_VENDOR = 0x18000000;
                    const uint SIO_UDP_CONNRESET = IOC_IN | IOC_VENDOR | 12;
                    s.IOControl(unchecked((int)SIO_UDP_CONNRESET), [0], null);
                }
                s.Bind(new IPEndPoint(IPAddress.Any, port));
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
                return false;
            }
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
            try
            {
                s.SendTo(datagram, SocketFlags.None, to);
            }
            catch (Exception ex)
            {
                SendFailed?.Invoke(to, ex.Message);
            }
        }

        void ReceiveLoop(object state)
        {
            var s = (Socket)state;
            EndPoint any = new IPEndPoint(IPAddress.Any, 0);
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
                        received((IPEndPoint)from, buffer, length);
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
