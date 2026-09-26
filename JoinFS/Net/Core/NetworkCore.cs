using System;
using System.Buffers;
using System.Collections.Generic;
using System.Net;
using System.Runtime.CompilerServices;

namespace JoinFS.Net
{
    /// <summary>
    /// The protocol-neutral heart of the network stack: demultiplexes incoming datagrams to protocol
    /// plugins, routes outgoing canonical messages to the right plugin per (peer, kind), runs the
    /// mesh, and relays/translates messages addressed to other nodes. Single-threaded by design (no
    /// locks): NetworkService drives it from its own thread; tests drive it directly with an
    /// in-memory transport and a manual clock. See docs/network-plugin-architecture.md §2.
    /// </summary>
    public sealed class NetworkCore : IProtocolHost
    {
        readonly List<IProtocolPlugin> plugins = [];
        readonly List<IPAddress> banList = [];
        readonly INetworkSink sink;

        public IDatagramTransport Transport { get; }
        public IClock Clock { get; }
        public LocalIdentity Identity { get; } = new();
        public PeerDirectory Peers { get; } = new();
        public ObjectStateCache Objects { get; } = new();
        public MeshManager Mesh { get; }

        /// <summary>The transport is open (set by the owner, which opens and closes it).</summary>
        public bool IsOpen { get; set; }

        public bool LowBandwidth { get; set; }

        public bool Connected => Mesh.Connected;

        public int RelayCount => Mesh.RelayCount;

        public IReadOnlyList<IProtocolPlugin> Plugins => plugins;

        public NetworkCore(IDatagramTransport transport, IClock clock, INetworkSink sink, Func<string, CredentialStore> credentialStoreFactory = null)
        {
            Transport = transport;
            Clock = clock;
            this.sink = sink;
            Mesh = new MeshManager(this, credentialStoreFactory ?? (folder => new CredentialStore(folder, text => Log(NetLogLevel.Event, text))));
        }

        /// <summary>Register a protocol; plugins are consulted in descending <see cref="IProtocolPlugin.Preference"/>.</summary>
        public void AddPlugin(IProtocolPlugin plugin)
        {
            plugins.Add(plugin);
            plugins.Sort((a, b) => b.Preference.CompareTo(a.Preference));
            plugin.Attach(this);
            foreach (Peer peer in Peers.All) peer.InvalidateRoutes();
        }

        public void BanIP(string ip)
        {
            if (IPAddress.TryParse(ip, out IPAddress address) && address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            {
                banList.Add(address);
            }
        }

        // ------------------------------------------------------------------ receive

        public void OnDatagram(IPEndPoint from, ReadOnlySpan<byte> datagram)
        {
            if (datagram.IsEmpty || banList.Contains(from.Address))
            {
                return;
            }
            foreach (IProtocolPlugin plugin in plugins)
            {
                if (plugin.Accepts(datagram))
                {
                    plugin.OnDatagram(from, datagram);
                    return;
                }
            }
        }

        public void Deliver<T>(in MessageMeta meta, in T message) where T : struct, IMessage
        {
            MessageKind kind = T.Kind;
            if (kind >= MessageKind.Join)
            {
                message.Dispatch(Mesh, meta);
                return;
            }
            // keep the identity/object cache current whoever the message is for (translation reads it)
            if (kind == MessageKind.Identity)
            {
                Objects.SetIdentity(meta.Sender, Unsafe.As<T, IdentityUpdate>(ref Unsafe.AsRef(in message)));
            }
            else if (kind == MessageKind.RemoveObject)
            {
                Objects.RemoveObject(meta.Sender, Unsafe.As<T, RemoveObject>(ref Unsafe.AsRef(in message)).ObjectId);
            }
            if (meta.Recipient.Valid() && meta.Recipient != Identity.Id)
            {
                Translate(meta, message);
                return;
            }
            if (!Connected && RequiresSession(kind))
            {
                return;
            }
            sink.Deliver(meta, message);
        }

        /// <summary>Kinds the application only accepts while this node is in a session (as the legacy receiver did).</summary>
        static bool RequiresSession(MessageKind kind) => kind switch
        {
            MessageKind.Position or MessageKind.ObjectPosition or MessageKind.Identity or MessageKind.VariableSync
                or MessageKind.Event or MessageKind.WeatherRequest or MessageKind.WeatherReply or MessageKind.WeatherUpdate
                or MessageKind.PeerInfo or MessageKind.StatusRequest or MessageKind.UserListRequest or MessageKind.UserPositionsRequest => true,
            _ => false,
        };

        /// <summary>
        /// A plugin decoded a message addressed to another node because it could not relay it in
        /// its own wire format: re-send it with whichever plugin reaches the recipient, keeping the
        /// original sender. This is the generic protocol bridge (design §2.6) - there is no
        /// per-protocol-pair translation code.
        /// </summary>
        void Translate<T>(MessageMeta meta, in T message) where T : struct, IMessage
        {
            if (T.Kind == MessageKind.Identity)
            {
                // identity is state, not a message, to the plugins: it's in the cache now and goes out
                // (inline for legacy, as its own message for JFP2) with the object's next position
                return;
            }
            NodeId target = meta.Recipient;
            IProtocolPlugin plugin = Route(target, T.Kind);
            if (plugin == null)
            {
                Log(NetLogLevel.Network, "NETWORK: No route to translate " + T.Kind + " from " + meta.Sender + " to " + target);
                return;
            }
            meta.EndPoint = null;
            meta.Forwarded = false;
            plugin.Send(meta, message, new ReadOnlySpan<NodeId>(in target));
        }

        // ------------------------------------------------------------------ send

        /// <summary>Send to specific nodes; each gets it through its own best plugin.</summary>
        public void Send<T>(MessageMeta meta, in T message, ReadOnlySpan<NodeId> recipients) where T : struct, IMessage
        {
            if (!IsOpen)
            {
                return;
            }
            if (!meta.Sender.Valid())
            {
                meta.Sender = Identity.Id;
            }
            if (meta.EndPoint != null)
            {
                SendToEndPointCore(meta, message);
                return;
            }
            if (recipients.Length == 1)
            {
                Route(recipients[0], T.Kind)?.Send(meta, message, recipients);
                return;
            }
            // group recipients by plugin, preserving their order within each group
            int count = plugins.Count;
            NodeId[] buffer = ArrayPool<NodeId>.Shared.Rent(recipients.Length * count);
            Span<int> used = stackalloc int[count];
            try
            {
                foreach (NodeId recipient in recipients)
                {
                    int index = RouteIndex(recipient, T.Kind);
                    if (index >= 0)
                    {
                        buffer[index * recipients.Length + used[index]++] = recipient;
                    }
                }
                for (int index = 0; index < count; index++)
                {
                    if (used[index] > 0)
                    {
                        plugins[index].Send(meta, message, new ReadOnlySpan<NodeId>(buffer, index * recipients.Length, used[index]));
                    }
                }
            }
            finally
            {
                ArrayPool<NodeId>.Shared.Return(buffer);
            }
        }

        public void SendTo<T>(NodeId recipient, in T message, bool guaranteed) where T : struct, IMessage =>
            Send(MessageMeta.To(recipient, guaranteed), message, new ReadOnlySpan<NodeId>(in recipient));

        /// <summary>Send to a raw endpoint (e.g. a hub or joiner that isn't a known node yet).</summary>
        public void SendToEndPoint<T>(IPEndPoint endPoint, in T message, bool guaranteed, NodeId recipient = default) where T : struct, IMessage =>
            Send(MessageMeta.ToEndPoint(endPoint, guaranteed, recipient), message, ReadOnlySpan<NodeId>.Empty);

        /// <summary>Send to every node in the session, in peer-table order.</summary>
        public void Broadcast<T>(in T message, bool guaranteed) where T : struct, IMessage
        {
            if (Peers.Count == 0)
            {
                return;
            }
            NodeId[] all = ArrayPool<NodeId>.Shared.Rent(Peers.Count);
            try
            {
                int n = 0;
                foreach (Peer peer in Peers.All) all[n++] = peer.Id;
                Send(new MessageMeta { Guaranteed = guaranteed }, message, new ReadOnlySpan<NodeId>(all, 0, n));
            }
            finally
            {
                ArrayPool<NodeId>.Shared.Return(all);
            }
        }

        void SendToEndPointCore<T>(in MessageMeta meta, in T message) where T : struct, IMessage
        {
            // a known node behind this endpoint gets its negotiated protocol; anyone else the baseline one
            NodeId known = meta.Recipient;
            if (!known.Valid() || !Peers.Contains(known))
            {
                known = Peers.FindByEndPoint(meta.EndPoint)?.Id ?? default;
            }
            IProtocolPlugin plugin = known.Valid() ? Route(known, T.Kind) : RouteUnknown(default, T.Kind);
            plugin?.Send(meta, message, ReadOnlySpan<NodeId>.Empty);
        }

        // ------------------------------------------------------------------ routing

        const sbyte NoRoute = -2;

        int RouteIndex(NodeId peerId, MessageKind kind)
        {
            if (Peers.TryGet(peerId, out Peer peer))
            {
                sbyte cached = peer.Routes[(int)kind];
                if (cached == -1)
                {
                    cached = NoRoute;
                    for (int i = 0; i < plugins.Count; i++)
                    {
                        if (plugins[i].CanCarry(peerId, kind))
                        {
                            cached = (sbyte)i;
                            break;
                        }
                    }
                    peer.Routes[(int)kind] = cached;
                }
                return cached;
            }
            for (int i = 0; i < plugins.Count; i++)
            {
                if (plugins[i].CanCarry(peerId, kind)) return i;
            }
            return NoRoute;
        }

        /// <summary>The plugin that will carry <paramref name="kind"/> to <paramref name="peerId"/> (null if none can).</summary>
        public IProtocolPlugin Route(NodeId peerId, MessageKind kind)
        {
            int index = RouteIndex(peerId, kind);
            return index >= 0 ? plugins[index] : null;
        }

        IProtocolPlugin RouteUnknown(NodeId peerId, MessageKind kind)
        {
            foreach (IProtocolPlugin plugin in plugins)
            {
                if (plugin.CanCarry(peerId, kind)) return plugin;
            }
            return null;
        }

        public void LinkChanged(NodeId peer)
        {
            // a link change alters what every peer routed through that node can use, and link changes
            // are rare, so drop every cached route rather than work out who depends on whom
            foreach (Peer p in Peers.All)
            {
                p.InvalidateRoutes();
            }
        }

        // ------------------------------------------------------------------ periodic / lifecycle

        public void Tick()
        {
            if (!IsOpen)
            {
                return;
            }
            Mesh.Tick();
            foreach (IProtocolPlugin plugin in plugins)
            {
                plugin.Tick();
            }
        }

        public bool TryAcquireRelay(NodeId sender) => Mesh.TryAcquireRelay(sender);

        internal void RemovePeer(Peer peer)
        {
            foreach (IProtocolPlugin plugin in plugins)
            {
                plugin.OnPeerRemoved(peer);
            }
            Peers.Remove(peer.Id);
            Objects.RemoveOwner(peer.Id);
            RaisePeerLeft(peer.Id);
        }

        internal void ResetSession()
        {
            foreach (IProtocolPlugin plugin in plugins)
            {
                plugin.OnSessionReset();
            }
        }

        internal void RaiseSessionJoined() => sink.OnEvent(new NetworkEvent { Kind = NetworkEventKind.SessionJoined });
        internal void RaisePeerJoined(NodeId id, IPEndPoint endPoint) => sink.OnEvent(new NetworkEvent { Kind = NetworkEventKind.PeerJoined, Node = id, EndPoint = endPoint });
        internal void RaisePeerEstablished(NodeId id) => sink.OnEvent(new NetworkEvent { Kind = NetworkEventKind.PeerEstablished, Node = id });
        internal void RaisePeerLeft(NodeId id) => sink.OnEvent(new NetworkEvent { Kind = NetworkEventKind.PeerLeft, Node = id });

        public void Log(NetLogLevel level, string text) => sink.OnEvent(new NetworkEvent { Kind = NetworkEventKind.Log, Level = level, Text = text });
    }
}
