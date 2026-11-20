using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace JoinFS.Tests.Atc
{
    public class FsdLineSplitterTests
    {
        static (bool Ok, List<string> Lines) Feed(FsdLineSplitter splitter, string text)
        {
            var lines = new List<string>();
            bool ok = splitter.Feed(Encoding.Latin1.GetBytes(text), lines);
            return (ok, lines);
        }

        [Fact]
        public void SplitsOnNewlineAndDropsTheCarriageReturn()
        {
            var (ok, lines) = Feed(new FsdLineSplitter(), "$DISERVER:CLIENT:x\r\n#AAa:b\n");

            Assert.True(ok);
            Assert.Equal(["$DISERVER:CLIENT:x", "#AAa:b"], lines);
        }

        [Fact]
        public void KeepsAPartialLineUntilItIsComplete()
        {
            var splitter = new FsdLineSplitter();

            Assert.Empty(Feed(splitter, "$PIa:SER").Lines);
            Assert.Equal(["$PIa:SERVER:1"], Feed(splitter, "VER:1\r").Lines.Concat(Feed(splitter, "\n").Lines));
        }

        [Fact]
        public void SkipsEmptyLines()
        {
            var (_, lines) = Feed(new FsdLineSplitter(), "\r\n\n#DAa\r\n");

            Assert.Equal(["#DAa"], lines);
        }

        [Fact]
        public void ALineOverTheLimit_IsAnOverflow()
        {
            var splitter = new FsdLineSplitter(maxBytes: 10);

            Assert.True(Feed(splitter, "0123456789").Ok);
            Assert.True(Feed(splitter, "x").Ok); // the room of a \r
            Assert.False(Feed(splitter, "x").Ok);
        }

        [Fact]
        public void ALineAtTheLimit_IsFine()
        {
            var (ok, lines) = Feed(new FsdLineSplitter(maxBytes: 10), "0123456789\r\n");

            Assert.True(ok);
            Assert.Equal(["0123456789"], lines);
        }

        [Fact]
        public void KeepsEveryByteOfALatin1Line()
        {
            var (_, lines) = Feed(new FsdLineSplitter(), "#TMa:b:Grüße\r\n");

            Assert.Equal(["#TMa:b:Grüße"], lines);
        }
    }

    /// <summary>The listener over real loopback TCP, with a handler that records (posting runs the call at once).</summary>
    public class FsdListenerTests : IDisposable
    {
        sealed class Recorder : IFsdHandler
        {
            public readonly BlockingCollection<string> Events = new();
            public IFsdConnection? Connection;

            public void OnConnected(IFsdConnection connection) { Connection = connection; Events.Add("connected"); }
            public void OnLine(IFsdConnection connection, string line) => Events.Add("line " + line);
            public void OnClosed(IFsdConnection connection) => Events.Add("closed");

            public string Next() => Events.TryTake(out string? e, TimeSpan.FromSeconds(5)) ? e : "timeout";
        }

        readonly Recorder handler = new();
        readonly List<string> log = [];
        readonly FsdListener listener;
        readonly List<TcpClient> clients = [];

        public FsdListenerTests()
        {
            listener = new FsdListener(handler, action => action(), s => { lock (log) log.Add(s); });
            Assert.True(listener.Start(0));
        }

        public void Dispose()
        {
            foreach (TcpClient client in clients) client.Dispose();
            listener.Dispose();
        }

        TcpClient ConnectClient(IPAddress? address = null)
        {
            var client = new TcpClient(AddressFamily.InterNetwork);
            client.Connect(address ?? IPAddress.Loopback, listener.Port);
            clients.Add(client);
            return client;
        }

        static void Write(TcpClient client, string text) => client.GetStream().Write(Encoding.Latin1.GetBytes(text));

        static string ReadLine(TcpClient client)
        {
            client.ReceiveTimeout = 5000;
            var bytes = new List<byte>();
            Stream stream = client.GetStream();
            while (true)
            {
                int b = stream.ReadByte();
                if (b < 0) return Encoding.Latin1.GetString(bytes.ToArray());
                if (b == '\n') return Encoding.Latin1.GetString(bytes.ToArray()).TrimEnd('\r');
                bytes.Add((byte)b);
            }
        }

        [Fact]
        public void ListensOnLoopbackOnly()
        {
            // the listener says where it listens
            Assert.Contains(log, l => l.Contains("127.0.0.1") && l.Contains(listener.Port.ToString()));

            // and the PC's own LAN address does not reach it (D4); a machine without one skips the check
            IPAddress? lan = Dns.GetHostAddresses(Dns.GetHostName())
                .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a));
            if (lan == null) return;
            using var client = new TcpClient(AddressFamily.InterNetwork);
            Assert.ThrowsAny<SocketException>(() => client.Connect(lan, listener.Port));
        }

        [Fact]
        public void ReportsAConnectionAndItsLines()
        {
            TcpClient client = ConnectClient();
            Assert.Equal("connected", handler.Next());

            Write(client, "$IDa:SERVER:1\r\n#AAEDDM_TWR:SERVER:Carla:1:t:4:100\r\n");

            Assert.Equal("line $IDa:SERVER:1", handler.Next());
            Assert.Equal("line #AAEDDM_TWR:SERVER:Carla:1:t:4:100", handler.Next());
        }

        [Fact]
        public void WritesLinesWithACrLfEnding()
        {
            TcpClient client = ConnectClient();
            Assert.Equal("connected", handler.Next());

            handler.Connection!.Send("$PO:a:b");
            handler.Connection!.Send("#TMserver:a:hi");

            Assert.Equal("$PO:a:b", ReadLine(client));
            Assert.Equal("#TMserver:a:hi", ReadLine(client));
        }

        [Fact]
        public void ClosingFlushesTheQueuedLinesFirst()
        {
            TcpClient client = ConnectClient();
            Assert.Equal("connected", handler.Next());

            handler.Connection!.Send("$ERSERVER:x:002::bad");
            handler.Connection!.Close();

            Assert.Equal("$ERSERVER:x:002::bad", ReadLine(client));
            Assert.Equal("", ReadLine(client)); // end of stream
            Assert.Equal("closed", handler.Next());
        }

        [Fact]
        public void AClientThatDisconnects_IsReported()
        {
            TcpClient client = ConnectClient();
            Assert.Equal("connected", handler.Next());

            client.Close();

            Assert.Equal("closed", handler.Next());
        }

        [Fact]
        public void AnOversizedLine_ClosesTheConnection()
        {
            TcpClient client = ConnectClient();
            Assert.Equal("connected", handler.Next());

            Write(client, new string('x', AtcGateway.MaxLineBytes + 10));

            Assert.Equal("closed", handler.Next());
            Assert.Contains(log, l => l.Contains("over " + AtcGateway.MaxLineBytes));
        }

        [Fact]
        public void AcceptsAnIPv6LoopbackClientWhenThePlatformHasIPv6()
        {
            if (!Socket.OSSupportsIPv6) return;
            var client = new TcpClient(AddressFamily.InterNetworkV6);
            clients.Add(client);
            try { client.Connect(IPAddress.IPv6Loopback, listener.Port); }
            catch (SocketException) { return; } // IPv6 loopback is not usable on this machine

            Assert.Equal("connected", handler.Next());
        }

        [Fact]
        public void Dispose_StopsListeningAndClosesTheConnections()
        {
            TcpClient client = ConnectClient();
            Assert.Equal("connected", handler.Next());
            int port = listener.Port;

            listener.Dispose();

            Assert.Equal("closed", handler.Next());
            Assert.Throws<SocketException>(() =>
            {
                using var again = new TcpClient(AddressFamily.InterNetwork);
                again.Connect(IPAddress.Loopback, port);
            });
        }

        [Fact]
        public void APortThatIsTaken_IsLoggedAndNotThrown()
        {
            using var second = new FsdListener(handler, action => action(), s => { lock (log) log.Add(s); });

            Assert.False(second.Start(listener.Port));
            Assert.Contains(log, l => l.Contains("Cannot listen"));
        }
    }
}
