using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Threading;
using JoinFS.Net.Legacy;

namespace JoinFS.Net
{
    /// <summary>
    /// Runs the network stack on its own thread and is the only thing the app talks to.
    ///
    /// - The network thread owns <see cref="NetworkCore"/> and everything under it (socket sends,
    ///   plugins, peers, mesh). Nothing below it takes a lock.
    /// - A receive thread (in <see cref="UdpTransport"/>) and the app post work into one mailbox,
    ///   so the network thread wakes immediately for either, and otherwise ticks every
    ///   <see cref="TickInterval"/>.
    /// - Messages and events for the app go into one ordered queue that the app drains on its own
    ///   thread (Main.DoWork, under conch) with <see cref="Drain"/>.
    /// - UI/Sim/CONSOLE code reads <see cref="Snapshot"/>: immutable, lock-free, at most
    ///   <see cref="SnapshotInterval"/> old.
    ///
    /// See docs/network-plugin-architecture.md §2.7.
    /// </summary>
    public sealed class NetworkService : INetworkSink, INetworkOutbox, IDisposable
    {
        public const double TickInterval = 0.005;
        public const double SnapshotInterval = 0.1;

        readonly UdpTransport transport = new();
        readonly BlockingCollection<IWork> mailbox = new(new ConcurrentQueue<IWork>());
        readonly ConcurrentQueue<IInbound> inbound = new();
        readonly IClock clock;
        Thread thread;
        volatile bool running;
        volatile NetworkSnapshot snapshot = NetworkSnapshot.Empty;
        double nextTick;
        double nextSnapshot;

        /// <summary>The core. Touch it only from the network thread (i.e. inside <see cref="Post"/>).</summary>
        public NetworkCore Core { get; }

        public LegacyPlugin Legacy { get; }

        public NetworkSnapshot Snapshot => snapshot;

        /// <summary>The UDP port currently bound (0 when closed).</summary>
        public int Port => transport.LocalPort;

        public NetworkService(IClock clock = null, Func<string, CredentialStore> credentialStoreFactory = null)
        {
            this.clock = clock ?? SystemClock.Instance;
            Core = new NetworkCore(transport, this.clock, this, credentialStoreFactory);
            Legacy = new LegacyPlugin((ushort)Stopwatch.GetTimestamp());
            Core.AddPlugin(Legacy);
            transport.Received += (from, buffer, length) => mailbox.Add(DatagramWork.Rent(from, buffer, length));
            transport.SendFailed += (to, error) => Core.Log(NetLogLevel.Event, error + ", " + to);
            Core.Identity.LocalAddress = LocalIdentity.DetectLocalAddress();
        }

        /// <summary>Add a protocol plugin (before <see cref="Start"/>).</summary>
        public void AddPlugin(IProtocolPlugin plugin) => Core.AddPlugin(plugin);

        public void Start()
        {
            if (running) return;
            running = true;
            thread = new Thread(Run) { IsBackground = true, Name = "JoinFS-Network" };
            thread.Start();
        }

        public void Stop()
        {
            if (!running) return;
            running = false;
            mailbox.Add(new ActionWork(_ => { }));
            thread?.Join(2000);
            thread = null;
            // anything posted before stopping (e.g. a final Leave) still runs, on this thread now
            // that the network thread is gone
            while (mailbox.TryTake(out IWork work)) Execute(work);
            transport.Close();
        }

        public void Dispose()
        {
            Stop();
            mailbox.Dispose();
        }

        // ------------------------------------------------------------------ app → network

        /// <summary>Run <paramref name="command"/> on the network thread.</summary>
        public void Post(Action<NetworkCore> command) => mailbox.Add(new ActionWork(command));

        public void Send<T>(in MessageMeta meta, in T message, ReadOnlySpan<NodeId> recipients) where T : struct, IMessage =>
            mailbox.Add(SendWork<T>.Rent(meta, message, recipients));

        public void SendTo<T>(NodeId recipient, in T message, bool guaranteed = false) where T : struct, IMessage =>
            Send(MessageMeta.To(recipient, guaranteed), message, new ReadOnlySpan<NodeId>(in recipient));

        public void SendToEndPoint<T>(IPEndPoint endPoint, in T message, bool guaranteed = false) where T : struct, IMessage =>
            Send(MessageMeta.ToEndPoint(endPoint, guaranteed), message, ReadOnlySpan<NodeId>.Empty);

        public void Broadcast<T>(in T message, bool guaranteed = false) where T : struct, IMessage =>
            mailbox.Add(new BroadcastWork<T>(message, guaranteed));

        /// <summary>
        /// Publish an object's identity (cached for the protocols that need it) and send its
        /// position to <paramref name="recipients"/>, in that order.
        /// </summary>
        public void SendObjectState<TPosition>(in IdentityUpdate identity, in TPosition position, ReadOnlySpan<NodeId> recipients) where TPosition : struct, IMessage
        {
            mailbox.Add(ObjectStateWork<TPosition>.Rent(identity, position, recipients));
        }

        /// <summary>
        /// Open (or move to) a UDP port. Moving leaves any current session first, on the old port,
        /// like LocalNode.Open did. Runs on the network thread; the caller waits for the result.
        /// </summary>
        public bool Open(int port, out string error)
        {
            bool ok = false;
            string failure = null;
            void OpenOnNetworkThread(NetworkCore core)
            {
                if (core.IsOpen && transport.LocalPort != port)
                {
                    core.Mesh.Leave();
                }
                ok = transport.Open(port, out failure);
                core.IsOpen = ok;
                if (ok) core.Identity.Port = (ushort)transport.LocalPort;
                nextSnapshot = 0;
            }
            if (running && Thread.CurrentThread != thread)
            {
                using var done = new ManualResetEventSlim();
                Post(core =>
                {
                    try { OpenOnNetworkThread(core); }
                    finally { done.Set(); }
                });
                if (!done.Wait(TimeSpan.FromSeconds(5)))
                {
                    error = "timed out opening port " + port;
                    return false;
                }
            }
            else
            {
                OpenOnNetworkThread(Core);
                snapshot = BuildSnapshot();
            }
            error = failure;
            return ok;
        }

        public void Close() => Post(core =>
        {
            core.Mesh.Leave();
            core.IsOpen = false;
            transport.Close();
        });

        // ------------------------------------------------------------------ network → app

        /// <summary>
        /// Hand queued messages and events to the app's handlers, in arrival order. Call on the app
        /// thread. Returns how many items were dispatched.
        /// </summary>
        public int Drain(IMessageHandler messages, INetworkEventHandler events, int max = int.MaxValue)
        {
            int count = 0;
            while (count < max && inbound.TryDequeue(out IInbound item))
            {
                item.Dispatch(messages, events);
                count++;
            }
            return count;
        }

        void INetworkSink.Deliver<T>(in MessageMeta meta, in T message) => inbound.Enqueue(InboundMessage<T>.Rent(meta, message));

        void INetworkSink.OnEvent(in NetworkEvent e) => inbound.Enqueue(new InboundEvent(e));

        // ------------------------------------------------------------------ network thread

        void Run()
        {
            while (running)
            {
                double now = clock.Now;
                int wait = (int)Math.Max(0, Math.Ceiling((nextTick - now) * 1000));
                if (mailbox.TryTake(out IWork work, wait))
                {
                    Execute(work);
                    // drain whatever else is ready without sleeping
                    while (mailbox.TryTake(out work)) Execute(work);
                }
                now = clock.Now;
                if (now >= nextTick)
                {
                    try
                    {
                        Core.Tick();
                    }
                    catch (Exception ex)
                    {
                        Core.Log(NetLogLevel.Event, "ERROR: Network tick - " + ex.Message);
                    }
                    nextTick = now + TickInterval;
                }
                if (now >= nextSnapshot)
                {
                    snapshot = BuildSnapshot();
                    nextSnapshot = now + SnapshotInterval;
                }
            }
        }

        void Execute(IWork work)
        {
            try
            {
                work.Execute(Core);
            }
            catch (Exception ex)
            {
                Core.Log(NetLogLevel.Event, "ERROR: Network - " + ex.Message);
            }
        }

        NetworkSnapshot BuildSnapshot()
        {
            var peers = new Dictionary<NodeId, PeerSnapshot>(Core.Peers.Count);
            var list = new List<PeerSnapshot>(Core.Peers.Count);
            foreach (Peer peer in Core.Peers.All)
            {
                string linkState = null;
                foreach (IProtocolPlugin plugin in Core.Plugins)
                {
                    if (plugin is IDescribesLinks describer && (linkState = describer.DescribeLink(peer)) != null) break;
                }
                var p = new PeerSnapshot
                {
                    Id = peer.Id,
                    EndPoint = new IPEndPoint(peer.EndPoint.Address, peer.EndPoint.Port),
                    RouteEndPoint = new IPEndPoint(peer.RouteEndPoint.Address, peer.RouteEndPoint.Port),
                    Direct = peer.Direct,
                    RouteIsOwnEndPoint = peer.RouteIsOwnEndPoint,
                    ReceiveEstablished = peer.ReceiveEstablished,
                    SendEstablished = peer.SendEstablished,
                    LowBandwidth = peer.LowBandwidth,
                    Rtt = peer.Rtt,
                    PositionProtocol = Core.Route(peer.Id, MessageKind.Position)?.Name,
                    LinkState = linkState,
                };
                peers[peer.Id] = p;
                list.Add(p);
            }
            MeshManager mesh = Core.Mesh;
            return new NetworkSnapshot
            {
                Open = Core.IsOpen,
                Port = Core.Identity.Port,
                LocalId = Core.Identity.Id,
                LocalAddress = Core.Identity.LocalAddress,
                InternetAddress = Core.Identity.InternetAddress,
                State = mesh.State,
                Suid = mesh.Suid,
                GlobalSession = mesh.GlobalSession,
                Creator = mesh.Creator,
                LoginRequired = mesh.LoginRequired,
                PasswordProtected = mesh.PasswordProtected,
                JoinResult = mesh.ActiveJoinResult,
                LoginResult = mesh.ActiveLoginResult,
                LowBandwidth = Core.LowBandwidth,
                RelayCount = mesh.RelayCount,
                GuaranteedInCount = Legacy.GuaranteedInCount,
                GuaranteedOutCount = Legacy.GuaranteedOutCount,
                Peers = peers,
                PeerList = list,
            };
        }
    }
}
