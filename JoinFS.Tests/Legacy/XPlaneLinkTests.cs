using System.Net;
using System.Net.Sockets;
using JoinFS.Net;

namespace JoinFS.Tests.Legacy
{
    /// <summary>
    /// XPlaneLink replaced the private LocalNode instance XPlane.cs used to talk to the native
    /// JoinFS-XP plugin. The plugin parses the legacy header (JoinFS-XP/Link.h), so the bytes must be
    /// identical to what LocalNode.PrepareMessage + Send(endPoint) produced - verified against the
    /// real LocalNode before it was deleted, and pinned here as explicit bytes.
    /// </summary>
    public class XPlaneLinkTests
    {
        [Fact]
        public void Framing_MatchesLegacyLocalNode()
        {
            using var capture = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            var target = (IPEndPoint)capture.Client.LocalEndPoint!;

            var link = new XPlaneLink(lanOctet: 42);
            Assert.True(link.Open(49101));
            var w = link.PrepareMessage();
            w.Write((short)7);
            w.Write((byte)3);
            w.Write("payload");
            link.Send(target);

            IPEndPoint? from = null;
            byte[] actual = capture.Receive(ref from);
            link.Close();

            // version 0x520B, flags 0, guaranteed id 0, index 0, count 1,
            // sender (ip 0, port 49101 = 0xBFCD, local 42), recipient all zero, then the payload
            Assert.Equal("0B52" + "00" + "0000" + "00" + "01" + "00000000CDBF2A" + "00000000000000" + "0700" + "03" + "077061796C6F6164",
                Convert.ToHexString(actual));
        }

        [Fact]
        public void Receive_DeliversPayloadAfterHeader()
        {
            var link = new XPlaneLink(lanOctet: 1);
            Assert.True(link.Open(49102));
            string? got = null;
            link.receiveNotify = (_, _, reader) => { reader.ReadInt16(); got = reader.ReadString(); };

            using var plugin = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            byte[] datagram = LegacyDatagrams.Build(default, default, false, false, 0, w => { w.Write((short)5); w.Write("hello"); });
            plugin.Send(datagram, datagram.Length, new IPEndPoint(IPAddress.Loopback, 49102));
            var deadline = DateTime.UtcNow.AddSeconds(2);
            while (got == null && DateTime.UtcNow < deadline)
            {
                link.DoWork();
                Thread.Sleep(5);
            }
            link.Close();
            Assert.Equal("hello", got);
        }
    }
}
