using JoinFS.Net;
using JoinFS.Net.Jfp2;
using JoinFS.Properties;
using System;
using System.Collections.Generic;
using System.Net;


namespace JoinFS
{
    /// <summary>
    /// The application's network session. It owns the network stack (<see cref="NetworkService"/>,
    /// which runs the protocol plugins on its own thread) and the session's parts, all on the app
    /// thread (Main.DoWork, under conch):
    /// <list type="bullet">
    /// <item><see cref="Bootstrap"/>: this node's addresses, seed hubs, ban list, DNS.</item>
    /// <item><see cref="Peers"/>: what the nodes in the session told us about themselves; cockpit sharing.</item>
    /// <item><see cref="SimSender"/> / <see cref="SimIngest"/>: the simulator's state out, and other nodes' objects in.</item>
    /// <item><see cref="Hubs"/>, <see cref="HubHost"/>, <see cref="Users"/>: the hub directory, this node as a hub, users by uuid.</item>
    /// <item><see cref="Comms"/>: text comms.</item>
    /// </list>
    /// This class itself runs the session commands (join, login, create, leave), routes each
    /// incoming message to the part that owns it, and runs the parts each tick in a fixed order.
    /// It knows nothing about wire protocols. See docs/reference/joinfs-architecture.md §7.
    /// </summary>
    public class Network : IMessageHandler, INetworkEventHandler, ISessionState
    {
        public const int MAX_ADDRESS_LENGTH = 40;
        public const int MAX_NICKNAME_LENGTH = 20;
        public const int MAX_PASSWORD_LENGTH = 24;
        public const int MAX_HUB_NAME_LENGTH = 25;
        public const int MAX_HUB_ABOUT_LENGTH = 40;
        public const int MAX_HUB_VOIP_LENGTH = 40;
        public const int MAX_HUB_EVENT_LENGTH = 30;

        public const ushort DEFAULT_PORT = 6112;

        /// <summary>The network stack (protocol plugins, mesh, socket) running on its own thread.</summary>
        public readonly NetworkService service;

        public readonly NetBootstrap Bootstrap;
        public readonly PeerTable Peers;
        public readonly SimSender SimSender;
        public readonly HubDirectory Hubs;
        public readonly UserDirectory Users;
        public readonly HubHost HubHost;
        public readonly SessionComms Comms;
        readonly SimIngest simIngest;

        readonly MainSessionHost host;

        public Network(Main main)
        {
            host = new MainSessionHost(main);
            host.Event("Unique address is " + UserDirectory.UuidToString(main.uuid));

            service = new NetworkService(credentialStoreFactory: folder => new CredentialStore(main.documentsPath, main.MonitorEvent));
            // the protocols this build speaks (legacy is built in; newer ones win per peer and
            // message kind where both sides negotiated them). JFP2 tells each neighbor which build
            // this is, e.g. "26.6.0 JoinFS-FS2024".
            service.AddPlugin(new Jfp2Plugin((ushort)System.Diagnostics.Stopwatch.GetTimestamp(), Main.Version + " " + Main.Name));

            Bootstrap = new NetBootstrap(service, host, main.settingsLocalAddress, Settings.Default.MyIp, host.Now);
            Peers = new PeerTable(service, this, host, host, host, main.log, host, host, host);
            simIngest = new SimIngest(host, this, host, main.log, Peers, service, host);
            SimSender = new SimSender(service, this, host);
            Hubs = new HubDirectory(service, this, host, main.log, Bootstrap, host, host, host);
            Users = new UserDirectory(service, this, host, Hubs, Bootstrap, host, host, host);
            HubHost = new HubHost(service, this, host, host, Peers, Hubs, Users, host, host);
            Comms = new SessionComms(service, () => main.notes, host, host);

            // what one part learns that another acts on
            Peers.HubAnnounced += Hubs.SubmitHub;
            Hubs.GlobalHubFound += endPoint => Join(endPoint, 0);

#if !NO_HUBS
            Hubs.Load();
#endif
            Bootstrap.DownloadAll(hubs:
#if !NO_HUBS
                true
#else
                false
#endif
            );
            host.HubsChanged(5);

            service.Start();
        }

        // ------------------------------------------------------------------ ISessionState

        /// <summary>Latest picture of the network thread's state (lock-free, at most ~100 ms old).</summary>
        public NetworkSnapshot Snapshot => service.Snapshot;

        /// <summary>This node's id (invalid until the public address is known).</summary>
        public NodeId LocalId => Bootstrap.LocalId;

        /// <summary>In a session.</summary>
        public bool Connected => Snapshot.Connected;

        /// <summary>This node's public address is known, so it can join or create a session.</summary>
        public bool Ready => LocalId.Valid();

        // ------------------------------------------------------------------ transport

        /// <summary>Open (or move to) the UDP port.</summary>
        public bool Open(int port)
        {
            if (!service.Open(port, out string error))
            {
                host.Event(error);
                return false;
            }
            // moving to a new port: the network thread already left any session on the old one
            Bootstrap.SetPort((ushort)service.Port);
            return true;
        }

        /// <summary>Leave any session and stop the network thread.</summary>
        public void Shutdown()
        {
            Leave();
            service.Close();
            service.Stop();
        }

        /// <summary>Ask for (or stop asking for) reduced traffic.</summary>
        public bool LowBandwidth
        {
            get => Snapshot.LowBandwidth;
            set => service.Post(core => core.LowBandwidth = value);
        }

        /// <summary>Ids of the nodes currently in the session, in session order.</summary>
        public NodeId[] PeerIds()
        {
            IReadOnlyList<PeerSnapshot> peers = Snapshot.PeerList;
            NodeId[] ids = new NodeId[peers.Count];
            for (int i = 0; i < ids.Length; i++) ids[i] = peers[i].Id;
            return ids;
        }

        /// <summary>Round-trip time to a node in seconds.</summary>
        public float GetNodeRTT(NodeId nuid) => Snapshot.Rtt(nuid);

        /// <summary>A node asked for reduced traffic.</summary>
        public bool NodeLowBandwidth(NodeId nuid) => Snapshot.Peer(nuid)?.LowBandwidth ?? false;

        // ------------------------------------------------------------------ work

        /// <summary>Process the session (app thread, under conch). The order matters: see each part.</summary>
        public void DoWork()
        {
            // everything the network thread received since the last tick, in order
            service.Drain(this, this);

            if (Ready)
            {
                DoSessionCommands();
                Peers.DoScheduled();
            }
            Bootstrap.DoWork(host.Now, host.Hub);
            foreach (IPEndPoint seed in Bootstrap.TakeSeedHubs())
            {
                Hubs.SubmitHub(seed);
            }
            Peers.DoWork();
            Users.DoAddressBook();
#if !NO_HUBS
            Users.DoOnlineUsers();
            Hubs.DoWork();
            HubHost.DoLocalUsers();
            Hubs.DoUserLists();
#endif
        }

        // ------------------------------------------------------------------ session commands
        //
        // Scheduled from any thread (UI), run on the next tick.

        /// <summary>The endpoint last joined.</summary>
        public IPEndPoint joinEndPoint = new(0, 0);

        /// <summary>
        /// Which method established the current session - decides what CheckForOrphanedSession
        /// replays on an automatic reconnect.
        /// </summary>
        internal enum SessionOrigin { None = 0, Join = 1, Login = 2, Global = 3 }
        SessionOrigin sessionOrigin = SessionOrigin.None;

        /// <summary>
        /// Credentials of the last Join/Login, kept in memory for as long as the app runs so the automatic
        /// reconnect (see CheckForOrphanedSession) can replay them without asking again - see
        /// tuduce/JoinFS#177 and the discussion on PR #179 about sessions that require login
        /// (https://github.com/tuduce/JoinFS/pull/179#issuecomment-5937299466). Neither Join nor Login ever
        /// receives a plaintext password - the UI hashes it before scheduling - so this only ever holds a
        /// hash (plus, for Login, the non-secret email address), never a reversible secret. Never written to
        /// Settings, Log, or any file.
        /// </summary>
        uint lastPasswordHash = 0;
        string lastLoginEmail = "";
        uint lastLoginHash = 0;
        bool lastLoginVerify = false;

        /// <summary>How often to retry while the session is lost - see CheckForOrphanedSession.</summary>
        const double RECONNECT_INTERVAL_S = 10.0;

        /// <summary>How long to keep retrying before giving up and leaving the next step to the user.</summary>
        const double RECONNECT_MAX_S = 5 * 60.0;

        readonly ReconnectSupervisor reconnectSupervisor = new(RECONNECT_INTERVAL_S, RECONNECT_MAX_S);

        /// <summary>
        /// True while a lost session is being re-established in the background - same "Waiting" signal
        /// Sim.View.Connecting gives Button_Simulator, for Button_Network to show the same three-state
        /// palette instead of reading as a healthy Connected, or a final Unconnected, while it retries.
        /// </summary>
        public bool Reconnecting => reconnectSupervisor.Pending;

        /// <summary>
        /// See tuduce/JoinFS#177: a client can be left reporting Connected with zero live peers (hub
        /// restart, a network drop that outlasts every peer's expiry) with nothing ever retrying the
        /// join/login on its own. Detect that and re-run whatever established the session - Join, Login or
        /// the global session, with the same credentials - every RECONNECT_INTERVAL_S for up to
        /// RECONNECT_MAX_S, until a fresh JoinReply/LoginReply repopulates the peer list or the user leaves.
        /// A session this node created rather than joined (joinEndPoint never set, see Create()) has no
        /// join target and is therefore never considered lost here - losing every client is that session's
        /// normal idle state, not an error.
        /// </summary>
        void CheckForOrphanedSession()
        {
            bool hasJoinTarget = joinEndPoint.Port != 0;
            NetworkSnapshot snapshot = Snapshot;

            switch (reconnectSupervisor.Tick(hasJoinTarget, snapshot.State, snapshot.PeerList.Count, host.Now))
            {
                case ReconnectAction.Retry:
                    RetrySession();
                    break;
                case ReconnectAction.GiveUp:
                    GiveUpReconnecting();
                    break;
            }
        }

        void RetrySession()
        {
            host.Event("Reconnecting to '" + AddressCodec.EncodeIP(joinEndPoint.ToString())
                + "' after losing every peer while still connected");

            IPEndPoint target = joinEndPoint;

            // Must reset the mesh before rejoining: our own Connected is still true (suid != 0, from
            // before every peer expired - that's the whole premise of a lost session), and
            // MeshManager.Handle(JoinReply) only adopts the hub's suid when !Connected. A restarted hub
            // hands out a fresh suid, so without this reset the reply's "message.Suid == suid" check
            // fails forever and RegisterNode never runs. Every manual rejoin avoids this because
            // Main.Join() always ScheduleLeave()s first. This is ResetSession(), not Leave(): the
            // reconnect in progress must survive the reset.
            ResetSession();

            switch (sessionOrigin)
            {
                case SessionOrigin.Join:
                    Join(target, lastPasswordHash);
                    break;
                case SessionOrigin.Login:
                    Login(target, lastLoginEmail, lastLoginHash, lastLoginVerify);
                    break;
                case SessionOrigin.Global:
                    StartGlobalSession();
                    break;
            }
        }

        void GiveUpReconnecting()
        {
            ResetSession();
            host.Event("Gave up reconnecting to '" + AddressCodec.EncodeIP(joinEndPoint.ToString())
                + "' after " + (int)(RECONNECT_MAX_S / 60) + " minutes");
            host.ShowMessage(string.Format(Resources.Strings.ReconnectGaveUp, (int)(RECONNECT_MAX_S / 60)));
            host.SessionChanged(1);
        }

        volatile IPEndPoint scheduleJoin = null;
        volatile uint schedulePasswordHash = 0;
        volatile bool scheduleJoinGlobal = false;
        volatile IPEndPoint scheduleLogin = null;
        volatile string scheduleLoginEmail = "";
        volatile uint scheduleLoginHash = 0;
        volatile bool scheduleLoginVerify = false;
        volatile bool scheduleLeave = false;
        volatile bool scheduleCreate = false;

        /// <summary>A join to a user (by uuid) is pending until the user's node is found.</summary>
        public bool scheduleJoinUser = false;
        uint scheduleJoinUuid = 0;

        public string ScheduleLoginEmail => scheduleLoginEmail;
        public uint ScheduleLoginHash => scheduleLoginHash;

        public void ScheduleJoin(IPEndPoint endPoint, uint passwordHash)
        {
            if (scheduleJoin == null)
            {
                scheduleJoin = endPoint;
                schedulePasswordHash = passwordHash;
            }
        }

        public void ScheduleJoinGlobal()
        {
#if !NO_GLOBAL
            scheduleJoinGlobal = true;
#endif
        }

        public void ScheduleLogin(IPEndPoint endPoint, string email, uint hash, bool verify)
        {
            if (scheduleLogin == null)
            {
                scheduleLogin = endPoint;
                scheduleLoginEmail = email;
                scheduleLoginHash = hash;
                scheduleLoginVerify = verify;
            }
        }

        /// <summary>Join a user's session: ask the hubs where the user is, and join once known.</summary>
        public void ScheduleJoinUser(uint uuid)
        {
            scheduleJoinUser = true;
            scheduleJoinUuid = uuid;
            Users.RequestNuid(uuid);
        }

        public void ScheduleLeave() => scheduleLeave = true;

        public void ScheduleCreate()
        {
#if !NO_CREATE
            scheduleCreate = true;
#endif
        }

        void DoSessionCommands()
        {
            if (scheduleLeave)
            {
                Leave();
                scheduleLeave = false;
            }

            // still reports Connected but has lost every peer (including the hub itself, if joined
            // directly) - see CheckForOrphanedSession. Runs after the Leave above so a just-processed
            // Leave is reflected immediately rather than retriggering a reconnect this same tick.
            CheckForOrphanedSession();

            if (scheduleJoinUser && Users.TryGetEndPoint(scheduleJoinUuid, out IPEndPoint userEndPoint))
            {
                Join(userEndPoint, 0);
                scheduleJoinUser = false;
            }

            if (scheduleCreate)
            {
                Create(false);
                scheduleCreate = false;
            }

            if (scheduleJoin != null)
            {
                Join(scheduleJoin, schedulePasswordHash);
                scheduleJoin = null;
                schedulePasswordHash = 0;
            }

            if (scheduleJoinGlobal)
            {
                StartGlobalSession();
                scheduleJoinGlobal = false;
            }

            if (scheduleLogin != null)
            {
                Login(scheduleLogin, scheduleLoginEmail, scheduleLoginHash, scheduleLoginVerify);
                scheduleLogin = null;
            }
        }

        /// <summary>Create the global session and join it on every hub running it.</summary>
        void StartGlobalSession()
        {
            Create(true);
            foreach (var hub in Hubs.List)
            {
                if (hub.globalSession)
                {
                    Join(hub.endPoint, 0);
                }
            }
            // after the joins, which each record themselves as the origin
            sessionOrigin = SessionOrigin.Global;
        }

        /// <summary>What joining and logging in have in common: remember where, and start the session.</summary>
        void StartSession(IPEndPoint endPoint, Action<NetworkCore> start)
        {
            joinEndPoint = endPoint;
            LowBandwidth = host.LowBandwidth;
            Comms.OnJoining();
            try
            {
#if DEBUG
                host.Event("Joining '" + AddressCodec.EncodeIP(endPoint.ToString()) + "'");
#endif
                service.Post(start);
                host.SessionChanged(5);
            }
            catch (Exception ex)
            {
                host.Event(ex.Message);
            }
        }

        void Join(IPEndPoint endPoint, uint passwordHash)
        {
            sessionOrigin = SessionOrigin.Join;
            lastPasswordHash = passwordHash;
            StartSession(endPoint, core => core.Mesh.Join(endPoint, passwordHash));
        }

        void Login(IPEndPoint endPoint, string email, uint hash, bool verify)
        {
            sessionOrigin = SessionOrigin.Login;
            lastLoginEmail = email;
            lastLoginHash = hash;
            lastLoginVerify = verify;
            StartSession(endPoint, core => core.Mesh.Login(endPoint, email, hash, verify));
        }

        /// <summary>
        /// Leave the session. Always posted - MeshManager.Leave() is a cheap no-op with nothing to
        /// leave (docs/network-plugin-architecture.md §2.11 item 2), so there's no need for a local
        /// "are we in a session" flag that could disagree with the network thread's own answer while
        /// the snapshot catches up. The event/refresh are still gated on the snapshot, since skipping
        /// them once in the rare race right after a Join is harmless; skipping the actual Leave
        /// wouldn't be.
        /// </summary>
        public void Leave()
        {
            reconnectSupervisor.NotifyUserLeft();
            sessionOrigin = SessionOrigin.None;
            if (Snapshot.State != SessionState.Unconnected)
            {
                host.Event("Left the session");
                host.SessionChanged(1);
            }
            ResetSession();
        }

        /// <summary>
        /// Drop the mesh session without touching the reconnect in progress or the remembered
        /// credentials: the automatic reconnect resets and rejoins with it, and a user Leave() is the
        /// one that cancels it.
        /// </summary>
        void ResetSession() => service.Post(core => core.Mesh.Leave());

        /// <summary>Create a session (or the global session) that others can join.</summary>
        public void Create(bool globalSession)
        {
            // a session of our own has no hub to go back to
            joinEndPoint = new IPEndPoint(0, 0);
            sessionOrigin = SessionOrigin.None;
            try
            {
                LowBandwidth = host.LowBandwidth;
                uint passwordHash = NetHash.HashPassword(host.Password.TrimStart(' ').TrimEnd(' '));
                string folder = host.DocumentsPath;
                service.Post(core => core.Mesh.Create(globalSession, passwordHash, loginRequired: false, folder));
                host.Event(globalSession ? "Joined global session" : "Created session");
                host.SessionChanged(5);
            }
            catch (Exception ex)
            {
                host.ShowMessage(ex.Message);
            }
        }

        // ------------------------------------------------------------------ incoming: events

        public void OnNetworkEvent(in NetworkEvent e)
        {
            switch (e.Kind)
            {
                case NetworkEventKind.SessionJoined:
                    Peers.OnSessionJoined();
                    break;
                case NetworkEventKind.PeerJoined:
                    Peers.OnPeerJoined(e.Node, e.EndPoint);
                    break;
                case NetworkEventKind.PeerEstablished:
                    Peers.OnPeerEstablished(e.Node);
                    Comms.OnPeerEstablished(e.Node);
                    break;
                case NetworkEventKind.PeerLeft:
                    Peers.OnPeerLeft(e.Node);
                    simIngest.OnPeerLeft(e.Node);
                    break;
                case NetworkEventKind.Log:
                    if (e.Level == NetLogLevel.Event) host.Event(e.Text);
                    else host.Network(e.Text);
                    break;
            }
        }

        // ------------------------------------------------------------------ incoming: messages
        //
        // Each canonical message to the part that owns it (mesh messages never get here).

        public void Handle(in MessageMeta meta, in IdentityUpdate message) => simIngest.Handle(meta, message);
        public void Handle(in MessageMeta meta, in PositionUpdate message) => simIngest.Handle(meta, message);
        public void Handle(in MessageMeta meta, in ObjectPositionUpdate message) => simIngest.Handle(meta, message);
        public void Handle(in MessageMeta meta, in VariableSyncUpdate message) => simIngest.Handle(meta, message);
        public void Handle(in MessageMeta meta, in EventUpdate message) => simIngest.Handle(meta, message);
        public void Handle(in MessageMeta meta, in RemoveObject message) => simIngest.Handle(meta, message);
        public void Handle(in MessageMeta meta, in FlightPlanUpdate message) => simIngest.Handle(meta, message);
        public void Handle(in MessageMeta meta, in WeatherRequest message) => simIngest.Handle(meta, message);
        public void Handle(in MessageMeta meta, in WeatherReply message) => simIngest.Handle(meta, message);
        public void Handle(in MessageMeta meta, in WeatherUpdate message) => simIngest.Handle(meta, message);

        public void Handle(in MessageMeta meta, in PeerInfo message) => Peers.Handle(meta, message);

        public void Handle(in MessageMeta meta, in StatusRequestUpdate message) => HubHost.Handle(meta, message);
        public void Handle(in MessageMeta meta, in UserListRequest message) => HubHost.Handle(meta, message);
        public void Handle(in MessageMeta meta, in UserPositionsRequest message) => HubHost.Handle(meta, message);

        public void Handle(in MessageMeta meta, in StatusUpdate message)
        {
            Hubs.Handle(meta, message);
            Users.Handle(meta, message);
        }
        public void Handle(in MessageMeta meta, in HubList message) => Hubs.Handle(meta, message);
        public void Handle(in MessageMeta meta, in HubUserUpdate message) => Hubs.Handle(meta, message);
        public void Handle(in MessageMeta meta, in UserPositions message) => Hubs.Handle(meta, message);

        public void Handle(in MessageMeta meta, in OnlineAnnouncement message) => Users.Handle(meta, message);
        public void Handle(in MessageMeta meta, in UserNuidRequest message) => Users.Handle(meta, message);
        public void Handle(in MessageMeta meta, in UserNuidReply message) => Users.Handle(meta, message);

        public void Handle(in MessageMeta meta, in CommsRequest message) => Comms.Handle(meta, message);
        public void Handle(in MessageMeta meta, in NotesBundle message) => Comms.Handle(meta, message);
    }
}
