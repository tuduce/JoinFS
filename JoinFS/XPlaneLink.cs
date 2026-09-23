using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
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
    /// Runs on the app thread, polled from Sim.DoWork via XPlane.DoWork.
    /// </summary>
    public sealed class XPlaneLink
    {
        UdpClient client;
        readonly MemoryStream sendStream = new(1024);
        readonly BinaryWriter writer;
        readonly MemoryStream receiveStream = new(1024);
        readonly BinaryReader reader;
        readonly byte localOctet;
        ushort port;

        /// <summary>Called for each message from the plugin, with the reader positioned after the header.</summary>
        public Action<IPEndPoint, NodeId, BinaryReader> receiveNotify;
        public Action<string> nodeError;

        public XPlaneLink(byte? lanOctet = null)
        {
            writer = new BinaryWriter(sendStream);
            reader = new BinaryReader(receiveStream);
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

        public bool IsOpen => client != null;

        public bool Open(int newPort)
        {
            if (newPort < IPEndPoint.MinPort || newPort > IPEndPoint.MaxPort) return false;
            if (client != null && port == (ushort)newPort) return true;
            Close();
            try
            {
                client = new UdpClient(newPort, AddressFamily.InterNetwork);
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    const uint IOC_IN = 0x80000000;
                    const uint IOC_VENDOR = 0x18000000;
                    client.Client.IOControl(unchecked((int)(IOC_IN | IOC_VENDOR | 12)), [0], null);
                }
                port = (ushort)newPort;
                return true;
            }
            catch (Exception ex)
            {
                nodeError?.Invoke(ex.Message + ": port=" + newPort);
                client = null;
                return false;
            }
        }

        public void Close()
        {
            client?.Close();
            client = null;
        }

        /// <summary>Start a message: writes the header and returns the writer for the payload.</summary>
        public BinaryWriter PrepareMessage()
        {
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
            if (client == null || endPoint == null) return;
            writer.Flush();
            try
            {
                client.Send(sendStream.GetBuffer(), (int)sendStream.Length, endPoint);
            }
            catch (Exception ex)
            {
                nodeError?.Invoke(ex.Message + ", " + endPoint);
            }
        }

        /// <summary>Process every datagram waiting from the plugin.</summary>
        public void DoWork()
        {
            while (client != null && client.Available > 0)
            {
                IPEndPoint endPoint = new(IPAddress.Any, 0);
                byte[] data;
                try
                {
                    data = client.Receive(ref endPoint);
                }
                catch (Exception ex)
                {
                    nodeError?.Invoke(ex.Message);
                    continue;
                }
                if (data.Length < LegacyWire.DataOffset || (short)(data[0] | data[1] << 8) != LegacyWire.Version
                    || (data[LegacyWire.FlagsOffset] & LegacyWire.FlagInternal) != 0)
                {
                    continue;
                }
                receiveStream.SetLength(0);
                receiveStream.Write(data, 0, data.Length);
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
        }
    }
}
