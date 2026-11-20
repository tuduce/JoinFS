using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace JoinFS
{
    /// <summary>
    /// Cuts the bytes of a connection into FSD lines: a line ends at <c>\n</c>, a <c>\r</c> before it is dropped, an
    /// empty line is skipped. A line that grows past the limit without ending is an overflow, and the caller closes.
    /// </summary>
    public sealed class FsdLineSplitter
    {
        readonly int maxBytes;
        readonly List<byte> pending = new List<byte>();

        public FsdLineSplitter(int maxBytes = AtcGateway.MaxLineBytes)
        {
            this.maxBytes = maxBytes;
        }

        /// <summary>Add received bytes; complete lines are appended to <paramref name="lines"/>. False on an overflow.</summary>
        public bool Feed(ReadOnlySpan<byte> bytes, List<string> lines)
        {
            foreach (byte b in bytes)
            {
                if (b == (byte)'\n')
                {
                    if (pending.Count > 0 && pending[pending.Count - 1] == (byte)'\r') pending.RemoveAt(pending.Count - 1);
                    if (pending.Count > 0) lines.Add(AtcGateway.Wire.GetString(pending.ToArray()));
                    pending.Clear();
                }
                else
                {
                    pending.Add(b);
                    // one more than the limit: the line's own \r is not part of it
                    if (pending.Count > maxBytes + 1) return false;
                }
            }
            return true;
        }
    }

    /// <summary>
    /// The TCP listener for the ATC client on this PC (plan D1, D4): loopback only, IPv4 and IPv6, no authentication
    /// because nothing outside the PC can reach it. It owns the sockets and their threads; it never touches the gateway's
    /// state, it only <paramref name="post"/>s calls to the <see cref="IFsdHandler"/> (the app thread, <c>Main.EnqueueCommand</c>).
    /// While no listener exists, nothing listens (D3).
    /// </summary>
    public sealed class FsdListener : IDisposable
    {
        /// <summary>Most lines a client may have waiting to be written; a slower client is dropped.</summary>
        const int OutboxLimit = 4096;

        readonly IFsdHandler handler;
        readonly Action<Action> post;
        readonly Action<string> log;
        readonly CancellationTokenSource cancel = new CancellationTokenSource();
        readonly List<TcpListener> listeners = new List<TcpListener>();
        readonly ConcurrentDictionary<Connection, bool> connections = new ConcurrentDictionary<Connection, bool>();
        bool disposed;

        public FsdListener(IFsdHandler handler, Action<Action> post, Action<string> log)
        {
            this.handler = handler;
            this.post = post;
            this.log = log ?? (_ => { });
        }

        /// <summary>The port in use, once <see cref="Start"/> returned true.</summary>
        public int Port { get; private set; }

        /// <summary>
        /// Listen on 127.0.0.1 and ::1. Port 0 takes a free one (tests). True when at least one family listens;
        /// a failure is logged and not thrown.
        /// </summary>
        public bool Start(int port)
        {
            string failures = "";
            foreach (IPAddress address in new[] { IPAddress.Loopback, IPAddress.IPv6Loopback })
            {
                try
                {
                    var listener = new TcpListener(address, port);
                    listener.Start();
                    if (port == 0) port = ((IPEndPoint)listener.LocalEndpoint).Port;
                    listeners.Add(listener);
                }
                catch (Exception ex)
                {
                    failures += (failures.Length > 0 ? "; " : "") + address + ": " + ex.Message;
                }
            }

            if (listeners.Count == 0)
            {
                log("Cannot listen for ATC clients on port " + port + " (" + failures + ")");
                return false;
            }

            Port = port;
            var families = new List<string>();
            foreach (TcpListener listener in listeners)
            {
                families.Add(((IPEndPoint)listener.LocalEndpoint).Address.ToString());
                _ = AcceptLoop(listener);
            }
            log("Listening for ATC clients on " + string.Join(" and ", families) + " port " + port);
            return true;
        }

        async Task AcceptLoop(TcpListener listener)
        {
            while (!cancel.IsCancellationRequested)
            {
                TcpClient tcp;
                try
                {
                    tcp = await listener.AcceptTcpClientAsync(cancel.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { return; }
                catch (ObjectDisposedException) { return; }
                catch (SocketException ex)
                {
                    if (cancel.IsCancellationRequested) return;
                    log("ATC listener error: " + ex.Message);
                    await Task.Delay(500).ConfigureAwait(false);
                    continue;
                }

                var connection = new Connection(this, tcp);
                connections[connection] = true;
                post(() => handler.OnConnected(connection));
                _ = connection.Run();
            }
        }

        /// <summary>Stop listening and close every connection. Pending lines are not flushed.</summary>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            cancel.Cancel();
            foreach (TcpListener listener in listeners)
            {
                try { listener.Stop(); } catch { }
            }
            foreach (Connection connection in connections.Keys) connection.Abort();
        }

        sealed class Connection : IFsdConnection
        {
            readonly FsdListener owner;
            readonly TcpClient tcp;
            readonly Channel<string> outbox = Channel.CreateBounded<string>(
                new BoundedChannelOptions(OutboxLimit) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
            int closedPosted;

            public Connection(FsdListener owner, TcpClient tcp)
            {
                this.owner = owner;
                this.tcp = tcp;
                RemoteAddress = (tcp.Client.RemoteEndPoint as IPEndPoint)?.Address.ToString() ?? "unknown";
            }

            public string RemoteAddress { get; }

            public void Send(string line)
            {
                if (!outbox.Writer.TryWrite(line))
                {
                    // the client does not read: drop it rather than queue without bound
                    owner.log("ATC client from " + RemoteAddress + " is not reading, closed");
                    Abort();
                }
            }

            public void Close() => outbox.Writer.TryComplete();

            /// <summary>Close at once, without writing what is queued.</summary>
            public void Abort()
            {
                outbox.Writer.TryComplete();
                try { tcp.Close(); } catch { }
            }

            public async Task Run()
            {
                // the reader ends when the client goes (and then ends the outbox); the writer ends when the outbox does
                // (our Close, or the reader) and then closes the socket, which ends the reader
                await Task.WhenAll(Read(), Write()).ConfigureAwait(false);
                owner.connections.TryRemove(this, out _);
                if (Interlocked.Exchange(ref closedPosted, 1) == 0)
                    owner.post(() => owner.handler.OnClosed(this));
            }

            async Task Read()
            {
                var splitter = new FsdLineSplitter();
                var lines = new List<string>();
                var buffer = new byte[4096];
                try
                {
                    NetworkStream stream = tcp.GetStream();
                    while (true)
                    {
                        int count = await stream.ReadAsync(buffer, 0, buffer.Length, owner.cancel.Token).ConfigureAwait(false);
                        if (count == 0) return;
                        lines.Clear();
                        bool ok = splitter.Feed(buffer.AsSpan(0, count), lines);
                        foreach (string line in lines)
                        {
                            string text = line;
                            owner.post(() => owner.handler.OnLine(this, text));
                        }
                        if (!ok)
                        {
                            owner.log("ATC client from " + RemoteAddress + " sent a line over " + AtcGateway.MaxLineBytes + " bytes, closed");
                            Abort();
                            return;
                        }
                    }
                }
                catch (Exception ex) when (ex is IOException || ex is SocketException || ex is ObjectDisposedException || ex is OperationCanceledException || ex is InvalidOperationException)
                {
                    // the peer went away, or we closed
                }
                finally
                {
                    // nothing more to write to a client that is gone
                    outbox.Writer.TryComplete();
                }
            }

            async Task Write()
            {
                try
                {
                    NetworkStream stream = tcp.GetStream();
                    await foreach (string line in outbox.Reader.ReadAllAsync().ConfigureAwait(false))
                    {
                        byte[] bytes = AtcGateway.Wire.GetBytes(line + "\r\n");
                        await stream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
                    }
                }
                catch (Exception ex) when (ex is IOException || ex is SocketException || ex is ObjectDisposedException || ex is InvalidOperationException)
                {
                    // the client is gone
                }
                finally
                {
                    // everything queued is written (or cannot be): close, which also ends the reader
                    try { tcp.Close(); } catch { }
                }
            }
        }
    }
}
