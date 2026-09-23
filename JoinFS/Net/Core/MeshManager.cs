using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Mail;

namespace JoinFS.Net
{
    public enum SessionState
    {
        Unconnected,
        Connecting,
        Connected,
    }

    /// <summary>
    /// Session membership, liveness and route discovery: create/join/login/leave, who is in the
    /// session (JoinReply node lists, AddNode introductions), pulses (RTT, expiry, "established"),
    /// and pathfinding through relays when a node can't be reached directly.
    ///
    /// Protocol-neutral: it sends and receives canonical mesh messages through the core's router,
    /// so which wire carries them is a plugin decision (today only the legacy plugin does; see
    /// docs/network-plugin-architecture.md §2.4 for what a JFP2 mesh would add). The rules and
    /// timings are a faithful port of LocalNode's, because released v26.5 peers depend on them.
    ///
    /// Network thread only.
    /// </summary>
    public sealed class MeshManager : IMessageHandler
    {
        public const int ExpireTime = 30;
        public const int MaxNodesPerDevice = 32;
        public const int MaxRoutingNodes = 10;
        const int MaxPathfinderNodes = 100;
        const double PulseInterval = 1;
        const double PathfinderInterval = 5;
        const double RelayHoldTime = 5;

        readonly NetworkCore core;
        readonly Func<string, CredentialStore> credentialStoreFactory;

        uint suid;
        bool active;
        uint passwordHash;
        bool allowJoin = true;
        CredentialStore credentials;

        double nextPulse;
        double nextPathfinder;
        int pathfinderCount;

        readonly Dictionary<NodeId, double> relayNodes = [];
        readonly List<NodeId> removeList = [];
        readonly List<NodeId> removeRelayList = [];

        public MeshManager(NetworkCore core, Func<string, CredentialStore> credentialStoreFactory)
        {
            this.core = core;
            this.credentialStoreFactory = credentialStoreFactory;
        }

        public uint Suid => suid;
        public bool Connected => suid != 0;
        public bool GlobalSession => suid == 1;
        public SessionState State => suid != 0 ? SessionState.Connected : active ? SessionState.Connecting : SessionState.Unconnected;
        public bool Creator { get; private set; }
        public bool LoginRequired { get; private set; }
        public bool PasswordProtected => passwordHash != 0;
        public JoinResult ActiveJoinResult { get; private set; } = JoinResult.Accepted;
        public LoginResult ActiveLoginResult { get; private set; } = LoginResult.Accepted;
        public int RelayCount => relayNodes.Count;

        /// <summary>For tests: credential store in use (null unless a login-required session was created).</summary>
        public CredentialStore Credentials => credentials;

        NodeId LocalId => core.Identity.Id;
        double Now => core.Clock.Now;

        // ------------------------------------------------------------------ commands

        /// <summary>Start a new session (or the global one) with this node as its first member.</summary>
        public void Create(bool globalSession, uint password, bool loginRequired, string credentialsFolder)
        {
            if (!core.IsOpen || !core.Identity.Ready)
            {
                return;
            }
            if (globalSession)
            {
                suid = 1;
                passwordHash = 0;
            }
            else
            {
                suid = (uint)core.Clock.Timestamp;
                // 0 = no session, 1 = global
                while (suid <= 1) suid++;
                passwordHash = password;
                Creator = true;
                LoginRequired = loginRequired;
                if (loginRequired)
                {
                    credentials?.Dispose();
                    credentials = credentialStoreFactory(credentialsFolder);
                    credentials.Start();
                }
            }
            allowJoin = true;
            active = true;
        }

        public void Join(IPEndPoint endPoint, uint password)
        {
            if (!core.IsOpen || !core.Identity.Ready)
            {
                return;
            }
            core.SendToEndPoint(endPoint, new JoinRequest { PasswordHash = password }, guaranteed: true);
            passwordHash = password;
            ActiveJoinResult = JoinResult.Accepted;
            ActiveLoginResult = LoginResult.Accepted;
            Creator = false;
            allowJoin = true;
            active = true;
        }

        public void Login(IPEndPoint endPoint, string email, uint hash, bool verify)
        {
            if (!core.IsOpen || !core.Identity.Ready)
            {
                return;
            }
            core.SendToEndPoint(endPoint, new LoginRequest { Email = email, PasswordHash = hash, Verify = verify }, guaranteed: true);
            passwordHash = 0;
            ActiveJoinResult = JoinResult.Accepted;
            ActiveLoginResult = LoginResult.Accepted;
            Creator = false;
            allowJoin = false;
            active = true;
        }

        public void Leave()
        {
            if (!core.IsOpen)
            {
                return;
            }
            core.Broadcast(new Leave { Suid = suid }, guaranteed: false);
            // every plugin forgets its per-session state (the legacy LocalNode only cleared its own
            // guaranteed lists here, leaking JFP2 sessions and relay slots into the next session)
            core.ResetSession();
            foreach (Peer peer in core.Peers.All)
            {
                core.RaisePeerLeft(peer.Id);
            }
            core.Peers.Clear();
            core.Objects.Clear();
            relayNodes.Clear();
            removeList.Clear();
            removeRelayList.Clear();
            suid = 0;
            active = false;
            passwordHash = 0;
            Creator = false;
            credentials?.Dispose();
            credentials = null;
            allowJoin = true;
        }

        /// <summary>Test hook: put the mesh straight into a session with the given settings.</summary>
        internal void ForceSession(uint sessionId, uint password = 0, bool loginRequired = false, bool creator = false, CredentialStore store = null)
        {
            suid = sessionId;
            passwordHash = password;
            LoginRequired = loginRequired;
            Creator = creator;
            credentials = store;
            active = true;
        }

        // ------------------------------------------------------------------ periodic

        public void Tick()
        {
            if (State == SessionState.Unconnected)
            {
                return;
            }
            ExpireNodes();
            DoPulse();
            DoRouting();
        }

        void ExpireNodes()
        {
            double now = Now;
            foreach (Peer peer in core.Peers.All)
            {
                if (now > peer.ExpireTime)
                {
                    removeList.Add(peer.Id);
                }
            }
            foreach (NodeId id in removeList)
            {
                if (!core.Peers.TryGet(id, out Peer gone))
                {
                    continue;
                }
                // anyone routed through the departed node has to find a new route
                foreach (Peer peer in core.Peers.All)
                {
                    if (peer.RouteEndPoint.Equals(gone.EndPoint))
                    {
                        peer.SendEstablished = false;
                        peer.RouteEndPoint = peer.EndPoint;
                        peer.InvalidateRoutes();
                    }
                }
                core.RemovePeer(gone);
            }
            removeList.Clear();
        }

        internal void DoPulse()
        {
            if (!Connected || Now <= nextPulse)
            {
                return;
            }
            core.Broadcast(new Pulse { Suid = suid, Time = core.Clock.Timestamp, LowBandwidth = core.LowBandwidth }, guaranteed: false);
            nextPulse = Now + PulseInterval;
        }

        internal void DoRouting()
        {
            if (!Connected)
            {
                return;
            }
            if (Now > nextPathfinder)
            {
                pathfinderCount++;
                // every 8th round, also look for a direct path to nodes we reach through a relay
                bool checkDirect = (pathfinderCount & 0x7) == 0;
                List<NodeId> wanted = [];
                foreach (Peer peer in core.Peers.All)
                {
                    if (!peer.SendEstablished || (checkDirect && !peer.Direct))
                    {
                        wanted.Add(peer.Id);
                        if (wanted.Count >= MaxPathfinderNodes) break;
                    }
                }
                if (wanted.Count > 0)
                {
                    var request = new Pathfinder { Suid = suid, Nodes = wanted };
                    // to the node itself (not its route), unaddressed, like LocalNode.DoRouting
                    foreach (Peer peer in core.Peers.All)
                    {
                        if (checkDirect || (peer.SendEstablished && peer.Direct))
                        {
                            core.SendToEndPoint(peer.EndPoint, request, guaranteed: false);
                            core.Log(NetLogLevel.Network, "NETWORK: Routing " + peer.Id + " " + peer.EndPoint + " " + checkDirect + " " + peer.SendEstablished);
                        }
                    }
                }
                nextPathfinder = Now + PathfinderInterval;
            }
            double now = Now;
            foreach (var entry in relayNodes)
            {
                if (now > entry.Value) removeRelayList.Add(entry.Key);
            }
            foreach (NodeId id in removeRelayList)
            {
                relayNodes.Remove(id);
            }
            removeRelayList.Clear();
        }

        public bool TryAcquireRelay(NodeId sender)
        {
            if (relayNodes.Count < MaxRoutingNodes || relayNodes.ContainsKey(sender))
            {
                relayNodes[sender] = Now + RelayHoldTime;
                return true;
            }
            return false;
        }

        // ------------------------------------------------------------------ membership

        int NodeCountDevice(NodeId id) => (NodeId.SameDevice(LocalId, id) ? 1 : 0) + core.Peers.CountSameDevice(id);

        /// <summary>
        /// Record that <paramref name="id"/> exists (and, if <paramref name="receive"/>, that it
        /// just sent us something on <paramref name="port"/>). On first contact, tell the app and
        /// introduce the node to everyone else.
        /// </summary>
        void RegisterNode(NodeId id, ushort port, bool receive, bool direct)
        {
            if (!id.Valid() || id == LocalId || NodeCountDevice(id) >= MaxNodesPerDevice)
            {
                return;
            }
            bool firstContact = false;
            if (core.Peers.TryGet(id, out Peer peer))
            {
                if (receive)
                {
                    if (!peer.ReceiveEstablished) firstContact = true;
                    peer.ReceiveEstablished = true;
                    if (direct)
                    {
                        // mutates the shared IPEndPoint instance, exactly like LocalNode (the route
                        // endpoint is the same object until a relay is chosen)
                        peer.EndPoint.Port = port;
                    }
                }
                core.Log(NetLogLevel.Network, "NETWORK: RegisterNode update " + id + " " + port + " " + receive + " " + direct + " " + firstContact);
            }
            else
            {
                if (receive) firstContact = true;
                peer = core.Peers.Add(id, core.Identity.MakeEndPoint(id, port), receive);
                peer.ExpireTime = Now + ExpireTime;
                core.Log(NetLogLevel.Network, "NETWORK: RegisterNode new " + id + " " + port + " " + receive + " " + direct);
            }
            if (firstContact)
            {
                core.RaisePeerJoined(id, peer.EndPoint);
                core.Broadcast(new AddNode { Suid = suid, Node = new KnownNode { Node = id, Port = (ushort)peer.EndPoint.Port } }, guaranteed: true);
            }
        }

        static void Responded(Peer peer, double now)
        {
            peer.SendEstablished = true;
            peer.ExpireTime = now + ExpireTime;
        }

        List<KnownNode> KnownNodes()
        {
            List<KnownNode> nodes = [];
            foreach (Peer other in core.Peers.All)
            {
                if (other.ReceiveEstablished)
                {
                    nodes.Add(new KnownNode { Node = other.Id, Port = (ushort)other.EndPoint.Port });
                }
            }
            return nodes;
        }

        // ------------------------------------------------------------------ message handlers

        public void Handle(in MessageMeta meta, in JoinRequest message)
        {
            core.Log(NetLogLevel.Network, "NETWORK: Join Message - " + meta.Sender + " - " + meta.EndPoint);
            if (!Connected || !allowJoin)
            {
                return;
            }
            if (NodeCountDevice(meta.Sender) >= MaxNodesPerDevice)
            {
                core.Log(NetLogLevel.Network, "NETWORK: Exceeded MAX_NODES_PER_DEVICE - " + meta.Sender + " - " + meta.EndPoint);
                return;
            }
            if (passwordHash != 0 && message.PasswordHash != passwordHash)
            {
                core.SendToEndPoint(meta.EndPoint, new JoinFail { Result = JoinResult.PasswordRequired }, guaranteed: true);
            }
            else if (LoginRequired)
            {
                core.SendToEndPoint(meta.EndPoint, new JoinFail { Result = JoinResult.LoginRequired }, guaranteed: true);
            }
            else
            {
                core.SendToEndPoint(meta.EndPoint, new JoinReply { Suid = suid, Nodes = KnownNodes() }, guaranteed: true);
                RegisterNode(meta.Sender, (ushort)meta.EndPoint.Port, true, !meta.Forwarded);
            }
        }

        public void Handle(in MessageMeta meta, in JoinReply message)
        {
            if (!active)
            {
                return;
            }
            if (!Connected)
            {
                suid = message.Suid;
                core.RaiseSessionJoined();
            }
            if (message.Suid == suid)
            {
                foreach (KnownNode node in message.Nodes)
                {
                    if (node.Node != LocalId)
                    {
                        RegisterNode(node.Node, node.Port, false, false);
                    }
                }
                RegisterNode(meta.Sender, (ushort)meta.EndPoint.Port, true, !meta.Forwarded);
            }
            core.Log(NetLogLevel.Network, "NETWORK: JoinReply " + meta.Sender + " " + message.Suid);
        }

        public void Handle(in MessageMeta meta, in JoinFail message) => ActiveJoinResult = message.Result;

        public void Handle(in MessageMeta meta, in LoginFail message) => ActiveLoginResult = message.Result;

        public void Handle(in MessageMeta meta, in LoginRequest message)
        {
            core.Log(NetLogLevel.Network, "NETWORK: Login Message - " + meta.Sender + " - " + meta.EndPoint);
            if (NodeCountDevice(meta.Sender) >= MaxNodesPerDevice)
            {
                core.Log(NetLogLevel.Network, "NETWORK: Exceeded MAX_NODES_PER_DEVICE - " + meta.Sender + " - " + meta.EndPoint);
                return;
            }
            if (!Connected || !Creator || !LoginRequired || credentials == null)
            {
                return;
            }
            MailAddress address;
            try
            {
                address = new MailAddress(message.Email);
            }
            catch
            {
                // (LocalNode wrote this reply into a stale send buffer; send it properly)
                core.SendToEndPoint(meta.EndPoint, new LoginFail { Result = LoginResult.InvalidAddress }, guaranteed: true);
                return;
            }
            LoginResult result = credentials.Check(address, message.PasswordHash, message.Verify);
            if (result != LoginResult.Accepted)
            {
                core.SendToEndPoint(meta.EndPoint, new LoginFail { Result = result }, guaranteed: true);
                return;
            }
            core.SendToEndPoint(meta.EndPoint, new JoinReply { Suid = suid, Nodes = KnownNodes() }, guaranteed: true);
            RegisterNode(meta.Sender, (ushort)meta.EndPoint.Port, true, !meta.Forwarded);
        }

        public void Handle(in MessageMeta meta, in Leave message)
        {
            if (Connected && message.Suid == suid)
            {
                removeList.Add(meta.Sender);
                removeRelayList.Add(meta.Sender);
            }
            core.Log(NetLogLevel.Network, "NETWORK: Leave " + meta.Sender + " " + message.Suid);
        }

        public void Handle(in MessageMeta meta, in AddNode message)
        {
            if (Connected && message.Suid == suid)
            {
                RegisterNode(message.Node.Node, message.Node.Port, false, false);
                core.Log(NetLogLevel.Network, "NETWORK: AddNode " + meta.Sender + " " + message.Suid + " " + message.Node.Node + " " + message.Node.Port);
            }
        }

        public void Handle(in MessageMeta meta, in Pulse message)
        {
            if (!Connected || message.Suid != suid)
            {
                return;
            }
            if (core.Peers.TryGet(meta.Sender, out Peer sender))
            {
                sender.LowBandwidth = message.LowBandwidth;
            }
            RegisterNode(meta.Sender, (ushort)meta.EndPoint.Port, true, !meta.Forwarded);
            core.SendToEndPoint(meta.EndPoint, new PulseResponse { Time = message.Time }, guaranteed: false, recipient: meta.Sender);
            core.Log(NetLogLevel.Network, "NETWORK: Pulse " + meta.Sender + " " + meta.EndPoint);
        }

        public void Handle(in MessageMeta meta, in PulseResponse message)
        {
            if (!Connected || !core.Peers.TryGet(meta.Sender, out Peer peer))
            {
                return;
            }
            peer.Rtt = (core.Clock.Timestamp - message.Time) / (float)core.Clock.Frequency;
            RegisterNode(meta.Sender, (ushort)meta.EndPoint.Port, true, !meta.Forwarded);
            if (!peer.SendEstablished)
            {
                core.RaisePeerEstablished(meta.Sender);
            }
            Responded(peer, Now);
            core.Log(NetLogLevel.Network, "NETWORK: PulseResponse " + meta.Sender + " " + meta.EndPoint);
        }

        public void Handle(in MessageMeta meta, in Pathfinder message)
        {
            if (!Connected || meta.Forwarded || message.Suid != suid)
            {
                return;
            }
            RegisterNode(meta.Sender, (ushort)meta.EndPoint.Port, true, true);
            List<NodeId> reachable = [];
            foreach (NodeId id in message.Nodes)
            {
                if (id == LocalId)
                {
                    reachable.Add(id);
                }
                else if (core.Peers.TryGet(id, out Peer peer) && peer.SendEstablished && peer.Direct
                    && relayNodes.Count + reachable.Count < MaxRoutingNodes)
                {
                    reachable.Add(id);
                }
            }
            if (reachable.Count > 0)
            {
                core.SendToEndPoint(meta.EndPoint, new PathfinderResponse { Suid = suid, Nodes = reachable }, guaranteed: false);
            }
            core.Log(NetLogLevel.Network, "NETWORK: PathFinder " + meta.Sender + " " + meta.EndPoint + " " + message.Nodes.Count + " " + reachable.Count);
        }

        public void Handle(in MessageMeta meta, in PathfinderResponse message)
        {
            if (!Connected || meta.Forwarded || message.Suid != suid)
            {
                return;
            }
            RegisterNode(meta.Sender, (ushort)meta.EndPoint.Port, true, true);
            double now = Now;
            foreach (NodeId id in message.Nodes)
            {
                if (!core.Peers.TryGet(id, out Peer peer))
                {
                    continue;
                }
                if (id == meta.Sender)
                {
                    // the responder itself: it's directly reachable
                    peer.RouteEndPoint = peer.EndPoint;
                    Responded(peer, now);
                    peer.InvalidateRoutes();
                    core.Log(NetLogLevel.Network, "NETWORK: PathFinderResponse Direct " + meta.Sender + " " + peer.EndPoint);
                }
                else if (!peer.SendEstablished)
                {
                    // reach it through the responder
                    peer.RouteEndPoint = meta.EndPoint;
                    Responded(peer, now);
                    peer.InvalidateRoutes();
                    core.Log(NetLogLevel.Network, "NETWORK: PathFinderResponse Indirect " + meta.Sender + " " + meta.EndPoint);
                }
            }
            core.Log(NetLogLevel.Network, "NETWORK: PathFinderResponse " + meta.Sender + " " + message.Nodes.Count);
        }
    }
}
