using System;
using System.Buffers;
using System.IO;
using System.Net;
using System.Net.Sockets;
using JoinFS.Net;
using JoinFS.Net.Legacy;

namespace JoinFS
{
    /// <summary>
    /// The local UDP link to the JoinFS X-Plane plugin (JoinFS-XP). It uses the legacy JoinFS
    /// datagram header (JoinFS-XP/Link.h's Msg struct mirrors it), but it is not a network session:
    /// no mesh, no relaying, no guaranteed delivery. It used to be a whole private LocalNode instance;
    /// this is just the framing, byte-identical with what that instance sent, so the native plugin
    /// needs no change (and no NODE_VERSION bump).
    ///
    /// Receiving runs on the socket's own receive thread (<see cref="UdpTransport"/>), which hands each
    /// datagram to <see cref="post"/>, so it is decoded on the sim thread as soon as it arrives.
    /// Messages are built and sent on the sim thread (the send buffer is shared).
    /// </summary>
    public sealed class XPlaneLink
    {
        readonly UdpTransport transport = new();
        readonly MemoryStream sendStream = new(1024);
        readonly BinaryWriter writer;
        readonly MemoryStream receiveStream = new(1024);
        readonly BinaryReader reader;
        readonly byte localOctet;
        ushort port;

        /// <summary>Called for each message from the plugin, with the reader positioned after the header.</summary>
        public Action<IPEndPoint, NodeId, BinaryReader> receiveNotify;
        public Action<string> nodeError;

        /// <summary>
        /// Runs an action on the thread that owns the link (the sim thread). Called on the receive
        /// thread for each datagram; when null the datagram is handled on the receive thread.
        /// </summary>
        public Action<Action> post;

        /// <summary>
        /// The thread that builds messages. The send buffer is shared, so every message must be
        /// built and sent on one thread; the first thread to send becomes the owner.
        /// </summary>
        int ownerThreadId;
        bool wrongThreadReported;

        void CheckThread()
        {
            int current = Environment.CurrentManagedThreadId;
            if (ownerThreadId == 0)
            {
                ownerThreadId = current;
            }
            else if (current != ownerThreadId && !wrongThreadReported)
            {
                wrongThreadReported = true;
                nodeError?.Invoke("THREAD - X-Plane link used from thread " + current + " (" + System.Threading.Thread.CurrentThread.Name + "), owner is thread " + ownerThreadId);
            }
        }

        public XPlaneLink(byte? lanOctet = null)
        {
            writer = new BinaryWriter(sendStream);
            reader = new BinaryReader(receiveStream);
            transport.Received += OnReceived;
            transport.SendFailed += (to, error) => nodeError?.Invoke(error + ", " + to);
            if (lanOctet.HasValue)
            {
                localOctet = lanOctet.Value;
                return;
            }
            // the old LocalNode put its LAN octet (and port) in the sender field; the plugin ignores it
            try
            {
                foreach (IPAddress ip in Dns.GetHostEntry(Dns.GetHostName()).AddressList)
                {
                    if (ip.AddressFamily == AddressFamily.InterNetwork) localOctet = ip.GetAddressBytes()[3];
                }
            }
            catch
            {
            }
        }

        public bool IsOpen => transport.IsOpen;

        public bool Open(int newPort)
        {
            if (newPort < IPEndPoint.MinPort || newPort > IPEndPoint.MaxPort) return false;
            if (transport.IsOpen && port == (ushort)newPort) return true;
            Close();
            if (!transport.Open(newPort, out string error))
            {
                nodeError?.Invoke(error);
                return false;
            }
            port = (ushort)newPort;
            return true;
        }

        public void Close()
        {
            transport.Close();
        }

        /// <summary>Start a message: writes the header and returns the writer for the payload.</summary>
        public BinaryWriter PrepareMessage()
        {
            CheckThread();
            sendStream.SetLength(0);
            writer.Write(LegacyWire.Version);
            writer.Write((byte)0);      // flags
            writer.Write((ushort)0);    // guaranteed id
            writer.Write((byte)0);      // segment index
            writer.Write((byte)1);      // segment count
            new NodeId(0, port, localOctet).Write(writer);
            default(NodeId).Write(writer);
            return writer;
        }

        public void Send(IPEndPoint endPoint)
        {
            if (!transport.IsOpen || endPoint == null) return;
            writer.Flush();
            transport.Send(endPoint, sendStream.GetBuffer().AsSpan(0, (int)sendStream.Length));
        }

        /// <summary>Receive thread: pass a datagram from the plugin to the owner thread.</summary>
        void OnReceived(IPEndPoint endPoint, byte[] buffer, int length)
        {
            Action<Action> postTo = post;
            if (postTo == null)
            {
                Process(endPoint, buffer, length);
                return;
            }
            postTo(() => Process(endPoint, buffer, length));
        }

        /// <summary>Handle one datagram from the plugin and return its buffer to the pool.</summary>
        void Process(IPEndPoint endPoint, byte[] data, int length)
        {
            try
            {
                // drop anything still queued when the link was closed
                if (!transport.IsOpen || length < LegacyWire.DataOffset || (short)(data[0] | data[1] << 8) != LegacyWire.Version
                    || (data[LegacyWire.FlagsOffset] & LegacyWire.FlagInternal) != 0)
                {
                    return;
                }
                receiveStream.SetLength(0);
                receiveStream.Write(data, 0, length);
                receiveStream.Position = LegacyWire.DataOffset;
                try
                {
                    receiveNotify?.Invoke(endPoint, NodeId.Read(data.AsSpan(LegacyWire.SenderOffset)), reader);
                }
                catch (Exception ex)
                {
                    nodeError?.Invoke("ERROR: Failed to read X-Plane plugin message: " + ex.Message);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(data);
            }
        }
    }
}
