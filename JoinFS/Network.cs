using JoinFS.Properties;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;


namespace JoinFS
{
    public class Network
    {
        const string HUB_LIST_FILE = "hubs.dat";
        const ushort HUB_LIST_VERSION = 10006;

        public const int MAX_ADDRESS_LENGTH = 40;
        public const int MAX_NICKNAME_LENGTH = 20;
        public const int MAX_PASSWORD_LENGTH = 24;
        public const int MAX_HUB_NAME_LENGTH = 25;
        public const int MAX_HUB_ABOUT_LENGTH = 40;
        public const int MAX_HUB_VOIP_LENGTH = 40;
        public const int MAX_HUB_EVENT_LENGTH = 30;

        public const ushort DEFAULT_PORT = 6112;

        public const int MAX_HUB_LIST_MESSAGE = 25;
        public const int MAX_USER_POSITIONS = 20;
        public const int MAX_INTEGER_VARIABLES = 100;
        public const int MAX_FLOAT_VARIABLES = 100;
        public const int MAX_STRING8_VARIABLES = 80;
        public const int MAX_IP_HUBS = 4;

        // remove hubs after this number of days
        public const int DEAD_HUB_DURATION = 7;

        // remove something if not updated after this time
        const double OFFLINE_TIME = 300.0;

        const float PENDING_HUB_EXPIRE_TIME = 172800.0f;

        public const int REQUEST_NUID_NUM_SAMPLES = 5;

        /// <summary>
        /// Timers
        /// </summary>
        readonly Timer sharedDataTimer = new(5.0);
        readonly Timer onlineUserTimer = new(60.0);
        readonly Timer addressBookTimer = new(60.0);
        readonly Timer hubsTimer = new(5.0);
        readonly Timer pendingHubsTimer = new(1800.0);
        readonly Timer hubUserListTimer = new(2.0);
        readonly Timer localUserListTimer = new(10.0);
        readonly Timer internetAddressTimer = new(10800.0);

        /// <summary>
        /// Hub list has recently changed
        /// </summary>
        bool hubListChanged = false;

        /// <summary>
        /// comms requests to make
        /// </summary>
        int commsRequests = 0;

        /// <summary>
        /// Reference to the main form
        /// </summary>
        readonly Main main;

        /// <summary>
        /// Vuids
        /// </summary>
        readonly uint vuidSquawk;
        readonly uint vuidIfr;

        /// <summary>
        /// Local node
        /// </summary>
        public LocalNode localNode;

        /// <summary>
        /// For seedhubs
        /// </summary>
        private static readonly HttpClient httpClient = new();
        string[] seedhubs = null;
        bool seedhubsFallback = false;

        private async Task DownloadSeedhubsAsync(string url)
        {
            try
            {
                var response = await httpClient.GetStringAsync(url);
                seedhubs = response.Split('\n');
                main?.MonitorEvent($"Seedhubs download complete from {url}");
            }
            catch (Exception ex)
            {
                main?.MonitorEvent($"Error downloading seedhubs from {url}: {ex.Message}");
                if (!seedhubsFallback)
                {
                    seedhubsFallback = true;
                    string fallBackUrl = "https://drive.google.com/uc?export=download&id=0Byn9605PQfMecnhwdUtITi1yYlk";
                    await DownloadSeedhubsAsync(fallBackUrl);
                }
                else
                {
                    seedhubs = [""];
                }
            }
        }

        /// <summary>
        /// For external IP
        /// </summary>
        string myip = null;
        bool myipFallback = false;

        private async Task DownloadMyIpAsync()
        {
            try
            {
                var response = await httpClient.GetStringAsync("https://checkip.amazonaws.com/");
                string result = response.TrimEnd('\n');
                if (IPAddress.TryParse(result, out IPAddress address) && address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                {
                    Settings.Default.MyIp = result;
                    myip = result;
                    return;
                }
                // fallback if needed
                if (!myipFallback)
                {
                    myipFallback = true;
                    await DownloadMyIpAsync("https://ipinfo.io/ip");
                }
                else
                {
                    myip = Settings.Default.MyIp;
                }
            }
            catch (Exception ex)
            {
                main?.MonitorEvent($"Error downloading IP address: {ex.Message}");
                if (!myipFallback)
                {
                    myipFallback = true;
                    await DownloadMyIpAsync("https://ipinfo.io/ip");
                }
                else
                {
                    myip = Settings.Default.MyIp;
                }
            }
        }

        private async Task DownloadMyIpAsync(string url)
        {
            try
            {
                var response = await httpClient.GetStringAsync(url);
                string result = response.TrimEnd('\n');
                if (IPAddress.TryParse(result, out IPAddress address) && address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                {
                    Settings.Default.MyIp = result;
                    myip = result;
                    return;
                }
                myip = Settings.Default.MyIp;
            }
            catch (Exception ex)
            {
                main?.MonitorEvent($"Error downloading IP address from {url}: {ex.Message}");
                myip = Settings.Default.MyIp;
            }
        }

        /// <summary>
        /// For banlist
        /// </summary>
        string[] banlist = null;
        bool banlistFallback = false;

        private async Task DownloadBanlistAsync(string url)
        {
            try
            {
                var response = await httpClient.GetStringAsync(url);
                banlist = response.Split('\n');
            }
            catch
            {
                if (!banlistFallback)
                {
                    banlistFallback = true;
                    string fallBackUrl = "https://drive.google.com/uc?export=download&id=1yhrHsv8s0_vnBhzyy7hgSv0Yw_31eJLu";
                    await DownloadBanlistAsync(fallBackUrl);
                }
                else
                {
                    banlist = [ "" ];
                }
            }
        }

#if EVAL
        /// <summary>
        /// Evaluation checker
        /// </summary>
        WebClient evalWebClient;
        int evalCode = 1;

        /// <summary>
        /// callback for banlist
        /// </summary>
        void EvalComplete(object sender, DownloadStringCompletedEventArgs e)
        {
            lock (main.conch)
            {
                // check for error
                if (e.Cancelled == false && e.Error == null && e.Result != null)
                {
                    // parse result
                    int.TryParse(e.Result, NumberStyles.Number, CultureInfo.InvariantCulture, out evalCode);
                }

                // cleanup
                evalWebClient.Dispose();
                evalWebClient = null;
            }
        }
#endif

        /// <summary>
        /// Constructor
        /// </summary>
        public Network(Main main)
        {

//#if DEBUG
//            int appCount = System.Diagnostics.Process.GetProcessesByName(System.IO.Path.GetFileNameWithoutExtension(System.Reflection.Assembly.GetEntryAssembly().Location)).Length;
//            int debugPort = 6112 + appCount;
//#endif
            // set main
            this.main = main;

#if DEBUG
            // set port
            //            port = debugPort;
#endif
            // monitor
            main.MonitorEvent("Unique address is " + Network.UuidToString(main.uuid));

            // create local node
            localNode = new LocalNode(main)
            {
                // initialize local node
                connectComplete = ConnectComplete,
                nodeJoin = NodeJoin,
                nodeEstablished = NodeEstablished,
                nodeLeave = NodeLeave,
                nodeError = main.MonitorEvent,
                nodeDebug = main.MonitorNetwork,
                receiveNotify = ReceiveMsg,
                jfp2ReceiveNotify = HandleJfp2Application
            };

            // register JFP2 codecs (docs/protocol-v2-implementation-plan.md Phase 2) - once per
            // process; CodecRegistry.Register just overwrites the same (class, version) entry if
            // called again, so this is safe even though Network's constructor could in principle run
            // more than once in a process (e.g. multiple JoinFS instances in the same CONSOLE host).
            Jfp2.Codecs.CodecRegistry.Register(new Jfp2.Codecs.StatusRequestV1Codec());
            Jfp2.Codecs.CodecRegistry.Register(new Jfp2.Codecs.StatusV1Codec());
            Jfp2.Codecs.CodecRegistry.Register(new Jfp2.Codecs.IdentityV1Codec());
            Jfp2.Codecs.CodecRegistry.Register(new Jfp2.Codecs.VariableSyncV1Codec());
            Jfp2.Codecs.CodecRegistry.Register(new Jfp2.Codecs.PositionV1Codec());
            Jfp2.Codecs.CodecRegistry.Register(new Jfp2.Codecs.EventV1Codec());
            Jfp2.Codecs.CodecRegistry.Register(new Jfp2.Codecs.FlightPlanV1Codec());
            Jfp2.Codecs.CodecRegistry.Register(new Jfp2.Codecs.NotesV1Codec());
            Jfp2.Codecs.CodecRegistry.Register(new Jfp2.Codecs.WeatherUpdateV1Codec());
            Jfp2.Codecs.CodecRegistry.Register(new Jfp2.Codecs.WeatherReplyV1Codec());

            // override local address when running in Docker or behind NAT
            if (IPAddress.TryParse(main.settingsLocalAddress, out IPAddress localAddress) && localAddress.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            {
                localNode.LocalAddress = localAddress;
            }

            // check for valid IP
            if (IPAddress.TryParse(Settings.Default.MyIp, out IPAddress address) && address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            {
                // set local node external address
                localNode.InternetAddress = address;
            }

            // initialize reset time for DNS
            dnsResetTime = DateTime.Now.AddDays(1);

            // delay these timers for one interval
            sharedDataTimer.Elapsed(main.ElapsedTime);
            localUserListTimer.Elapsed(main.ElapsedTime);
            hubUserListTimer.Elapsed(main.ElapsedTime);
            internetAddressTimer.Elapsed(main.ElapsedTime);

#if !NO_HUBS
            // load hub list
            LoadHubList();
#endif
            string hubsUrl = "https://raw.githubusercontent.com/tuduce/JoinFS/refs/heads/main/JoinFS/util/seedhubs.txt";
            string banUrl = "https://raw.githubusercontent.com/tuduce/JoinFS/refs/heads/main/JoinFS/util/banlist.txt";
            var tasks = new List<Task> {
                DownloadMyIpAsync(),
#if !NO_HUBS
                DownloadSeedhubsAsync(hubsUrl),
                DownloadBanlistAsync(banUrl),
#endif
            };
            Task.WhenAll(tasks).GetAwaiter().GetResult();

#if !CONSOLE
            main.hubsForm ?. refresher.Schedule(5);
#endif
            addressBookTimer.Set(main.ElapsedTime + 4.0);

            // get vuids
            vuidSquawk = VariableMgr.CreateVuid("transponder code:1");
            vuidIfr = VariableMgr.CreateVuid("ai traffic isifr");
        }

        /// <summary>
        /// Process local node
        /// </summary>
        void DoLocalNode()
        {
            // check that node is ready
            if (localNode.Ready)
            {
                // check for scheduled leave
                if (scheduleLeave)
                {
                    // leave
                    Leave();
                    // reset
                    scheduleLeave = false;
                }

                // check for scheduled join user
                if (scheduleJoinUser)
                {
                    // check if end point is known
                    if (onlineUsers.TryGetValue(scheduleJoinUuid, out var user))
                    {
                        // join
                        Join(MakeEndPoint(user), 0);
                        // reset
                        scheduleJoinUser = false;
                    }
                }

                // check for scheduled create
                if (scheduleCreate)
                {
                    // create
                    Create(false);
                    // reset
                    scheduleCreate = false;
                }

                // check for scheduled join
                if (scheduleJoin != null)
                {
                    // join
                    Join(scheduleJoin, schedulePasswordHash);
                    // reset
                    scheduleJoin = null;
                    schedulePasswordHash = 0;
                }

                // check for scheduled join global
                if (scheduleJoinGlobal)
                {
                    // create global session
                    Create(true);

                    // for each hub in the list
                    foreach (var hub in hubList)
                    {
                        // check if global session
                        if (hub.globalSession)
                        {
                            // join with session
                            Join(hub.endPoint, 0);
                        }
                    }

                    // reset
                    scheduleJoinGlobal = false;
                }

                // check for scheduled login
                if (scheduleLogin != null)
                {
                    // join
                    Login(scheduleLogin, scheduleLoginEmail, scheduleLoginHash, scheduleLoginVerify);
                    // reset
                    scheduleLogin = null;
                }

                // check for scheduled shared data message
                if (scheduleSharedData.Valid())
                {
                    // send message
                    SendSharedDataMessage(scheduleSharedData);
                    // reset
                    scheduleSharedData = new LocalNode.Nuid();
                }
            }

            // check for hub
            if (main.settingsHub)
            {
                // internet address timer
                if (internetAddressTimer.Elapsed(main.ElapsedTime))
                {
                    try
                    {
                        // get myip. fire and forget
                        _ = DownloadMyIpAsync();
                    }
                    catch (Exception ex)
                    {
                        // monitor event
                        main.MonitorEvent(ex.Message);
                    }
                }
            }

            // process node
            localNode.DoWork();
        }

        /// <summary>
        /// Process web clients
        /// </summary>
        void DoWebClients()
        {
            // check if myip is available
            if (myip != null)
            {
                // check for valid IP
                if (IPAddress.TryParse(myip, out IPAddress address) && address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                {
                    // set local node external address
                    localNode.InternetAddress = address;
                }
                // reset myip
                myip = null;
            }

            // check if seedhubs is available
            if (seedhubs != null)
            {
                // for each hub
                if (seedhubs.Length > 0)
                {
                    // for each seed hub
                    foreach (var seedHub in seedhubs)
                    {
                        // convert address to end point
                        if (seedHub.Length > 0 && MakeEndPoint(seedHub.TrimEnd('\r'), Network.DEFAULT_PORT, out IPEndPoint endPoint))
                        {
                            // submit hub
                            SubmitHub(endPoint);
                        }
                    }
                }
                // reset seedhubs
                seedhubs = null;
            }

            // check if banlist is available
            if (banlist != null)
            {
                // for each IP address
                if (banlist.Length > 0)
                {
                    // for each IP address
                    foreach (var ip in banlist)
                    {
                        // submit ip
                        localNode.BanIP(ip);
                    }
                }
                // reset banlist
                banlist = null;
            }
        }

        /// <summary>
        /// Process shared data
        /// </summary>
        void DoSharedData()
        {
            // shared data
            if (sharedDataTimer.Elapsed(main.ElapsedTime))
            {
                // check for user aircraft and connected
                if (localNode.Connected)
                {
                    try
                    {
                        // get nodes
                        LocalNode.Nuid[] nodeList = localNode.GetNodeList();
                        // for each node
                        foreach (var nuid in nodeList)
                        {
                            // create message
                            SendSharedDataMessage(nuid);
                        }
                    }
                    catch (Exception ex)
                    {
                        main.MonitorEvent("Failed to write SharedData message: " + ex.Message);
                    }
                }
            }
        }

        /// <summary>
        /// Process online users
        /// </summary>
        void DoOnlineUsers()
        {
            // online
            if (localNode.Ready && onlineUserTimer.Elapsed(main.ElapsedTime))
            {
                // send message
                SendOnlineMessage();

                // for each online user
                foreach (var user in onlineUsers)
                {
                    // check expire time
                    if (user.Value.expireTime < main.ElapsedTime)
                    {
                        // add to removal list
                        tempOnlineUsers.Add(user.Key);
                    }
                }

                // for each online user to remove
                foreach (var uuid in tempOnlineUsers)
                {
                    // remove online user
                    onlineUsers.Remove(uuid);
                    // monitor
                    main.MonitorNetwork("Removed Online User '" + UuidToString(uuid) + "'");
                }

                // clear list
                tempOnlineUsers.Clear();
            }
        }

#if !CONSOLE
        /// <summary>
        /// Fast updates for the address book
        /// </summary>
        int addressBookFastCount = 4;
#endif

        /// <summary>
        /// Process address book
        /// </summary>
        void DoAddressBook()
        {
#if !CONSOLE
            // online
            if (localNode.Ready && main.addressBookForm != null && main.addressBookForm.Visible && addressBookTimer.Elapsed(main.ElapsedTime))
            {
                // check fast count
                if (addressBookFastCount > 0)
                {
                    // elapse sooner
                    addressBookTimer.Set(main.ElapsedTime + 3.0);
                    // update fast count
                    addressBookFastCount--;
                }

                // for each entry
                foreach (var entry in main.addressBook.entries)
                {
                    // check for valid endpoint
                    if (entry.endPoint.Port != 0)
                    {
                        // send status request to node (via JFP2 if negotiated, else legacy)
                        SendStatusRequest(entry.endPoint, false);
                    }
                    // check if user is unknown
                    else if (onlineUsers.TryGetValue(entry.uuid, out var user))
                    {
                        // update end point
                        entry.endPoint = MakeEndPoint(user);
                        // send status request to node (via JFP2 if negotiated, else legacy)
                        SendStatusRequest(entry.endPoint, false);
                    }
                    // check if user is unknown
                    else if (entry.uuid != 0)
                    {
                        // request nuid
                        RequestNuid(entry.uuid);
                    }

                    // check for entry going offline
                    if (main.ElapsedTime > entry.offlineTime)
                    {
                        // no longer online
                        entry.online = false;
                    }
                }
            }
#endif
        }

        /// <summary>
        /// Count of status requests
        /// </summary>
        int hubStatusRequestCount = 0;

        /// <summary>
        /// Process hubs
        /// </summary>
        void DoHubs()
        {
            // online
            if (localNode.Ready)
            {
                // hubs timer
                if (hubsTimer.Elapsed(main.ElapsedTime))
                {
                    // check for scheduled submit hub
                    if (submitHub != null)
                    {
                        // submit hub
                        SubmitHub(submitHub);
                        // reset
                        submitHub = null;
                    }

                    // Deliberately NOT migrated to SendStatusRequest/JFP2 (docs/protocol-v2-
                    // implementation-plan.md Phase 2): this prepares ONE legacy sendBuffer and fans
                    // it out, unmodified, to potentially many hub endpoints below AND (via the
                    // pendingHubsTimer block further down, on a different timer with no re-prepare of
                    // its own) to pendingHubList entries too - splitting some of those sends to JFP2
                    // while others keep relying on this one shared buffer would risk sending stale or
                    // wrong bytes to whichever endpoint if the eligibility split ever disagreed with
                    // the buffer's actual contents. Hub/address-book endpoints are also essentially
                    // never legacy-mesh peers with a JFP2 PeerSession in the first place (Status here
                    // is directory/discovery traffic, orthogonal to session membership), so there is
                    // little to gain by touching this call site.
                    WriteStatusRequestMessage((hubStatusRequestCount & 7) == 0);
                    // update count
                    hubStatusRequestCount++;

                    // check for first few iterations since launch
                    if (hubStatusRequestCount <= 3)
                    {
                        // for each hub in the list
                        foreach (var hub in hubList)
                        {
                            // check for valid nuid and currently offline
                            if (hub.nuid.Valid() && hub.online == false)
                            {
                                // send request
                                localNode.Send(hub.endPoint);
                            }
                        }
                    }
                    // check for any hubs
                    else if (hubList.Count > 0)
                    {
                        // cycle through the hub list
                        int index = hubStatusRequestCount % hubList.Count;
                        // check for valid nuid
                        if (hubList[index].nuid.Valid())
                        {
                            // send request
                            localNode.Send(hubList[index].endPoint);
                        }
                    }

                    // for each hub in the list
                    foreach (var hub in hubList)
                    {
                        // check for removal duration after running for a while
                        if (main.ElapsedTime > 120.0 && (DateTime.Now - hub.dateTime).Days > DEAD_HUB_DURATION)
                        {
                            // add to remove list
                            tempHubList.Add(hub);
                        }

                        // check for entry going offline
                        if (main.ElapsedTime > hub.offlineTime)
                        {
                            // no longer online
                            hub.online = false;
                        }
                    }

                    // for each hub to remove
                    foreach (var hub in tempHubList)
                    {
                        main.MonitorEvent("Removed hub '" + hub.name + "' - '" + UuidToString(MakeUuid(hub.guid)) + "'");
                        // remove hub
                        hubList.Remove(hub);
                        // hub list changed
                        hubListChanged = true;
                    }

                    // clear remote list
                    tempHubList.Clear();

                    // check if hub list has changed and this is the primary instance
                    if (hubListChanged)
                    {
                        // save hub list
                        SaveHubList();
                        // reset flag
                        hubListChanged = false;
                    }
                }

                // pending hubs timer
                if (pendingHubsTimer.Elapsed(main.ElapsedTime))
                {
                    // for each pending hub
                    foreach (var pending in pendingHubList)
                    {
                        // send request
                        localNode.Send(pending.Key);
                        // check if expired
                        if (pending.Value < main.ElapsedTime)
                        {
                            // remove pending hub
                            tempEndPoints.Add(pending.Key);
                        }
                    }

                    // for each pending hub to remove
                    foreach (var endPoint in tempEndPoints)
                    {
                        // remove hub
                        pendingHubList.Remove(endPoint);
                    }

                    // clear temp end points
                    tempEndPoints.Clear();
                }
            }
        }

        /// <summary>
        /// Process DNS lookups
        /// </summary>
        void DoDNS()
        {
            // check for DNS reset
            if (DateTime.Now > dnsResetTime)
            {
                // clear lookups
                dnsLookups.Clear();
                // initialize reset time for DNS
                dnsResetTime = DateTime.Now.AddDays(1);
            }
        }

        /// <summary>
        /// Process local user list
        /// </summary>
        void DoLocalUserList()
        {
            // local user list
            if (localUserListTimer.Elapsed(main.ElapsedTime))
            {
                // check if this is a hub
                if (main.settingsHub)
                {
                    // clear current users
                    localUserList.Clear();

                    // get user aircraft
                    Sim.Aircraft aircraft = main.sim ?. userAircraft;

                    // check for local ATC
                    if (main.settingsAtc && main.settingsAtcAirport.Length > 0)
                    {
                        // check if airport is listed
                        if (main.airportList.TryGetValue(main.settingsAtcAirport, out var airport))
                        {
                            // convert to radians
                            double latitude = Math.Min(90.0, Math.Max(-90.0, airport.latitude));
                            double longitude = Math.Min(180.0, Math.Max(-180.0, airport.longitude));
                            // get ATC level
                            int level = Settings.Default.AtcLevel;
                            Sim.FlightPlan flightPlan = new()
                            {
                                callsign = Sim.MakeAtcCallsign(main.settingsAtcAirport, level),
                                departure = main.settingsAtcAirport
                            };
                            // write client entry for ATC
                            localUserList.Add(new HubUser(main.guid, true, main.settingsNickname, Settings.Default.AtcFrequency, latitude, longitude, 0.0, null, flightPlan, 0, level, main.settingsActivityCircle, true, 0));
                        }
                    }
                    else if (aircraft != null && aircraft.Position != null)
                    {
                        int squawk = aircraft.variableSet != null ? aircraft.variableSet.GetInteger(vuidSquawk) : 0;
                        bool ifr = aircraft.variableSet != null && aircraft.variableSet.GetInteger(vuidIfr) != 0;
                        // write client entry for pilot
                        localUserList.Add(new HubUser(main.guid, false, main.settingsNickname, 0, aircraft.Position.geo.z * (180.0 / Math.PI), aircraft.Position.geo.x * (180.0 / Math.PI), aircraft.Position.geo.y, aircraft.netVelocity, aircraft.flightPlan, squawk, 0, 0, ifr, aircraft.Position.angles.y));
                    }

                    // for each node
                    foreach (var node in nodeList)
                    {
                        // get aircraft
                        aircraft = main.sim ?. objectList.Find(o => o.ownerNuid == node.Key && o is Sim.Aircraft && (o as Sim.Aircraft).user) as Sim.Aircraft;

                        // check for ATC
                        if (node.Value.atc && node.Value.atcAirport.Length > 0)
                        {
                            // check if airport is listed
                            if (main.airportList.TryGetValue(node.Value.atcAirport, out Main.Airport value))
                            {
                                // convert to radians
                                double latitude = Math.Min(90.0, Math.Max(-90.0, value.latitude));
                                double longitude = Math.Min(180.0, Math.Max(-180.0, value.longitude));
                                Sim.FlightPlan flightPlan = new()
                                {
                                    callsign = Sim.MakeAtcCallsign(node.Value.atcAirport, node.Value.atcLevel),
                                    departure = node.Value.atcAirport
                                };
                                // write client entry for ATC
                                localUserList.Add(new HubUser(node.Value.guid, true, node.Value.nickname, node.Value.atcFrequency, latitude, longitude, 0.0, null, flightPlan, 0, node.Value.atcLevel, node.Value.activityCircle, true, 0));
                            }
                        }
                        else if (aircraft != null && aircraft.Position != null)
                        {
                            int squawk = aircraft.variableSet != null ? aircraft.variableSet.GetInteger(vuidSquawk) : 0;
                            bool ifr = aircraft.variableSet != null && aircraft.variableSet.GetInteger(vuidIfr) != 0;
                            // write client entry for pilot
                            localUserList.Add(new HubUser(node.Value.guid, false, node.Value.nickname, 0, aircraft.Position.geo.z * (180.0 / Math.PI), aircraft.Position.geo.x * (180.0 / Math.PI), aircraft.Position.geo.y, aircraft.netVelocity, aircraft.flightPlan, squawk, 0, 0, ifr, aircraft.Position.angles.y));
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Current hub to request user list
        /// </summary>
        int userListRequestCount = 0;

        /// <summary>
        /// Process hub user list
        /// </summary>
        void DoHubUserList()
        {
            // hub user list
            if (localNode.Ready && hubUserListTimer.Elapsed(main.ElapsedTime))
            {
                // for each hub
                foreach (var hub in hubList)
                {
                    // remove expired users
                    hub.userList.RemoveAll(u => main.ElapsedTime > u.expireTime);
                }

                // check if whazzup global is enabled or aircraft list is enabled
                if (main.settingsWhazzup && main.settingsWhazzupPublic
#if !SERVER && !CONSOLE
                    || main.aircraftForm != null && main.aircraftForm.Visible && Settings.Default.IncludeGlobalAircraft
                    || main.atcForm != null && main.atcForm.Visible
#endif
                    )
                {
                    // check for first few iterations since launch
                    if (userListRequestCount <= 3)
                    {
                        // for all hubs
                        foreach (var hub in hubList)
                        {
                            // check if hub is online and no users yet
                            if (hub.online && hub.userList.Count == 0)
                            {
                                // send user list request
                                SendUserListRequestMessage(hub.endPoint);
                            }
                        }
                    }
                    else if (hubList.Count > 0)
                    {
                        // cycle through the hub list
                        int index = userListRequestCount % hubList.Count;
                        // check if hub is online
                        if (hubList[index].online)
                        {
                            // send user list request
                            SendUserListRequestMessage(hubList[index].endPoint);
                        }

                        // check if updating whazzup
                        if (main.settingsWhazzup && main.settingsWhazzupPublic)
                        {
                            // for each hub
                            foreach (var hub in hubList)
                            {
                                // check if hub is online
                                if (hub.online)
                                {
                                    // send user positions request
                                    SendUserPositionsRequestMessage(hub.endPoint);
                                }
                            }
                        }
                    }

                    // update count
                    userListRequestCount++;
                }
            }
        }

        /// <summary>
        /// Process all network components
        /// </summary>
        public void DoWork()
        {
            DoLocalNode();
            DoWebClients();
            DoSharedData();
            DoAddressBook();
            DoDNS();
            DoJfp2IdentityCleanup();
#if !NO_HUBS
            DoOnlineUsers();
            DoHubs();
            DoLocalUserList();
            DoHubUserList();
#endif
        }

        /// <summary>
        /// Convert address from text to an end point
        /// </summary>
        /// <param name="addressText">Address string</param>
        /// <param name="endPoint">End point</param>
        /// <returns>Success</returns>
        public bool MakeEndPoint(string addressText, ushort port, out IPEndPoint endPoint)
        {
            // result
            endPoint = new IPEndPoint(0, 0);

            // parse ip address
            string[] parts = addressText.Split(':');

            // check result
            if (parts.Length <= 0)
            {
                // failed
                return false;
            }

            // get remote port
            int remotePort = port;
            if (parts.Length > 1 && Int32.TryParse(parts[1], NumberStyles.Number, CultureInfo.InvariantCulture, out remotePort) == false)
            {
                // failed
                return false;
            }

            if (IPAddress.TryParse(parts[0], out IPAddress address) && address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            {
                // set end point
                endPoint = new IPEndPoint(address, remotePort);
            }
            // try DNS lookup
            else if (DnsLookup(parts[0], out address) && address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            {
                // set end point
                endPoint = new IPEndPoint(address, remotePort);
            }
            else
            {
                // failed
                return false;
            }

            // success
            return true;
        }

        /// <summary>
        /// Swizzle bits
        /// </summary>
        /// <param name="bits"></param>
        /// <returns></returns>
        static uint EncodeBits(uint bits)
        {
            uint result = 0;

            for (int i = 15; i >= 0; i--)
            {
                result |= ((bits & (1 << (i * 2 + 1))) != 0) ? (uint)(1 << (i + 16)) : 0;
                result |= ((bits & (1 << (i * 2))) != 0) ? (uint)(1 << i) : 0;
            }

            return result;
        }

        /// <summary>
        /// Unswizzle bits
        /// </summary>
        /// <param name="bits"></param>
        /// <returns></returns>
        static uint DecodeBits(uint bits)
        {
            uint result = 0;

            for (int i = 15; i >= 0; i--)
            {
                result |= ((bits & (1 << (i + 16))) != 0) ? (uint)(1 << (i * 2 + 1)) : 0;
                result |= ((bits & (1 << i)) != 0) ? (uint)(1 << (i * 2)) : 0;
            }

            return result;
        }

        /// <summary>
        /// Encode an IP address
        /// </summary>
        /// <param name="address"></param>
        /// <returns></returns>
        public static string EncodeIP(string address)
        {
            string result = "";

            // separate port
            string[] colonParts = address.Split(':');

            // check for valid address
            if (colonParts.Length > 0)
            {
                // split IP address into parts
                string[] parts = colonParts[0].Split('.');

                // check format
                if (parts.Length == 4)
                {
                    // convert each part
                    if (uint.TryParse(parts[0], NumberStyles.Number, CultureInfo.InvariantCulture, out uint n1)
                        && uint.TryParse(parts[1], NumberStyles.Number, CultureInfo.InvariantCulture, out uint n2)
                        && uint.TryParse(parts[2], NumberStyles.Number, CultureInfo.InvariantCulture, out uint n3)
                        && uint.TryParse(parts[3], NumberStyles.Number, CultureInfo.InvariantCulture, out uint n4))
                    {
                        // validate parts
                        if (n1 <= 0xff && n2 <= 0xff && n3 <= 0xff && n4 <= 0xff)
                        {
                            // encode
                            uint code = EncodeBits(((n1 & 0xff) << 24) + ((n2 & 0xff) << 16) + ((n3 & 0xff) << 8) + (n4 & 0xff));
                            result += (code >> 16).ToString("D5", CultureInfo.InvariantCulture) + "-" + (code & 0xffff).ToString("D5", CultureInfo.InvariantCulture);
                            // check for port
                            if (colonParts.Length > 1)
                            {
                                // add port
                                result += "-" + colonParts[1];
                            }
                            // success
                            return result;
                        }
                    }
                }
            }
            // failed
            return address;
        }

        /// <summary>
        /// Decode an IP address
        /// </summary>
        /// <param name="address"></param>
        /// <returns></returns>
        public static string DecodeIP(string address)
        {
            string result = "";

            // separate port
            string[] colonParts = address.Split(':');

            // check for valid address
            if (colonParts.Length > 0)
            {
                // split coded address into parts
                string[] parts = colonParts[0].Split('-');

                // check format
                if (parts.Length == 2)
                {
                    // convert each part
                    if (uint.TryParse(parts[0], NumberStyles.Number, CultureInfo.InvariantCulture, out uint n1)
                        && uint.TryParse(parts[1], NumberStyles.Number, CultureInfo.InvariantCulture, out uint n2))
                    {
                        // validate parts
                        if (n1 <= 0xffff && n2 <= 0xffff)
                        {
                            // decode
                            uint ip = DecodeBits((n1 << 16) + (n2 & 0xffff));
                            result += (ip >> 24).ToString(CultureInfo.InvariantCulture) + "." + ((ip >> 16) & 0xff).ToString(CultureInfo.InvariantCulture) + "." + ((ip >> 8) & 0xff).ToString(CultureInfo.InvariantCulture) + "." + (ip & 0xff).ToString(CultureInfo.InvariantCulture);
                            // check for port
                            if (colonParts.Length > 1)
                            {
                                // add port
                                result += ":" + colonParts[1];
                            }
                            // success
                            return result;
                        }
                    }
                }
            }

            // separate using spaces
            string[] spaceParts = address.Split('-');

            // check for vaid address
            if (spaceParts.Length == 2 || spaceParts.Length == 3)
            {
                // convert each part
                if (uint.TryParse(spaceParts[0], NumberStyles.Number, CultureInfo.InvariantCulture, out uint n1)
                    && uint.TryParse(spaceParts[1], NumberStyles.Number, CultureInfo.InvariantCulture, out uint n2))
                {
                    // validate parts
                    if (n1 <= 0xffff && n2 <= 0xffff)
                    {
                        // decode
                        uint ip = DecodeBits((n1 << 16) + (n2 & 0xffff));
                        result += (ip >> 24).ToString(CultureInfo.InvariantCulture) + "." + ((ip >> 16) & 0xff).ToString(CultureInfo.InvariantCulture) + "." + ((ip >> 8) & 0xff).ToString(CultureInfo.InvariantCulture) + "." + (ip & 0xff).ToString(CultureInfo.InvariantCulture);
                        // check for port
                        if (spaceParts.Length == 3)
                        {
                            // add port
                            result += ":" + spaceParts[2];
                        }
                        // success
                        return result;
                    }
                }
            }
            // failed
            return address;
        }

        /// <summary>
        /// Scheduled submit hub
        /// </summary>
        IPEndPoint submitHub;

        /// <summary>
        /// Schedule a submit hub
        /// </summary>
        /// <param name="addressText"></param>
        public void ScheduleSubmitHub(IPEndPoint endPoint)
        {
#if !NO_HUBS
            // check if not scheduled
            // set schedule
            submitHub ??= endPoint;
#endif
        }

        /// <summary>
        /// Submit a new hub to the list
        /// </summary>
        /// <param name="addressText">Address</param>
        public void SubmitHub(IPEndPoint endPoint)
        {
            // count IP
            int count = 0;
            // for each pending hub
            foreach (var pending in pendingHubList)
            {
                // check for same internet address
                if (pending.Key.Address.Equals(endPoint.Address))
                {
                    // increment count
                    count++;
                }
            }

            // check pending list
            if (pendingHubList.ContainsKey(endPoint) == false && count < MAX_IP_HUBS)
            {
                // add hub to pending list
                pendingHubList.Add(endPoint, (float)main.ElapsedTime + PENDING_HUB_EXPIRE_TIME);
                // request status (via JFP2 if negotiated, else legacy)
                SendStatusRequest(endPoint, true);
            }
        }

        /// <summary>
        /// Save hubs to file
        /// </summary>
        void SaveHubList()
        {
            try
            {
                // open file
                BinaryWriter writer = new(File.Create(main.storagePath + Path.DirectorySeparatorChar + HUB_LIST_FILE));
                if (writer != null)
                {
                    // for each hub
                    foreach (var hub in hubList)
                    {
                        // check that the hub is not ignored
                        if (main.log.IgnoreNode(hub.endPoint.Address) == false && main.log.IgnoreNode(ref hub.guid) == false)
                        {
                            // add to save list
                            tempHubList.Add(hub);
                        }
                    }

                    // write version
                    writer.Write(HUB_LIST_VERSION);
                    // write hub count
                    writer.Write((ushort)tempHubList.Count);
                    // for each hub in the save list
                    foreach (var hub in tempHubList)
                    {
                        // write address
                        writer.Write(hub.addressText);
                        // write nuid
                        hub.nuid.Write(writer);
                        // write port
                        writer.Write(hub.port);
                        // write time
                        writer.Write(hub.dateTime.ToBinary());
                        // write guid
                        writer.Write(hub.guid.ToByteArray());
                        // write version
                        writer.Write(hub.appVersion);
                        // write name
                        writer.Write(hub.name);
                        // write about
                        writer.Write(hub.about);
                        // write voip
                        writer.Write(hub.voip);
                        // write event
                        writer.Write(hub.nextEvent);
                        // write airport
                        writer.Write(hub.airport);
                        // write activity circle
                        writer.Write(hub.activityCircle);
                        // write global flag
                        writer.Write(hub.globalSession);
                        // write password
                        writer.Write(hub.password);
                    }
                    // finished
                    writer.Close();

                    main.MonitorEvent("Saved " + tempHubList.Count + " hub(s)");

                    // clear temp hub list
                    tempHubList.Clear();
                }
            }
            catch (Exception ex)
            {
                main.MonitorEvent(ex.Message);
            }
        }

        /// <summary>
        /// Load hub list from file
        /// </summary>
        void LoadHubList()
        {
            BinaryReader reader = null;

            // try ten times
            for (int attempt = 1; attempt <= 10; attempt++)
            {
                try
                {
                    // check for matching file
                    if (File.Exists(main.storagePath + Path.DirectorySeparatorChar + HUB_LIST_FILE))
                    {
                        // open file
                        reader = new BinaryReader(File.Open(main.storagePath + Path.DirectorySeparatorChar + HUB_LIST_FILE, FileMode.Open));

                        // clear list
                        hubList.Clear();

                        // get hub list version
                        ushort version = reader.ReadUInt16();
                        // check for invalid or unknown later version
                        if (version < 10006 || version > HUB_LIST_VERSION)
                        {
                            // create new hub list
                            main.MonitorEvent("Invalid hub list version. Recreating the hub list.");
                        }
                        else
                        {
                            // read number of hubs
                            ushort count = reader.ReadUInt16();
                            // for each hub
                            for (int i = 0; i < count; i++)
                            {
                                // create new hub
                                Hub hub = new() {
                                    // read address
                                    addressText = reader.ReadString()
                                };
                                
                                if (version >= 10005)
                                {
                                    // read nuid
                                    hub.nuid = new LocalNode.Nuid(reader);
                                    // read port
                                    hub.port = reader.ReadUInt16();
                                }
                                else
                                {
                                    // read ip
                                    hub.nuid.ip = version >= 10003 ? reader.ReadUInt32() : 0;
                                    // read port
                                    hub.port = reader.ReadUInt16();
                                    // read local
                                    hub.nuid.local = version >= 10002 ? reader.ReadByte() : (byte)0;
                                }
                                // read time
                                hub.dateTime = DateTime.FromBinary(reader.ReadInt64());
                                // read guid
                                hub.guid = new Guid(reader.ReadBytes(16));
                                // read version
                                hub.appVersion = reader.ReadString();
                                // read name
                                hub.name = reader.ReadString();
                                // read about
                                hub.about = reader.ReadString();
                                // read voip
                                hub.voip = reader.ReadString();
                                // read event
                                hub.nextEvent = reader.ReadString();
                                // read airport
                                hub.airport = reader.ReadString();
                                // read activity circle
                                hub.activityCircle = reader.ReadInt32();
                                // read global flag
                                hub.globalSession = reader.ReadBoolean();
                                // read password
                                hub.password = version >= 10004 && reader.ReadBoolean();

                                // check for old version
                                if (version < 10003)
                                {
                                    // set end point
                                    MakeEndPoint(hub.addressText, hub.port, out hub.endPoint);
                                    // set nuid
                                    hub.nuid = new LocalNode.Nuid(hub.endPoint, hub.nuid.local);
                                }
                                else
                                {
                                    // set end point
                                    hub.endPoint = localNode.MakeEndPoint(hub.nuid, hub.port);
                                }

                                // check for valid hub
                                if (hub.nuid.Valid() && HubCount_IP(hub.nuid) < MAX_IP_HUBS)
                                {
                                    // add hub
                                    hubList.Add(hub);
                                }
                            }

                            // monitor
                            if (attempt == 1)
                            {
                                main.MonitorEvent("Loaded " + count + " hub(s)");
                            }
                            else
                            {
                                main.MonitorEvent("Loaded " + count + " hub(s) on attempt " + attempt);
                            }
                        }

                        // close file
                        reader.Close();
                        // finished
                        return;
                    }
                }
                catch (Exception ex)
                {
                    main.MonitorEvent(ex.Message);
                }
                finally
                {
                    // check for file
                    // close file
                    reader?.Close();
                }

                // sleep for random time
                Random random = new((int)DateTime.Now.Ticks);
                Thread.Sleep(random.Next(10, 100));
            }
        }

        /// <summary>
        /// Current end point that was joined
        /// </summary>
        public IPEndPoint joinEndPoint = new(0, 0);

        /// <summary>
        /// A scheduled join
        /// </summary>
        volatile IPEndPoint scheduleJoin = null;
        volatile uint schedulePasswordHash = 0;

        /// <summary>
        /// Schedule a join
        /// </summary>
        /// <param name="endPoint"></param>
        public void ScheduleJoin(IPEndPoint endPoint, uint passwordHash)
        {
            // check if not scheduled
            if (scheduleJoin == null)
            {
                // schedule
                scheduleJoin = endPoint;
                schedulePasswordHash = passwordHash;
            }
        }

        /// <summary>
        /// A scheduled join
        /// </summary>
        volatile bool scheduleJoinGlobal = false;

        /// <summary>
        /// Schedule a join to the global session
        /// </summary>
        /// <param name=""></param>
        public void ScheduleJoinGlobal()
        {
#if !NO_GLOBAL
            // schedule
            scheduleJoinGlobal = true;
#endif
        }

        /// <summary>
        /// A scheduled login
        /// </summary>
        volatile IPEndPoint scheduleLogin = null;
        volatile string scheduleLoginEmail = "";
        volatile uint scheduleLoginHash = 0;
        volatile bool scheduleLoginVerify = false;

        public string ScheduleLoginEmail { get { return scheduleLoginEmail; } }
        public uint ScheduleLoginHash { get { return scheduleLoginHash; } }

        /// <summary>
        /// Schedule a login
        /// </summary>
        /// <param name="endPoint"></param>
        public void ScheduleLogin(IPEndPoint endPoint, string email, uint hash, bool verify)
        {
            // check if not scheduled
            if (scheduleLogin == null)
            {
                // schedule
                scheduleLogin = endPoint;
                scheduleLoginEmail = email;
                scheduleLoginHash = hash;
                scheduleLoginVerify = verify;
            }
        }

        /// <summary>
        /// Join a session
        /// </summary>
        /// <param name="endPoint"></param>
        void Join(IPEndPoint endPoint, uint passwordHash)
        {
            // save end point
            joinEndPoint = endPoint;
            // low bandwidth
            localNode.lowBandwidth = Settings.Default.LowBandwidth;

            // initialize comms request count
            commsRequests = 3;

            try
            {
#if DEBUG
                // show event
                main.MonitorEvent("Joining '" + EncodeIP(endPoint.ToString()) + "'");
#endif

                // join network
                localNode.Join(endPoint, passwordHash);
#if !SERVER && !CONSOLE
                // refresh
                main.aircraftForm ?. refresher.Schedule(5);
                main.objectsForm ?. refresher.Schedule(5);
#endif

#if !CONSOLE
                main.sessionForm ?. usersRefresher.Schedule(5);
#endif
            }
            catch (Exception ex)
            {
                main.MonitorEvent(ex.Message);
            }
        }

        /// <summary>
        /// Login to a session
        /// </summary>
        /// <param name="endPoint"></param>
        void Login(IPEndPoint endPoint, string email, uint hash, bool verify)
        {
            // save end point
            joinEndPoint = endPoint;
            // low bandwidth
            localNode.lowBandwidth = Settings.Default.LowBandwidth;

            // initialize comms request count
            commsRequests = 3;

            try
            {
#if DEBUG
                // show event
                main.MonitorEvent("Joining '" + EncodeIP(endPoint.ToString()) + "'");
#endif

                // login to a session
                localNode.Login(endPoint, email, hash, verify);
                // refresh
#if !SERVER && !CONSOLE
                main.aircraftForm ?. refresher.Schedule(5);
                main.objectsForm ?. refresher.Schedule(5);
#endif

#if !CONSOLE
                main.sessionForm ?. usersRefresher.Schedule(5);
#endif
            }
            catch (Exception ex)
            {
                main.MonitorEvent(ex.Message);
            }
        }

        /// <summary>
        /// A scheduled leave
        /// </summary>
        volatile bool scheduleLeave = false;

        /// <summary>
        /// Schedule a leave
        /// </summary>
        /// <param name="endPoint"></param>
        public void ScheduleLeave()
        {
            // schedule
            scheduleLeave = true;
        }

        /// <summary>
        /// leave session
        /// </summary>
        /// <param name="endPoint"></param>
        public void Leave()
        {
            // check if currently connected
            if (localNode.CurrentState != LocalNode.State.Unconnected)
            {
                try
                {
                    // leave network
                    localNode.Leave();
                }
                catch (Exception ex)
                {
                    main.ShowMessage(ex.Message);
                }
                // show event
                main.MonitorEvent("Left the session");
                // refresh
#if !SERVER && !CONSOLE
                main.aircraftForm ?. refresher.Schedule();
                main.objectsForm ?. refresher.Schedule();
#endif

#if !CONSOLE
                main.sessionForm ?. usersRefresher.Schedule();
#endif
            }
        }

        /// <summary>
        /// A scheduled create
        /// </summary>
        volatile bool scheduleCreate = false;

        /// <summary>
        /// Schedule a create
        /// </summary>
        /// <param name="endPoint"></param>
        public void ScheduleCreate()
        {
#if !NO_CREATE
            // schedule
            scheduleCreate = true;
#endif
        }

        /// <summary>
        /// create session
        /// </summary>
        /// <param name="endPoint"></param>
        public void Create(bool globalSession)
        {
            try
            {
                // low bandwidth
                localNode.lowBandwidth = Settings.Default.LowBandwidth;

                // login required
                bool loginRequired = false;

                // create network
                localNode.Create(globalSession, LocalNode.HashPassword(main.settingsPassword.TrimStart(' ').TrimEnd(' ')), loginRequired);

                // check if global session
                if (globalSession)
                {
                    // show event
                    main.MonitorEvent("Joined global session");
                }
                else
                {
                    // show event
                    main.MonitorEvent("Created session");
                }
#if !SERVER && !CONSOLE
                // refresh
                main.aircraftForm ?. refresher.Schedule(5);
                main.objectsForm ?. refresher.Schedule(5);
#endif

#if !CONSOLE
                main.sessionForm ?. usersRefresher.Schedule(5);
#endif
            }
            catch (Exception ex)
            {
                main.ShowMessage(ex.Message);
            }
        }

        /// <summary>
        /// Write message for position velocity state
        /// </summary>
        /// <param name="simObject">Object</param>
        /// <param name="simPositionVelocity">Position and velocity state</param>
        public void WriteObjectPositionVelocityMessage(Sim.Obj simObject, ref Sim.ObjectPositionVelocity positionVelocity)
        {
            // prepare message
            BinaryWriter message = localNode.PrepareMessage(new LocalNode.Nuid(), false);

            // add header
            message.Write(Sim.VERSION);
            // add message ID
            message.Write((short)MESSAGE_ID.ObjectPosition);
            // add aircraft ID
            message.Write(simObject.netId);
            message.Write(simObject.ModelTitle);
            message.Write((byte)simObject.typerole);
            // flags
            byte flags = 0;
            // add pause flag
            flags |= (byte)(simObject.paused ? 0x1 : 0x0);
            message.Write(flags);
            // add current time
            message.Write(simObject.simTime);
            // add position and velocity
            Sim.Write(message, ref positionVelocity);
            // livery/ICAO type/airline - unconditional; livery is only ever populated on FS2024 (the
            // only sim that reports a real livery name via SimConnect), but other builds still relay
            // whatever a peer sends them, same reasoning as ICAO data mattering for FS2020 too
            message.Write(simObject.ownerLivery);
            message.Write(simObject is Sim.Aircraft aircraftObj ? aircraftObj.flightPlan.icaoType : "");
            message.Write(simObject is Sim.Aircraft aircraftObj2 ? aircraftObj2.flightPlan.icaoAirline : "");
            // class code/WTC - appended at the end so older peers (which stop reading after icaoAirline)
            // simply ignore these trailing bytes. Sourced from the owner's own config-confirmed/live-
            // derived data (Sim.Obj.ownerClassCode etc.), not re-derived here, so every receiving peer
            // gets the sender's best-available classification instead of independently re-deriving it
            // from ownerIcaoType, which fails for add-ons reporting a bogus/non-standard type string.
            message.Write(simObject.ownerClassCode);
            message.Write(simObject.ownerWtc);
            message.Write(simObject.ownerClassCodeConfirmed);
        }


        /// <summary>
        /// Write message for position velocity state
        /// </summary>
        public void WriteAircraftPositionMessage(uint netId, double netTime, Sim.Aircraft aircraft, ref Sim.AircraftPosition aircraftPosition)
        {
            // prepare message
            BinaryWriter message = localNode.PrepareMessage(new LocalNode.Nuid(), false);

            // add header
            message.Write(Sim.VERSION);
            // add message ID
            message.Write((short)MESSAGE_ID.AircraftPosition);
            // add aircraft ID
            message.Write(netId);
            message.Write(aircraft.user);
            message.Write(aircraft is Sim.Plane);
            message.Write(aircraft.flightPlan.callsign);
            message.Write(aircraft.ModelTitle);
            message.Write((byte)aircraft.typerole);
            // flags
            byte flags = 0;
            // add pause flag
            flags |= (byte)(aircraft.paused ? 0x1 : 0x0);
            message.Write(flags);
            // add current time
            message.Write(netTime);
            // add position and velocity
            Sim.Write(message, ref aircraftPosition);
            // livery/ICAO type/airline - unconditional; livery is only ever populated on FS2024 (the
            // only sim that reports a real livery name via SimConnect), but other builds still relay
            // whatever a peer sends them, same reasoning as ICAO data mattering for FS2020 too
            message.Write(aircraft.ownerLivery);
            message.Write(aircraft.flightPlan.icaoType);
            message.Write(aircraft.flightPlan.icaoAirline);
            // registration/flight number - appended at the end so older peers (which stop reading after
            // icaoAirline) simply ignore these trailing bytes instead of misreading the message
            message.Write(aircraft.flightPlan.registration);
            message.Write(aircraft.flightPlan.flightNumber);
            // class code/WTC - see WriteObjectPositionVelocityMessage for why these are sourced from the
            // owner's own confirmed data rather than re-derived by each receiving peer
            message.Write(aircraft.ownerClassCode);
            message.Write(aircraft.ownerWtc);
            message.Write(aircraft.ownerClassCodeConfirmed);
            message.Write(aircraftPosition.staticCgToGround);
            // The legacy network protocol is frozen.
            // Use the JFP2 protocol if you need to extend the messages.
        }

        /// <summary>
        /// Write message for integer variables
        /// </summary>
        public void SendIntegerVariablesMessage(LocalNode.Nuid nuid, uint netId, Dictionary<uint, int> variables, LocalNode.Nuid ownerNuid)
        {
            try
            {
                // prepare message
                BinaryWriter message = localNode.PrepareMessage(new LocalNode.Nuid(), false);
                // add header
                message.Write(Sim.VERSION);
                // add message ID
                message.Write((short)MESSAGE_ID.IntegerVariables);
                // add owner nuid
                ownerNuid.Write(message);
                // add aircraft ID
                message.Write(netId);
                // save buffer position
                long countPosition = message.BaseStream.Position;
                // variable count
                ushort count = 0;
                // write placeholder count
                message.Write(count);

                // for each variable in the set
                foreach (var variable in variables)
                {
                    // add variable ID
                    message.Write(variable.Key);
                    // add value
                    message.Write(variable.Value);
                    // update count
                    count++;
                    // check if message is full
                    if (count >= MAX_INTEGER_VARIABLES)
                    {
                        // no change times
                        message.Write((ushort)0);
                        // modify placeholder
                        message.BaseStream.Position = countPosition;
                        message.Write(count);
                        // check for valid nuid
                        if (nuid.Valid())
                        {
                            // send message
                            localNode.Send(nuid);
                        }
                        else
                        {
                            // broadcast message
                            localNode.Broadcast();
                        }
                        // prepare next message
                        message = localNode.PrepareMessage(new LocalNode.Nuid(), false);
                        // add header
                        message.Write(Sim.VERSION);
                        // add message ID
                        message.Write((short)MESSAGE_ID.IntegerVariables);
                        // add owner nuid
                        ownerNuid.Write(message);
                        // add aircraft ID
                        message.Write(netId);
                        // save buffer position
                        countPosition = message.BaseStream.Position;
                        // reset count
                        count = 0;
                        // write placeholder count
                        message.Write(count);
                    }
                }

                // check if message is has data
                if (count > 0)
                {
                    // no change times
                    message.Write((ushort)0);
                    // modify placeholder
                    message.BaseStream.Position = countPosition;
                    message.Write(count);
                    // check for valid nuid
                    if (nuid.Valid())
                    {
                        // send message
                        localNode.Send(nuid);
                    }
                    else
                    {
                        // broadcast message
                        localNode.Broadcast();
                    }
                }
            }
            catch (Exception ex)
            {
                main.MonitorEvent("ERROR - Failed to send IntegerVariables message - " + ex.Message);
            }
        }

        /// <summary>
        /// Write message for float variables
        /// </summary>
        public void SendFloatVariablesMessage(LocalNode.Nuid nuid, uint netId, Dictionary<uint, float> variables, LocalNode.Nuid ownerNuid)
        {
            try
            {
                // prepare message
                BinaryWriter message = localNode.PrepareMessage(new LocalNode.Nuid(), false);
                // add header
                message.Write(Sim.VERSION);
                // add message ID
                message.Write((short)MESSAGE_ID.FloatVariables);
                // add owner nuid
                ownerNuid.Write(message);
                // add aircraft ID
                message.Write(netId);
                // save buffer position
                long countPosition = message.BaseStream.Position;
                // variable count
                ushort count = 0;
                // write placeholder count
                message.Write(count);

                // for each variable in the set
                foreach (var variable in variables)
                {
                    // add variable ID
                    message.Write(variable.Key);
                    // add value
                    message.Write(variable.Value);
                    // update count
                    count++;
                    // check if message is full
                    if (count >= MAX_FLOAT_VARIABLES)
                    {
                        // no change times
                        message.Write((ushort)0);
                        // modify placeholder
                        message.BaseStream.Position = countPosition;
                        message.Write(count);
                        // check for valid nuid
                        if (nuid.Valid())
                        {
                            // send message
                            localNode.Send(nuid);
                        }
                        else
                        {
                            // broadcast message
                            localNode.Broadcast();
                        }
                        // prepare next message
                        message = localNode.PrepareMessage(new LocalNode.Nuid(), false);
                        // add header
                        message.Write(Sim.VERSION);
                        // add message ID
                        message.Write((short)MESSAGE_ID.FloatVariables);
                        // add owner nuid
                        ownerNuid.Write(message);
                        // add aircraft ID
                        message.Write(netId);
                        // save buffer position
                        countPosition = message.BaseStream.Position;
                        // reset count
                        count = 0;
                        // write placeholder count
                        message.Write(count);
                    }
                }

                // check if message is has data
                if (count > 0)
                {
                    // no change times
                    message.Write((ushort)0);
                    // modify placeholder
                    message.BaseStream.Position = countPosition;
                    message.Write(count);
                    // check for valid nuid
                    if (nuid.Valid())
                    {
                        // send message
                        localNode.Send(nuid);
                    }
                    else
                    {
                        // broadcast message
                        localNode.Broadcast();
                    }
                }
            }
            catch (Exception ex)
            {
                main.MonitorEvent("ERROR - Failed to send FloatVariables message - " + ex.Message);
            }
        }

        /// <summary>
        /// Write message for integer variables
        /// </summary>
        public void SendString8VariablesMessage(LocalNode.Nuid nuid, uint netId, Dictionary<uint, string> variables, LocalNode.Nuid ownerNuid)
        {
            try
            {
                // prepare message
                BinaryWriter message = localNode.PrepareMessage(new LocalNode.Nuid(), false);
                // add header
                message.Write(Sim.VERSION);
                // add message ID
                message.Write((short)MESSAGE_ID.String8Variables);
                // add owner nuid
                ownerNuid.Write(message);
                // add aircraft ID
                message.Write(netId);
                // save buffer position
                long countPosition = message.BaseStream.Position;
                // variable count
                ushort count = 0;
                // write placeholder count
                message.Write(count);

                // for each variable in the set
                foreach (var variable in variables)
                {
                    // add variable ID
                    message.Write(variable.Key);
                    // add value
                    message.Write(variable.Value);
                    // update count
                    count++;
                    // check if message is full
                    if (count >= MAX_STRING8_VARIABLES)
                    {
                        // no change times
                        message.Write((ushort)0);
                        // modify placeholder
                        message.BaseStream.Position = countPosition;
                        message.Write(count);
                        // check for valid nuid
                        if (nuid.Valid())
                        {
                            // send message
                            localNode.Send(nuid);
                        }
                        else
                        {
                            // broadcast message
                            localNode.Broadcast();
                        }
                        // prepare next message
                        message = localNode.PrepareMessage(new LocalNode.Nuid(), false);
                        // add header
                        message.Write(Sim.VERSION);
                        // add message ID
                        message.Write((short)MESSAGE_ID.String8Variables);
                        // add owner nuid
                        ownerNuid.Write(message);
                        // add aircraft ID
                        message.Write(netId);
                        // save buffer position
                        countPosition = message.BaseStream.Position;
                        // reset count
                        count = 0;
                        // write placeholder count
                        message.Write(count);
                    }
                }

                // check if message is has data
                if (count > 0)
                {
                    // no change times
                    message.Write((ushort)0);
                    // modify placeholder
                    message.BaseStream.Position = countPosition;
                    message.Write(count);
                    // check for valid nuid
                    if (nuid.Valid())
                    {
                        // send message
                        localNode.Send(nuid);
                    }
                    else
                    {
                        // broadcast message
                        localNode.Broadcast();
                    }
                }
            }
            catch (Exception ex)
            {
                main.MonitorEvent("ERROR - Failed to send String8Variables message - " + ex.Message);
            }
        }

        /// <summary>
        /// Write message for weather request
        /// </summary>
        public void WriteWeatherRequestMessage(uint netId)
        {
            // prepare message
            BinaryWriter message = localNode.PrepareMessage(new LocalNode.Nuid(), true);

            // add header
            message.Write(Sim.VERSION);
            // add message ID
            message.Write((short)MESSAGE_ID.WeatherRequest);
            // add net ID
            message.Write(netId);
        }

        /// <summary>
        /// Write message for weather reply
        /// </summary>
        public void WriteWeatherReplyMessage(string metar)
        {
            // prepare message
            BinaryWriter message = localNode.PrepareMessage(new LocalNode.Nuid(), true);

            // add header
            message.Write(Sim.VERSION);
            // add message ID
            message.Write((short)MESSAGE_ID.WeatherReply);
            // add metar
            message.Write(metar);
        }

        /// <summary>
        /// Write message for weather update
        /// </summary>
        public void WriteWeatherUpdateMessage(string metar)
        {
            // prepare message
            BinaryWriter message = localNode.PrepareMessage(new LocalNode.Nuid(), false);

            // add header
            message.Write(Sim.VERSION);
            // add message ID
            message.Write((short)MESSAGE_ID.WeatherUpdate);
            // add metar
            message.Write(metar);
        }

        /// <summary>
        /// Scheduled shared data message
        /// </summary>
        LocalNode.Nuid scheduleSharedData = new();

        /// <summary>
        /// Schedule a shared data message
        /// </summary>
        /// <param name="nuid"></param>
        public void ScheduleSharedDataMessage(LocalNode.Nuid nuid)
        {
            // check if not scheduled
            if (scheduleSharedData.Invalid())
            {
                // schedule
                scheduleSharedData = nuid;
            }
        }

        /// <summary>
        /// Write message for shared data
        /// </summary>
        public void SendSharedDataMessage(LocalNode.Nuid nuid)
        {
            // prepare message
            BinaryWriter message = localNode.PrepareMessage(new LocalNode.Nuid(), false);

            // add header
            message.Write(Sim.VERSION);
            // add message ID
            message.Write((short)MESSAGE_ID.SharedData);

            // add share cockpit
            byte share = 0;
            if (main.log.ShareCockpit(nuid) || Settings.Default.ShareCockpitEveryone) share |= 0x01;
            if (nuid == shareFlightControls) share |= 0x02;
            if (nuid == shareAncillaryControls) share |= 0x04;
            if (nuid == shareNavControls) share |= 0x08;
            message.Write(share);
            // add nickname
            message.Write(main.settingsNickname);
            // add guid
            Guid guid = main.guid;
            message.Write(guid.ToByteArray());
            // add flags
            byte flags = 0;
            // check for hub
            if (main.settingsHub)
            {
                // add hub flag
                flags |= 0x01;
            }
            // check for ATC
            if (main.settingsAtc)
            {
                // add ATC flag
                flags |= 0x02;
            }
            // check for simulator connected
            if (main.sim != null && main.sim.Connected)
            {
                // add simulator connected flag
                flags |= 0x04;
            }
            // add flags
            message.Write((byte)flags);
            // add airport
            message.Write(main.settingsAtcAirport);
            // add level
            message.Write((byte)Settings.Default.AtcLevel);
            // add frequency
            message.Write((ushort)Settings.Default.AtcFrequency);
            // add activity circle
            message.Write((byte)main.settingsActivityCircle);
            // add version
            message.Write(Main.Version);
            // add simulator
            message.Write(main.sim != null ? main.sim.GetSimulatorName() : Resources.Strings.NotConnected);
            // send to node
            localNode.Send(nuid);
        }

        /// <summary>
        /// Write message for status request
        /// </summary>
        public void WriteStatusRequestMessage(bool requestHubList)
        {
            // prepare message
            BinaryWriter message = localNode.PrepareMessage(new LocalNode.Nuid(), false);

            // add header
            message.Write(Sim.VERSION);
            // add message ID
            message.Write((short)MESSAGE_ID.StatusRequest);
            // add hub flag
            message.Write((byte)(main.settingsHub ? 1 : 0));
            // add request flag
            message.Write((byte)(requestHubList ? 1 : 0));
            // add uuid
            message.Write(main.uuid);
        }

        /// <summary>
        /// Write message for status reply
        /// </summary>
        public void WriteStatusMessage()
        {
            // prepare message
            BinaryWriter message = localNode.PrepareMessage(new LocalNode.Nuid(), false);

            // add header
            message.Write(Sim.VERSION);
            // add message ID
            message.Write((short)MESSAGE_ID.Status);
            // add guid
            Guid guid = main.guid;
            message.Write(guid.ToByteArray());
            // add application version
            message.Write(Main.Version);
            // add node count
            message.Write((ushort)localUserList.Count);
            // get main ATC
            int atcCount = GetMainAtc(out string airport, out int level);
            // add ATC count
            message.Write((ushort)atcCount);
            // check for ATC
            if (atcCount > 0)
            {
                // add airport
                message.Write(airport);
                // add level
                message.Write((byte)level);
            }
            // count of objects
            ushort planes = 0;
            ushort helicopters = 0;
            ushort boats = 0;
            ushort vehicles = 0;
            // check for sim
            if (main.sim != null)
            {
                // for each object
                foreach (var obj in main.sim.objectList)
                {
                    // check for network object
                    if (obj.owner == Sim.Obj.Owner.Network || main.sim.IsBroadcast(obj))
                    {
                        // accumulate counts
                        if (obj is Sim.Plane)
                        {
                            planes++;
                        }
                        else if (obj is Sim.Helicopter)
                        {
                            helicopters++;
                        }
                        else if (obj is Sim.Boat)
                        {
                            boats++;
                        }
                        else if (obj is Sim.Vehicle)
                        {
                            vehicles++;
                        }
                    }
                }
            }
            // add plane count
            message.Write(planes);
            // add helicopter count
            message.Write(helicopters);
            // add boat count
            message.Write(boats);
            // add vehicle count
            message.Write(vehicles);
            // add hub
            message.Write(main.settingsHub);
            // hub details
            if (main.settingsHub)
            {
                // get address
                string address = main.settingsHubDomain;
                // check for blank address
                if (address.Length == 0)
                {
                    // use my IP
                    address = Settings.Default.MyIp;
                }
                // add hub address
                message.Write(address);
                // add hub name
                message.Write(main.settingsHubName);
                // add hub about
                message.Write(main.settingsHubAbout);
                // add hub voip
                message.Write(main.settingsHubVoip);
                // add hub next event
                message.Write(main.settingsHubEvent);
                // add airport
                message.Write(main.settingsAtc ? main.settingsAtcAirport : "");
                // add activity circle
                message.Write(main.settingsActivityCircle);
                // add global flag
                byte flags = 0;
                if (localNode.GlobalSession) flags |= 0x02;
                if (localNode.Password) flags |= 0x04;
                message.Write(flags);
            }
        }

        /// <summary>
        /// Send a Status reply to `endPoint` via JFP2 if that peer has completed JFP2 negotiation and
        /// agreed on the Status message class; otherwise fall back to the untouched legacy
        /// WriteStatusMessage()+Send() path, byte-identical to what an unpatched build would send.
        /// See docs/protocol-v2-implementation-plan.md Phase 2 and docs/protocol-v2-architecture.md
        /// §8.2 for the split-send pattern this follows.
        /// </summary>
        void SendStatus(IPEndPoint endPoint)
        {
            if (localNode.TryGetJfp2AppPeer(endPoint, Jfp2.MessageClasses.Status, out LocalNode.Nuid nuid, out byte version))
            {
                Jfp2.Codecs.StatusUpdate status = BuildJfp2StatusUpdate();
                var codec = Jfp2.Codecs.CodecRegistry.Resolve<Jfp2.Codecs.StatusUpdate>(Jfp2.MessageClasses.Status, version);
                Span<byte> buffer = stackalloc byte[512]; // several free-text hub fields; sized generously
                int length = codec.Encode(status, buffer);
                localNode.SendJfp2Application(nuid, Jfp2.MessageClasses.Status, buffer[..length]);
            }
            else
            {
                WriteStatusMessage();
                localNode.Send(endPoint);
            }
        }

        /// <summary>
        /// Send a StatusRequest to `endPoint` via JFP2 if negotiated, otherwise fall back to the
        /// untouched legacy WriteStatusRequestMessage()+Send() path. See SendStatus's remarks.
        /// </summary>
        void SendStatusRequest(IPEndPoint endPoint, bool requestHubList)
        {
            if (localNode.TryGetJfp2AppPeer(endPoint, Jfp2.MessageClasses.StatusRequest, out LocalNode.Nuid nuid, out byte version))
            {
                Jfp2.Codecs.StatusRequestUpdate request = new()
                {
                    HubEnabled = main.settingsHub,
                    HubListRequested = requestHubList,
                    Uuid = main.uuid,
                };
                var codec = Jfp2.Codecs.CodecRegistry.Resolve<Jfp2.Codecs.StatusRequestUpdate>(Jfp2.MessageClasses.StatusRequest, version);
                Span<byte> buffer = stackalloc byte[Jfp2.Codecs.StatusRequestV1Codec.Size];
                int length = codec.Encode(request, buffer);
                localNode.SendJfp2Application(nuid, Jfp2.MessageClasses.StatusRequest, buffer[..length]);
            }
            else
            {
                WriteStatusRequestMessage(requestHubList);
                localNode.Send(endPoint);
            }
        }

        /// <summary>
        /// Gather this node's current status fields into a version-agnostic StatusUpdate value - the
        /// exact same fields WriteStatusMessage() writes to the legacy wire format, so a JFP2-
        /// negotiated peer learns the same information a legacy peer would.
        /// </summary>
        Jfp2.Codecs.StatusUpdate BuildJfp2StatusUpdate()
        {
            int atcCount = GetMainAtc(out string airport, out int level);

            ushort planes = 0, helicopters = 0, boats = 0, vehicles = 0;
            if (main.sim != null)
            {
                foreach (var obj in main.sim.objectList)
                {
                    if (obj.owner == Sim.Obj.Owner.Network || main.sim.IsBroadcast(obj))
                    {
                        if (obj is Sim.Plane) planes++;
                        else if (obj is Sim.Helicopter) helicopters++;
                        else if (obj is Sim.Boat) boats++;
                        else if (obj is Sim.Vehicle) vehicles++;
                    }
                }
            }

            var status = new Jfp2.Codecs.StatusUpdate
            {
                Guid = main.guid,
                AppVersion = Main.Version,
                Users = (ushort)localUserList.Count,
                AtcCount = (ushort)atcCount,
                AtcAirport = airport,
                AtcLevel = level,
                Planes = planes,
                Helicopters = helicopters,
                Boats = boats,
                Vehicles = vehicles,
                HubEnabled = main.settingsHub,
            };

            if (main.settingsHub)
            {
                string address = main.settingsHubDomain;
                if (address.Length == 0)
                {
                    address = Settings.Default.MyIp;
                }
                status.Address = address;
                status.Name = main.settingsHubName;
                status.About = main.settingsHubAbout;
                status.Voip = main.settingsHubVoip;
                status.NextEvent = main.settingsHubEvent;
                status.Airport = main.settingsAtc ? main.settingsAtcAirport : "";
                status.ActivityCircle = main.settingsActivityCircle;
                status.GlobalSession = localNode.GlobalSession;
                status.PasswordRequired = localNode.Password;
            }

            return status;
        }

        /// <summary>
        /// Dispatch a decoded JFP2 application-partition message to the same downstream logic the
        /// legacy protocol's ReceiveMsg switch already runs - see HandleStatusRequest/HandleStatus.
        /// Wired as LocalNode.jfp2ReceiveNotify in this class's constructor.
        /// </summary>
        void HandleJfp2Application(IPEndPoint endPoint, LocalNode.Nuid nuid, byte messageClass, byte schemaVersion, ReadOnlySpan<byte> payload)
        {
            switch (messageClass)
            {
                case Jfp2.MessageClasses.StatusRequest:
                    try
                    {
                        Jfp2.Codecs.StatusRequestUpdate request = Jfp2.Codecs.CodecRegistry.Resolve<Jfp2.Codecs.StatusRequestUpdate>(messageClass, schemaVersion).Decode(payload);
                        HandleStatusRequest(endPoint, nuid, request);
                    }
                    catch (Exception ex)
                    {
                        main.MonitorEvent("ERROR: Failed to read JFP2 StatusRequest message. " + ex.Message);
                    }
                    break;

                case Jfp2.MessageClasses.Status:
                    try
                    {
                        Jfp2.Codecs.StatusUpdate status = Jfp2.Codecs.CodecRegistry.Resolve<Jfp2.Codecs.StatusUpdate>(messageClass, schemaVersion).Decode(payload);
                        // A JFP2 peer always sends every Status field explicitly (docs/protocol-v2-
                        // design.md §1.3/§7.5's whole point is no more conditional/EOF-sensed shapes),
                        // so there is no legacy dataVersion-gated flags byte to reconstruct here.
                        HandleStatus(endPoint, nuid, status, legacyDataVersion: 0);
                    }
                    catch (Exception ex)
                    {
                        main.MonitorEvent("ERROR: Failed to read JFP2 Status message. " + ex.Message);
                    }
                    break;

                case Jfp2.MessageClasses.Identity:
                    try
                    {
                        Jfp2.Codecs.IdentityUpdate identity = Jfp2.Codecs.CodecRegistry.Resolve<Jfp2.Codecs.IdentityUpdate>(messageClass, schemaVersion).Decode(payload);
                        HandleJfp2Identity(nuid, identity);
                    }
                    catch (Exception ex)
                    {
                        main.MonitorEvent("ERROR: Failed to read JFP2 Identity message. " + ex.Message);
                    }
                    break;

                case Jfp2.MessageClasses.VariableSync:
                    try
                    {
                        Jfp2.Codecs.VariableSyncUpdate sync = Jfp2.Codecs.CodecRegistry.Resolve<Jfp2.Codecs.VariableSyncUpdate>(messageClass, schemaVersion).Decode(payload);
                        HandleJfp2VariableSync(nuid, sync);
                    }
                    catch (Exception ex)
                    {
                        main.MonitorEvent("ERROR: Failed to read JFP2 VariableSync message. " + ex.Message);
                    }
                    break;

                case Jfp2.MessageClasses.Position:
                    try
                    {
                        Jfp2.Codecs.PositionUpdate update = Jfp2.Codecs.CodecRegistry.Resolve<Jfp2.Codecs.PositionUpdate>(messageClass, schemaVersion).Decode(payload);
                        HandleJfp2Position(nuid, update);
                    }
                    catch (Exception ex)
                    {
                        main.MonitorEvent("ERROR: Failed to read JFP2 Position message. " + ex.Message);
                    }
                    break;

                case Jfp2.MessageClasses.Event:
                    try
                    {
                        Jfp2.Codecs.EventUpdate evt = Jfp2.Codecs.CodecRegistry.Resolve<Jfp2.Codecs.EventUpdate>(messageClass, schemaVersion).Decode(payload);
                        HandleSimEvent(nuid, evt.ObjectId, evt.EventId, evt.Data);
                    }
                    catch (Exception ex)
                    {
                        main.MonitorEvent("ERROR: Failed to read JFP2 Event message. " + ex.Message);
                    }
                    break;

                case Jfp2.MessageClasses.FlightPlan:
                    try
                    {
                        Jfp2.Codecs.FlightPlanUpdate flightPlan = Jfp2.Codecs.CodecRegistry.Resolve<Jfp2.Codecs.FlightPlanUpdate>(messageClass, schemaVersion).Decode(payload);
                        // JFP2 always writes every field explicitly (no legacy version-gated shape),
                        // so there's no real wire "version" byte to pass through - 1 is what every
                        // legacy sender already writes there unconditionally (see WriteFlightPlanMessage)
                        HandleFlightPlan(nuid, flightPlan.ObjectId, 1, flightPlan);
                    }
                    catch (Exception ex)
                    {
                        main.MonitorEvent("ERROR: Failed to read JFP2 FlightPlan message. " + ex.Message);
                    }
                    break;

                case Jfp2.MessageClasses.Notes:
                    try
                    {
                        Jfp2.Codecs.NoteUpdate note = Jfp2.Codecs.CodecRegistry.Resolve<Jfp2.Codecs.NoteUpdate>(messageClass, schemaVersion).Decode(payload);
                        Guid guid = note.Guid;
                        main.notes.ProcessCommsNote(ref guid, note.Nickname, note.Callsign, note.NoteId, note.Age, note.Channel, note.Text);
                    }
                    catch (Exception ex)
                    {
                        main.MonitorEvent("ERROR: Failed to read JFP2 Notes message. " + ex.Message);
                    }
                    break;

                case Jfp2.MessageClasses.Weather:
                    try
                    {
                        Jfp2.Codecs.WeatherReport report = Jfp2.Codecs.CodecRegistry.Resolve<Jfp2.Codecs.WeatherReport>(messageClass, schemaVersion).Decode(payload);
                        if (report.Metar.Length > 0)
                        {
                            main.sim?.SetWeatherObservation(nuid, report.Metar);
                        }
                    }
                    catch (Exception ex)
                    {
                        main.MonitorEvent("ERROR: Failed to read JFP2 Weather message. " + ex.Message);
                    }
                    break;

                case Jfp2.MessageClasses.WeatherReply:
                    try
                    {
                        Jfp2.Codecs.WeatherReport report = Jfp2.Codecs.CodecRegistry.Resolve<Jfp2.Codecs.WeatherReport>(messageClass, schemaVersion).Decode(payload);
                        if (report.Metar.Length > 0)
                        {
                            main.sim?.SetWeatherObservation(report.Metar);
                        }
                    }
                    catch (Exception ex)
                    {
                        main.MonitorEvent("ERROR: Failed to read JFP2 WeatherReply message. " + ex.Message);
                    }
                    break;

                default:
                    // No other application message class is implemented over JFP2 yet (Phase 6+).
                    break;
            }
        }

        /// <summary>
        /// Shared StatusRequest handling for both the legacy StatusRequest message and its JFP2
        /// counterpart - the wire read differs per protocol, but what happens once the fields are
        /// known is identical, matching the "codec decodes into a version-agnostic value, shared logic
        /// downstream" principle used for the hub translation bridge (docs/protocol-v2-design.md §7.7).
        /// </summary>
        void HandleStatusRequest(IPEndPoint endPoint, LocalNode.Nuid nuid, Jfp2.Codecs.StatusRequestUpdate request)
        {
            if (localNode.Connected)
            {
                // check for hub
                if (request.HubEnabled)
                {
                    // submit new hub
                    SubmitHub(endPoint);
                }

                try
                {
                    // send reply (via JFP2 or legacy, whichever this peer negotiated - see SendStatus)
                    SendStatus(endPoint);
                }
                catch (Exception ex)
                {
                    main.MonitorEvent("Failed to write status reply message: " + ex.Message);
                }

                // check if this is a hub
                if (main.settingsHub)
                {
                    // check for request
                    if (request.HubListRequested)
                    {
                        // write hub list
                        SendHubListMessage(endPoint);
                    }

                    try
                    {
                        // register
                        RegisterOnlineUser(request.Uuid, nuid, (ushort)endPoint.Port);
                    }
                    catch { }
                }
            }
        }

        /// <summary>
        /// Shared Status handling for both the legacy Status message and its JFP2 counterpart. See
        /// HandleStatusRequest's remarks. `legacyDataVersion` is purely the write-only, never-read
        /// Hub.dataVersion diagnostic field (docs/network-protocol.md never documents a reader for it,
        /// and neither does this codebase) - 0 for a JFP2-sourced update, since JFP2 has no single
        /// scalar analogous to the legacy per-message DataVersion.
        /// </summary>
        void HandleStatus(IPEndPoint endPoint, LocalNode.Nuid nuid, Jfp2.Codecs.StatusUpdate status, ushort legacyDataVersion)
        {
            // check for hub
            if (status.HubEnabled)
            {
                // remove from pending list
                pendingHubList.Remove(endPoint);

                // check for maximum IPs and not this hub
                if (HubCount_IP(nuid) < MAX_IP_HUBS && nuid != localNode.GetLocalNuid())
                {
                    // find hub in the list
                    Hub hub = hubList.Find(h => h.nuid == nuid);
                    if (hub == null)
                    {
                        // create new entry
                        hub = new Hub();
                        // add hub to the list
                        hubList.Add(hub);
                        // hub has changed
                        hubListChanged = true;
                        main.MonitorEvent("Added new hub '" + status.Name + "' - '" + UuidToString(MakeUuid(status.Guid)) + "'");
                    }
                    // check if hub changed
                    else if (
                        hub.guid.Equals(status.Guid) == false ||
                        hub.appVersion.Equals(status.AppVersion) == false ||
                        hub.addressText.Equals(status.Address) == false ||
                        hub.name.Equals(status.Name) == false ||
                        hub.about.Equals(status.About) == false ||
                        hub.voip.Equals(status.Voip) == false ||
                        hub.nextEvent.Equals(status.NextEvent) == false ||
                        hub.airport.Equals(status.Airport) == false ||
                        hub.activityCircle != status.ActivityCircle ||
                        hub.globalSession != status.GlobalSession ||
                        hub.password != status.PasswordRequired
                        )
                    {
                        // hub has changed
                        hubListChanged = true;
                    }

                    // check for global enabled
                    if (localNode.GlobalSession)
                    {
                        // check for first contact with global hub
                        if (hub.globalSession == false && status.GlobalSession)
                        {
                            // join with session
                            Join(endPoint, 0);
                        }
                    }

                    // update hub details
                    hub.nuid = nuid;
                    hub.endPoint = endPoint;
                    hub.port = (ushort)endPoint.Port;
                    hub.dateTime = DateTime.Now;
                    hub.guid = status.Guid;
                    hub.appVersion = status.AppVersion;
                    hub.dataVersion = legacyDataVersion;
                    hub.online = true;
                    hub.offlineTime = main.ElapsedTime + OFFLINE_TIME;
                    hub.users = status.Users;
                    hub.atcCount = status.AtcCount;
                    hub.atcAirport = status.AtcAirport;
                    hub.atcLevel = status.AtcLevel;
                    hub.planes = status.Planes;
                    hub.helicopters = status.Helicopters;
                    hub.boats = status.Boats;
                    hub.vehicles = status.Vehicles;
                    hub.addressText = status.Address;
                    hub.name = status.Name;
                    hub.about = status.About;
                    hub.voip = status.Voip;
                    hub.nextEvent = status.NextEvent;
                    hub.airport = status.Airport;
                    hub.activityCircle = status.ActivityCircle;
                    hub.globalSession = status.GlobalSession;
                    hub.password = status.PasswordRequired;
                }
            }
            else
            {
                // check if this used to be a hub
                Hub hub = hubList.Find(h => h.nuid == nuid);
                if (hub != null)
                {
                    main.MonitorEvent("Removed hub '" + hub.name + "' - '" + UuidToString(MakeUuid(status.Guid)) + "'");
                    // remove
                    hubList.Remove(hub);
                    // hub has changed
                    hubListChanged = true;
                }
            }

            // check for existing entry
            AddressBook.AddressBookEntry entry = main.addressBook.entries.Find(f => f.endPoint.Address.Equals(endPoint.Address));
            if (entry != null)
            {
                // update entry
                entry.online = true;
                entry.offlineTime = main.ElapsedTime + OFFLINE_TIME;
            }
        }

        #region JFP2 Identity/VariableSync (Phase 3)

        // docs/protocol-v2-implementation-plan.md Phase 3. Unlike Status (Phase 2), Identity and
        // VariableSync have no single existing legacy call site to redirect - Identity is brand new
        // (its fields currently ride inside every ObjectPosition/AircraftPosition tick, see
        // WriteObjectPositionVelocityMessage/WriteAircraftPositionMessage) and VariableSync unifies
        // three legacy messages sent from one broadcast-everyone call (Sim.cs's variablesTimer loop).
        // Both are therefore wired from Sim.cs's existing per-object broadcast loop via the small
        // per-peer entry points below (SendJfp2IdentityIfNeeded / SendVariableUpdate), each of which
        // independently decides JFP2 vs legacy for that one peer - see docs/protocol-v2-architecture.md
        // §8.2's split-send pattern, same idea Phase 2 already used for Status.
        //
        // SendJfp2IdentityIfNeeded (below) also gets the efficiency win when relayed through a JFP2-
        // capable hub to another indirect JFP2 peer (Jfp2Bridge design plan Increment 2 - see
        // TryGetJfp2RelayPeer/SendJfp2RelayApplication in Node.cs), since the hub forwards the already-
        // encoded bytes unchanged rather than needing to decode/re-encode anything. VariableSync
        // (SendVariableUpdate) is not yet wired for relay - still deliberately out of scope, see
        // docs/protocol-v2-implementation-plan.md.
        //
        // Still out of scope (see docs/protocol-v2-implementation-plan.md for the full reasoning):
        // Jfp2Bridge, the hub-role decode/re-encode translator between a JFP2 peer and a legacy-only
        // peer (design doc §7.7) - that's a genuinely different case from the relay above, since there
        // the hub has no shared wire format to forward unchanged. A JFP2 peer talking through a
        // legacy-only (or not-yet-negotiating) hub still simply never negotiates JFP2 with it and uses
        // legacy throughout, unaffected by any of this.

        /// <summary>
        /// How often (seconds) to resend Identity to a peer even if nothing changed, so a peer that
        /// joins mid-session or missed a single unreliable datagram still converges within a bounded
        /// window - design doc §6.2 says "on the order of every 3-5 seconds".
        /// </summary>
        const double JFP2_IDENTITY_HEARTBEAT_INTERVAL = 4.0;

        /// <summary>
        /// Last Identity actually sent to a given peer for a given local object, so sends only go out
        /// on change or heartbeat - see SendJfp2IdentityIfNeeded. Keyed by (Obj.netId, peer Nuid);
        /// entries are for objects THIS node broadcasts, so netId alone (always one of our own ids) is
        /// enough without an owner qualifier.
        /// </summary>
        class Jfp2IdentitySendState
        {
            public bool Sent;
            public Jfp2.Codecs.IdentityUpdate Last;
            public double LastSentTime;
        }
        readonly Dictionary<(uint NetId, LocalNode.Nuid Peer), Jfp2IdentitySendState> jfp2IdentitySendState = [];

        /// <summary>
        /// Build this object's current Identity fields, sourced exactly the way
        /// WriteObjectPositionVelocityMessage/WriteAircraftPositionMessage already source them for the
        /// legacy wire (JoinFS/Network.cs) - including the existing legacy quirk that a non-Aircraft
        /// Obj's icaoType/icaoAirline/registration are never populated (only Sim.Aircraft.flightPlan
        /// carries those), preserved here for parity rather than "fixed", since this is a transport
        /// migration, not a behavior change.
        /// </summary>
        Jfp2.Codecs.IdentityUpdate BuildJfp2IdentityUpdate(Sim.Obj obj)
        {
            Sim.Aircraft aircraft = obj as Sim.Aircraft;
            return new Jfp2.Codecs.IdentityUpdate
            {
                ObjectId = obj.netId,
                IsAircraft = aircraft != null,
                IsPlane = obj is Sim.Plane,
                Callsign = aircraft?.flightPlan.callsign ?? "",
                Model = obj.ModelTitle,
                Livery = obj.ownerLivery,
                IcaoType = aircraft?.flightPlan.icaoType ?? "",
                IcaoAirline = aircraft?.flightPlan.icaoAirline ?? "",
                Registration = aircraft?.flightPlan.registration ?? "",
                FlightNumber = aircraft?.flightPlan.flightNumber ?? "",
                ClassCode = obj.ownerClassCode,
                Wtc = obj.ownerWtc,
                ClassCodeConfirmed = obj.ownerClassCodeConfirmed,
                TypeRole = (byte)obj.typerole,
            };
        }

        static bool Jfp2IdentityEquals(in Jfp2.Codecs.IdentityUpdate a, in Jfp2.Codecs.IdentityUpdate b)
        {
            return a.IsAircraft == b.IsAircraft && a.IsPlane == b.IsPlane
                && a.Callsign == b.Callsign && a.Model == b.Model && a.Livery == b.Livery
                && a.IcaoType == b.IcaoType && a.IcaoAirline == b.IcaoAirline && a.Registration == b.Registration
                && a.FlightNumber == b.FlightNumber
                && a.ClassCode == b.ClassCode && a.Wtc == b.Wtc
                && a.ClassCodeConfirmed == b.ClassCodeConfirmed && a.TypeRole == b.TypeRole;
        }

        /// <summary>
        /// Send Identity for `obj` to `peerNuid` if that peer negotiated JFP2 Identity, directly or
        /// through a relay (see TryGetJfp2RelayPeer/SendJfp2RelayApplication - Jfp2Bridge design plan
        /// Increment 2), AND (anything about the identity changed since the last send to this specific
        /// peer, or the heartbeat interval elapsed). A no-op for a peer reachable only via legacy -
        /// legacy peers keep getting identity fields the unchanged way, inline in every Position
        /// message.
        ///
        /// Wiring this for relay is not optional scope creep: SendJfp2Position's identity-before-
        /// position ordering guard (see its own comment) checks jfp2IdentitySendState regardless of
        /// whether the peer is direct or relayed, so if this method never sent Identity via JFP2 to an
        /// indirect peer, that guard would withhold Position for that peer forever - relaying Position
        /// without also relaying Identity would be a silent regression (an indirect peer would stop
        /// seeing this aircraft at all), not a smaller, safer increment.
        /// </summary>
        public void SendJfp2IdentityIfNeeded(LocalNode.Nuid peerNuid, Sim.Obj obj)
        {
            bool direct = localNode.TryGetJfp2AppPeer(peerNuid, Jfp2.MessageClasses.Identity, out byte version);
            LocalNode.Nuid hubNuid = default;
            if (!direct && !localNode.TryGetJfp2RelayPeer(peerNuid, Jfp2.MessageClasses.Identity, out hubNuid, out version))
            {
                return;
            }

            Jfp2.Codecs.IdentityUpdate current = BuildJfp2IdentityUpdate(obj);
            var key = (obj.netId, peerNuid);
            if (!jfp2IdentitySendState.TryGetValue(key, out Jfp2IdentitySendState state))
            {
                state = new Jfp2IdentitySendState();
                jfp2IdentitySendState[key] = state;
            }

            bool changed = !state.Sent || !Jfp2IdentityEquals(state.Last, current);
            bool heartbeatDue = main.ElapsedTime - state.LastSentTime >= JFP2_IDENTITY_HEARTBEAT_INTERVAL;
            if (!changed && !heartbeatDue)
            {
                return;
            }

            var codec = Jfp2.Codecs.CodecRegistry.Resolve<Jfp2.Codecs.IdentityUpdate>(Jfp2.MessageClasses.Identity, version);
            Span<byte> buffer = stackalloc byte[512];
            int length = codec.Encode(current, buffer);
            if (direct)
            {
                localNode.SendJfp2Application(peerNuid, Jfp2.MessageClasses.Identity, buffer[..length]);
            }
            else
            {
                localNode.SendJfp2RelayApplication(hubNuid, peerNuid, Jfp2.MessageClasses.Identity, buffer[..length]);
            }

            state.Sent = true;
            state.Last = current;
            state.LastSentTime = main.ElapsedTime;
        }

        /// <summary>
        /// Identity messages received for an object we don't know about yet - cached here so a
        /// shortly-following JFP2 Position (Phase 4) can use it to create the object, honoring the
        /// send-side guarantee (Network.SendJfp2Position) that Identity always reaches a peer before
        /// that object's first Position does (docs/protocol-v2-design.md §7.7). Cleaned up by
        /// DoJfp2IdentityCleanup alongside jfp2IdentitySendState.
        /// </summary>
        readonly Dictionary<(uint NetId, LocalNode.Nuid Sender), Jfp2.Codecs.IdentityUpdate> jfp2PendingIdentity = [];

        /// <summary>
        /// Apply a received Identity update onto the matching already-known object, or cache it if the
        /// object doesn't exist locally yet (see jfp2PendingIdentity) - the sender's own Position send
        /// path guarantees Identity precedes that object's first Position, so this is the normal path
        /// for a brand-new object, not a fallback.
        /// </summary>
        void HandleJfp2Identity(LocalNode.Nuid nuid, Jfp2.Codecs.IdentityUpdate identity)
        {
            if (main.sim == null)
            {
                return;
            }

            Sim.Obj obj = main.sim.objectList.Find(o => o.ownerNuid == nuid && o.netId == identity.ObjectId);
            if (obj == null)
            {
                jfp2PendingIdentity[(identity.ObjectId, nuid)] = identity;
                return;
            }

            bool modelChanged = identity.Model != obj.ownerModel;

            // same entry point RemoveObjectFromSim/UpdateAircraft already use to apply model/livery/
            // classCode/wtc/typerole and re-run substitution matching (JoinFS/Sim.cs)
            main.sim.UpdateObject(obj, identity.Model, identity.Livery, identity.IcaoType, identity.IcaoAirline, identity.ClassCode, identity.Wtc, identity.ClassCodeConfirmed, identity.TypeRole);

            if (modelChanged)
            {
                // force a de-spawn/respawn under the new model, matching legacy's own model-change
                // handling in Sim.UpdateAircraft
                main.sim.RemoveObjectFromSim(obj);
            }

            if (obj is Sim.Aircraft aircraft)
            {
                bool callsignChanged = identity.Callsign != aircraft.flightPlan.callsign;
                aircraft.flightPlan.callsign = identity.Callsign;
                aircraft.flightPlan.registration = identity.Registration;
                if (callsignChanged)
                {
                    // retune ATC ID display, matching legacy's SetAtcId-on-callsign-change behavior
                    main.sim.SetAtcId(nuid);
                }
            }
        }

        /// <summary>
        /// Periodic cleanup of jfp2IdentitySendState so it doesn't grow unbounded over a long session
        /// - entries for a peer that's no longer connected are simply stale bookkeeping. Called from
        /// DoWork() on a slow timer; cheap enough (a handful of dictionary entries per broadcast
        /// object) that an occasional pass is more than sufficient.
        /// </summary>
        readonly Timer jfp2IdentityCleanupTimer = new(30.0);
        readonly List<(uint NetId, LocalNode.Nuid Peer)> tempJfp2IdentityKeys = [];
        readonly List<(uint NetId, LocalNode.Nuid Sender)> tempJfp2PendingIdentityKeys = [];
        void DoJfp2IdentityCleanup()
        {
            if (!jfp2IdentityCleanupTimer.Elapsed(main.ElapsedTime))
            {
                return;
            }

            foreach (var key in jfp2IdentitySendState.Keys)
            {
                if (!localNode.NodeReceiveEstablished(key.Peer) && !localNode.NodeSendEstablished(key.Peer))
                {
                    tempJfp2IdentityKeys.Add(key);
                }
            }
            foreach (var key in tempJfp2IdentityKeys)
            {
                jfp2IdentitySendState.Remove(key);
            }
            tempJfp2IdentityKeys.Clear();

            // same idea for jfp2PendingIdentity - a cached Identity whose sender disconnected before
            // ever sending the matching Position (Phase 4) is just stale bookkeeping
            foreach (var key in jfp2PendingIdentity.Keys)
            {
                if (!localNode.NodeReceiveEstablished(key.Sender) && !localNode.NodeSendEstablished(key.Sender))
                {
                    tempJfp2PendingIdentityKeys.Add(key);
                }
            }
            foreach (var key in tempJfp2PendingIdentityKeys)
            {
                jfp2PendingIdentity.Remove(key);
            }
            tempJfp2PendingIdentityKeys.Clear();
        }

        /// <summary>
        /// Maximum VariableEntry count per JFP2 VariableSync datagram - kept comfortably under
        /// VariableSyncV1Codec's one-byte (255) entry-count cap. A single object's combined integer+
        /// float+string8 set can in principle approach MAX_INTEGER_VARIABLES+MAX_FLOAT_VARIABLES+
        /// MAX_STRING8_VARIABLES (100+100+80=280), so this chunks the same way the legacy
        /// SendIntegerVariablesMessage/etc. already chunk at their own per-type caps.
        /// </summary>
        const int JFP2_VARIABLE_SYNC_CHUNK_SIZE = 200;

        /// <summary>
        /// Send this object's variable set to `peerNuid` via JFP2 VariableSync if that peer negotiated
        /// it, directly or through a relay (see TryGetJfp2RelayPeer/SendJfp2RelayApplication - Jfp2Bridge
        /// design plan Increment 3); otherwise falls back to the unchanged legacy
        /// SendIntegerVariablesMessage/SendFloatVariablesMessage/SendString8VariablesMessage, unicast to
        /// that one peer (byte-identical content to what a broadcast to that peer would have sent).
        /// Callers replace a single legacy broadcast-to-everyone call with one call to this method per
        /// node in LocalNode.GetNodeList(), matching the split-send pattern Position's own call sites
        /// already use (JoinFS/Sim.cs already loops per-node there) - see Sim.cs's variablesTimer block.
        /// </summary>
        public void SendVariableUpdate(LocalNode.Nuid peerNuid, uint netId, Dictionary<uint, int> integers, Dictionary<uint, float> floats, Dictionary<uint, string> string8s, LocalNode.Nuid ownerNuid)
        {
            bool direct = localNode.TryGetJfp2AppPeer(peerNuid, Jfp2.MessageClasses.VariableSync, out byte version);
            LocalNode.Nuid hubNuid = default;
            if (direct || localNode.TryGetJfp2RelayPeer(peerNuid, Jfp2.MessageClasses.VariableSync, out hubNuid, out version))
            {
                SendJfp2VariableSync(peerNuid, netId, integers, floats, string8s, version, direct, hubNuid);
            }
            else
            {
                SendIntegerVariablesMessage(peerNuid, netId, integers, ownerNuid);
                SendFloatVariablesMessage(peerNuid, netId, floats, ownerNuid);
                SendString8VariablesMessage(peerNuid, netId, string8s, ownerNuid);
            }
        }

        void SendJfp2VariableSync(LocalNode.Nuid peerNuid, uint netId, Dictionary<uint, int> integers, Dictionary<uint, float> floats, Dictionary<uint, string> string8s, byte version, bool direct, LocalNode.Nuid hubNuid)
        {
            int total = integers.Count + floats.Count + string8s.Count;
            if (total == 0)
            {
                return;
            }

            var entries = new List<Jfp2.Codecs.VariableEntry>(total);
            foreach (var kv in integers) entries.Add(new Jfp2.Codecs.VariableEntry { Vuid = kv.Key, Kind = Jfp2.Codecs.VariableKind.Int32, IntValue = kv.Value });
            foreach (var kv in floats) entries.Add(new Jfp2.Codecs.VariableEntry { Vuid = kv.Key, Kind = Jfp2.Codecs.VariableKind.Float32, FloatValue = kv.Value });
            foreach (var kv in string8s) entries.Add(new Jfp2.Codecs.VariableEntry { Vuid = kv.Key, Kind = Jfp2.Codecs.VariableKind.String8, StringValue = kv.Value });

            var codec = Jfp2.Codecs.CodecRegistry.Resolve<Jfp2.Codecs.VariableSyncUpdate>(Jfp2.MessageClasses.VariableSync, version);
            for (int offset = 0; offset < entries.Count; offset += JFP2_VARIABLE_SYNC_CHUNK_SIZE)
            {
                int count = Math.Min(JFP2_VARIABLE_SYNC_CHUNK_SIZE, entries.Count - offset);
                var chunkEntries = entries.GetRange(offset, count);
                var chunk = new Jfp2.Codecs.VariableSyncUpdate { ObjectId = netId, Entries = chunkEntries };
                // Sized exactly per entry rather than count * a fixed worst-case: String8 entries are
                // variable-length now (docs/protocol-v2-implementation-review.md Finding 5), so a flat
                // per-entry bound can no longer guarantee the buffer is big enough.
                int bufferSize = Jfp2.Codecs.VariableSyncV1Codec.HeaderSize;
                foreach (var e in chunkEntries) bufferSize += Jfp2.Codecs.VariableSyncV1Codec.EntrySize(e);
                byte[] buffer = new byte[bufferSize];
                int length = codec.Encode(chunk, buffer);
                if (direct)
                {
                    localNode.SendJfp2Application(peerNuid, Jfp2.MessageClasses.VariableSync, buffer.AsSpan(0, length));
                }
                else
                {
                    localNode.SendJfp2RelayApplication(hubNuid, peerNuid, Jfp2.MessageClasses.VariableSync, buffer.AsSpan(0, length));
                }
            }
        }

        /// <summary>
        /// Apply a received VariableSync onto the matching aircraft, mirroring the legacy IntegerVariables/
        /// FloatVariables/String8Variables receive cases in ReceiveMsg exactly (including the shared-
        /// cockpit netId==uint.MaxValue branch and recording integration) - just regrouped from one
        /// self-describing entry list back into the three typed dictionaries those existing Sim.
        /// UpdateAircraft overloads expect, since nothing downstream needs to change to consume them.
        /// Matches the legacy receive path's existing scope: non-Aircraft Obj variables are not applied
        /// here either (Sim.cs's UpdateAircraft(Nuid, uint, Dictionary&lt;...&gt;) overloads are Aircraft-only,
        /// same gap network-protocol.md/recording-protocol.md already document for the legacy path).
        /// </summary>
        void HandleJfp2VariableSync(LocalNode.Nuid nuid, Jfp2.Codecs.VariableSyncUpdate sync)
        {
            if (main.sim == null || sync.Entries == null || sync.Entries.Count == 0 || !localNode.Connected)
            {
                return;
            }

            Dictionary<uint, int> integers = null;
            Dictionary<uint, float> floats = null;
            Dictionary<uint, string> string8s = null;
            foreach (var entry in sync.Entries)
            {
                switch (entry.Kind)
                {
                    case Jfp2.Codecs.VariableKind.Int32:
                        (integers ??= []).Add(entry.Vuid, entry.IntValue);
                        break;
                    case Jfp2.Codecs.VariableKind.Float32:
                        (floats ??= []).Add(entry.Vuid, entry.FloatValue);
                        break;
                    case Jfp2.Codecs.VariableKind.String8:
                        (string8s ??= []).Add(entry.Vuid, entry.StringValue);
                        break;
                }
            }

            LocalNode.Nuid ownerNuid = nuid;
            uint netId = sync.ObjectId;
            if (sync.ObjectId == uint.MaxValue)
            {
                // shared cockpit - update the user's own aircraft directly, exactly like the legacy
                // receive cases' shared-cockpit branch
                if (main.sim.userAircraft == null)
                {
                    return;
                }
                ownerNuid = main.sim.userAircraft.ownerNuid;
                netId = main.sim.userAircraft.netId;
            }

            ApplyJfp2Variables(ownerNuid, netId, integers, floats, string8s);
        }

        void ApplyJfp2Variables(LocalNode.Nuid ownerNuid, uint netId, Dictionary<uint, int> integers, Dictionary<uint, float> floats, Dictionary<uint, string> string8s)
        {
            if (integers != null)
            {
                Sim.Aircraft aircraft = main.sim.UpdateAircraft(ownerNuid, netId, integers);
                if (aircraft != null && main.recorder.recording && aircraft.record)
                {
                    main.recorder.Record(aircraft.recorderObj, integers);
                }
            }
            if (floats != null)
            {
                Sim.Aircraft aircraft = main.sim.UpdateAircraft(ownerNuid, netId, floats);
                if (aircraft != null && main.recorder.recording && aircraft.record)
                {
                    main.recorder.Record(aircraft.recorderObj, floats);
                }
            }
            if (string8s != null)
            {
                Sim.Aircraft aircraft = main.sim.UpdateAircraft(ownerNuid, netId, string8s);
                if (aircraft != null && main.recorder.recording && aircraft.record)
                {
                    main.recorder.Record(aircraft.recorderObj, string8s);
                }
            }
        }

        #endregion

        #region JFP2 Position (Phase 4)

        // docs/protocol-v2-implementation-plan.md Phase 4. Position is the highest-frequency message
        // in the system (design doc §2/§6.1) and the reason the whole JFP2 envelope is 8 bytes instead
        // of 21. Scoped to AIRCRAFT position only, matching the legacy AircraftPosition message - the
        // generic (non-Aircraft) ObjectPosition message stays entirely on the legacy path this phase;
        // see the implementation plan for the reasoning (it's materially lower frequency and this keeps
        // the phase tractable). Every identity-ish field (livery, ICAO type/airline, registration,
        // class code/WTC, callsign, model, typerole) already moved to Identity in Phase 3 and is
        // deliberately not repeated here.

        /// <summary>
        /// Build this aircraft's current motion state into a version-agnostic PositionUpdate, sourced
        /// exactly the way WriteAircraftPositionMessage already sources the same fields for the legacy
        /// wire (JoinFS/Network.cs) minus everything that moved to Identity.
        /// </summary>
        Jfp2.Codecs.PositionUpdate BuildJfp2PositionUpdate(uint objectId, Sim.Aircraft aircraft, ref Sim.AircraftPosition position, double netTime)
        {
            Jfp2.Codecs.PositionStateFlags flags = Jfp2.Codecs.PositionStateFlags.None;
            if (position.ground != 0) flags |= Jfp2.Codecs.PositionStateFlags.OnGround;
            if (Settings.Default.ElevationCorrection) flags |= Jfp2.Codecs.PositionStateFlags.ElevationCorrection;
            if (aircraft.user) flags |= Jfp2.Codecs.PositionStateFlags.UserControlled;
            if (aircraft.paused) flags |= Jfp2.Codecs.PositionStateFlags.Paused;

            return new Jfp2.Codecs.PositionUpdate
            {
                ObjectId = objectId,
                NetTime = netTime,
                Latitude = position.latitude,
                Longitude = position.longitude,
                Altitude = position.altitude,
                Pitch = position.pitch,
                Bank = position.bank,
                Heading = position.heading,
                VelocityX = position.velocityX,
                VelocityY = position.velocityY,
                VelocityZ = position.velocityZ,
                AngularVelocityX = position.angularVelocityX,
                AngularVelocityY = position.angularVelocityY,
                AngularVelocityZ = position.angularVelocityZ,
                AccelerationX = position.accelerationX,
                AccelerationY = position.accelerationY,
                AccelerationZ = position.accelerationZ,
                Rudder = position.rudder,
                Elevator = position.elevator,
                Aileron = position.aileron,
                BrakeLeft = position.brakeLeft,
                BrakeRight = position.brakeRight,
                Elevation = position.elevation,
                StaticCgToGround = position.staticCgToGround,
                StateFlags = flags,
            };
        }

        /// <summary>
        /// The inverse of BuildJfp2PositionUpdate - rebuilds a Sim.AircraftPosition from a decoded
        /// PositionUpdate for handing to the exact same Sim.UpdateAircraft entry points the legacy
        /// receive path already uses.
        /// </summary>
        static Sim.AircraftPosition ToAircraftPosition(in Jfp2.Codecs.PositionUpdate update)
        {
            return new Sim.AircraftPosition
            {
                latitude = update.Latitude,
                longitude = update.Longitude,
                altitude = update.Altitude,
                pitch = update.Pitch,
                bank = update.Bank,
                heading = update.Heading,
                velocityX = update.VelocityX,
                velocityY = update.VelocityY,
                velocityZ = update.VelocityZ,
                angularVelocityX = update.AngularVelocityX,
                angularVelocityY = update.AngularVelocityY,
                angularVelocityZ = update.AngularVelocityZ,
                accelerationX = update.AccelerationX,
                accelerationY = update.AccelerationY,
                accelerationZ = update.AccelerationZ,
                rudder = update.Rudder,
                elevator = update.Elevator,
                aileron = update.Aileron,
                brakeLeft = update.BrakeLeft,
                brakeRight = update.BrakeRight,
                elevation = update.Elevation,
                radarAltitude = 0.0f,
                ground = (update.StateFlags & Jfp2.Codecs.PositionStateFlags.OnGround) != 0 ? 1 : 0,
                staticCgToGround = update.StaticCgToGround,
            };
        }

        /// <summary>
        /// Send `aircraft`'s position to `peerNuid` via JFP2 Position if negotiated, directly or
        /// through a relay (see TryGetJfp2RelayPeer/SendJfp2RelayApplication - Jfp2Bridge design plan
        /// Increment 2: the hub relays this on, unchanged, to `peerNuid`, so from this node's point of
        /// view sending to an indirect peer looks identical to sending direct). Returns true if the
        /// caller should NOT also send legacy (either because it was actually sent via JFP2, or
        /// because it was deliberately withheld this one tick - see the ordering note below); false
        /// means this peer hasn't negotiated Position, directly or via a relay, and the caller should
        /// use its own already-prepared legacy Write*Message()+Send() path, unchanged.
        /// </summary>
        public bool SendJfp2Position(LocalNode.Nuid peerNuid, Sim.Aircraft aircraft, ref Sim.AircraftPosition position, double netTime, bool sharedCockpit = false)
        {
            bool direct = localNode.TryGetJfp2AppPeer(peerNuid, Jfp2.MessageClasses.Position, out byte version);
            LocalNode.Nuid hubNuid = default;
            if (!direct && !localNode.TryGetJfp2RelayPeer(peerNuid, Jfp2.MessageClasses.Position, out hubNuid, out version))
            {
                return false;
            }

            uint objectId = sharedCockpit ? uint.MaxValue : aircraft.netId;

            if (!sharedCockpit)
            {
                // Guarantee Identity reaches this peer before this object's first-ever Position does
                // (docs/protocol-v2-design.md §7.7) - SendJfp2IdentityIfNeeded is always called
                // separately (Sim.cs's variablesTimer loop) over the same peer list, directly or via
                // relay exactly like this method, so if it hasn't sent Identity to this peer even once
                // yet, withhold Position for this one tick rather than let it arrive first. The object
                // doesn't exist on the receiving end before Identity arrives anyway, so losing one tick
                // of position for a brand-new object is unobservable - and this peer has already
                // committed to JFP2 for Position, so "handled" (no legacy fallback) is still the right
                // return value.
                if (!jfp2IdentitySendState.TryGetValue((aircraft.netId, peerNuid), out Jfp2IdentitySendState identityState) || !identityState.Sent)
                {
                    return true;
                }
            }

            Jfp2.Codecs.PositionUpdate update = BuildJfp2PositionUpdate(objectId, aircraft, ref position, netTime);
            var codec = Jfp2.Codecs.CodecRegistry.Resolve<Jfp2.Codecs.PositionUpdate>(Jfp2.MessageClasses.Position, version);
            Span<byte> buffer = stackalloc byte[Jfp2.Codecs.PositionV1Codec.Size];
            int length = codec.Encode(update, buffer);
            if (direct)
            {
                localNode.SendJfp2Application(peerNuid, Jfp2.MessageClasses.Position, buffer[..length]);
            }
            else
            {
                localNode.SendJfp2RelayApplication(hubNuid, peerNuid, Jfp2.MessageClasses.Position, buffer[..length]);
            }
            return true;
        }

        /// <summary>
        /// Apply a received Position update. Mirrors the legacy AircraftPosition receive case
        /// (Network.ReceiveMsg) exactly: the shared-cockpit netId==uint.MaxValue branch updates the
        /// local user's own aircraft directly; otherwise the update is gated by the same "user OR
        /// MultipleObjects permission" rule, and a brand-new object is created using whatever Identity
        /// was cached for it (see jfp2PendingIdentity / HandleJfp2Identity) - the send-side ordering
        /// guarantee means that cache should always be populated by the time Position for a genuinely
        /// new object arrives; if it isn't (packet loss), the update is dropped and a later Position
        /// tick (after the next Identity heartbeat, at most ~4s) picks it up instead.
        /// </summary>
        void HandleJfp2Position(LocalNode.Nuid nuid, Jfp2.Codecs.PositionUpdate update)
        {
            if (main.sim == null || !localNode.Connected)
            {
                return;
            }

            bool userControlled = (update.StateFlags & Jfp2.Codecs.PositionStateFlags.UserControlled) != 0;
            Sim.AircraftPosition aircraftPosition = ToAircraftPosition(update);

            if (update.ObjectId == uint.MaxValue)
            {
                // shared cockpit - update the user's own aircraft directly, exactly like the legacy
                // AircraftPosition receive case's shared-cockpit branch
                if (main.sim.userAircraft != null && shareFlightControls == nuid)
                {
                    main.sim.UpdateAircraft(main.sim.userAircraft, update.NetTime, aircraftPosition);
                }
                return;
            }

            if (!userControlled && !main.log.MultipleObjects(nuid) && !main.settingsMultiObjects)
            {
                return;
            }

            Sim.Aircraft aircraft = main.sim.objectList.Find(o => o.ownerNuid == nuid && o.netId == update.ObjectId) as Sim.Aircraft;

            if (aircraft == null)
            {
                if (!jfp2PendingIdentity.TryGetValue((update.ObjectId, nuid), out Jfp2.Codecs.IdentityUpdate identity))
                {
                    // Identity hasn't arrived yet for a genuinely new object - drop this Position
                    // update rather than guess at a model/type; see the ordering note above.
                    return;
                }

                string nickname = userControlled ? main.network.GetNodeName(nuid) : "";
                aircraft = main.sim.UpdateAircraft(nuid, update.ObjectId, userControlled, identity.IsPlane, identity.Callsign, identity.Registration,
                    nickname, identity.Model, identity.Livery, identity.IcaoType, identity.IcaoAirline, identity.FlightNumber,
                    identity.ClassCode, identity.Wtc, identity.ClassCodeConfirmed, identity.TypeRole, update.NetTime, ref aircraftPosition);
                jfp2PendingIdentity.Remove((update.ObjectId, nuid));
            }
            else
            {
                aircraft.paused = (update.StateFlags & Jfp2.Codecs.PositionStateFlags.Paused) != 0;
                main.sim.UpdateAircraft(aircraft, update.NetTime, aircraftPosition);
            }

            if (aircraft != null && main.recorder.recording && aircraft.record)
            {
                main.recorder.Record(aircraft.recorderObj, update.NetTime, ref aircraftPosition);
            }
        }

        #endregion

        #region JFP2 Event/FlightPlan/Notes/Weather (Phase 5)

        // docs/protocol-v2-implementation-plan.md Phase 5. See that document for the full scoping
        // notes on FlightPlan's unexplained self-nuid branch (preserved, not understood or removed)
        // and Notes' scope (the live single-note push only - the bulk catch-up dump stays legacy-only).

        /// <summary>
        /// Shared SimEvent handling for both the legacy message and its JFP2 counterpart - identical
        /// to what used to be inline in the legacy ReceiveMsg case block, factored out so JFP2 can
        /// call it too. Note Sim.UpdateAircraft(Nuid, uint, uint, uint, bool) itself re-broadcasts to
        /// this node's OTHER peers when the event lands on a Recorder-owned object being played back
        /// (see SendEventUpdate's own call site in Sim.cs) - that relay logic is unchanged and applies
        /// equally regardless of whether the inbound event arrived via legacy or JFP2.
        /// </summary>
        void HandleSimEvent(LocalNode.Nuid nuid, uint netId, uint eventId, uint data)
        {
            if (!localNode.Connected)
            {
                return;
            }

            if (netId == uint.MaxValue)
            {
                if (main.sim != null && main.sim.userAircraft != null)
                {
                    bool flight = main.log.ShareCockpit(nuid) && nuid == shareFlightControls;
                    main.sim.UpdateAircraft(main.sim.userAircraft.ownerNuid, main.sim.userAircraft.netId, eventId, data, flight);
                }
            }
            else
            {
                Sim.Aircraft aircraft = main.sim?.UpdateAircraft(nuid, netId, eventId, data, true);
                if (aircraft != null && main.recorder.recording && aircraft.record)
                {
                    main.recorder.Record(aircraft.recorderObj, eventId, data);
                }
            }
        }

        /// <summary>
        /// Send a SimEvent to `peerNuid` via JFP2 Event if negotiated, directly or through a relay (see
        /// TryGetJfp2RelayPeer/SendJfp2RelayApplication - Jfp2Bridge design plan Increment 3),
        /// otherwise the unchanged legacy WriteSimEventMessage()+Send()/Broadcast() path. Callers use
        /// this once per node instead of a single legacy Broadcast()/Send() call, matching the split-
        /// send pattern already used for Position/VariableSync/Identity. Returns true if handled via
        /// JFP2 (caller should not also send legacy).
        /// </summary>
        public bool SendEventUpdate(LocalNode.Nuid peerNuid, uint netId, uint eventId, uint data)
        {
            bool direct = localNode.TryGetJfp2AppPeer(peerNuid, Jfp2.MessageClasses.Event, out byte version);
            LocalNode.Nuid hubNuid = default;
            if (!direct && !localNode.TryGetJfp2RelayPeer(peerNuid, Jfp2.MessageClasses.Event, out hubNuid, out version))
            {
                return false;
            }
            var update = new Jfp2.Codecs.EventUpdate { ObjectId = netId, EventId = eventId, Data = data };
            var codec = Jfp2.Codecs.CodecRegistry.Resolve<Jfp2.Codecs.EventUpdate>(Jfp2.MessageClasses.Event, version);
            Span<byte> buffer = stackalloc byte[Jfp2.Codecs.EventV1Codec.Size];
            int length = codec.Encode(update, buffer);
            // guaranteed: true - matches legacy WriteSimEventMessage, which also sends guaranteed
            // (docs/protocol-v2-implementation-review.md Finding 1). A relayed guaranteed send is
            // retried by this node exactly as if peerNuid were direct - see SendJfp2RelayApplication's
            // own remarks on end-to-end acking through the relay.
            if (direct)
            {
                localNode.SendJfp2Application(peerNuid, Jfp2.MessageClasses.Event, buffer[..length], guaranteed: true);
            }
            else
            {
                localNode.SendJfp2RelayApplication(hubNuid, peerNuid, Jfp2.MessageClasses.Event, buffer[..length], guaranteed: true);
            }
            return true;
        }

        /// <summary>
        /// Shared FlightPlan handling for both the legacy message and its JFP2 counterpart.
        /// </summary>
        void HandleFlightPlan(LocalNode.Nuid ownerNuid, uint netId, byte version, Jfp2.Codecs.FlightPlanUpdate flightPlan)
        {
            if (main.sim == null)
            {
                return;
            }

            // Preserved exactly from legacy even though its trigger condition under normal peer-to-
            // peer mesh operation isn't fully understood: both real send call sites
            // (Network.BroadcastFlightPlanUpdate's callers) always pass this node's own Nuid as
            // ownerNuid, and Broadcast() shouldn't ordinarily deliver a node's own broadcast back to
            // itself. Kept as a faithful mechanical port rather than silently dropped - see the
            // implementation plan for this note.
            if (localNode.GetLocalNuid().Equals(ownerNuid))
            {
                main.sim.userFlightPlan.icaoType = flightPlan.IcaoType;
                main.sim.userFlightPlan.departure = flightPlan.Departure.ToUpperInvariant();
                main.sim.userFlightPlan.destination = flightPlan.Destination.ToUpperInvariant();
                main.sim.userFlightPlan.rules = flightPlan.Rules;
                main.sim.userFlightPlan.route = flightPlan.Route;
                main.sim.userFlightPlan.remarks = flightPlan.Remarks;
                main.sim.userFlightPlan.alternate = flightPlan.Alternate;
                main.sim.userFlightPlan.speed = flightPlan.Speed;
                main.sim.userFlightPlan.altitude = flightPlan.Altitude;
                main.sim.userFlightPlan.callsign = flightPlan.Callsign;
                main.sim.userFlightPlan.registration = flightPlan.Registration;
                main.sim.userFlightPlan.icaoAirline = flightPlan.IcaoAirline;
                main.sim.userFlightPlan.flightNumber = flightPlan.FlightNumber;
                main.MonitorEvent("Flight Plan Update");
                if (main.sim.userAircraft != null)
                {
                    main.sim.userAircraft.flightPlanVersion++;
                    if (main.sim.userAircraft.flightPlanVersion == 0) main.sim.userAircraft.flightPlanVersion = 1;
                }
            }
            else if (main.sim.objectList.Find(o => o.ownerNuid == ownerNuid && o.netId == netId) is Sim.Aircraft aircraft)
            {
                aircraft.flightPlanVersion = version;
                aircraft.flightPlan.icaoType = flightPlan.IcaoType;
                aircraft.flightPlan.departure = flightPlan.Departure.ToUpperInvariant();
                aircraft.flightPlan.destination = flightPlan.Destination.ToUpperInvariant();
                aircraft.flightPlan.rules = flightPlan.Rules;
                aircraft.flightPlan.route = flightPlan.Route;
                aircraft.flightPlan.remarks = flightPlan.Remarks;
                aircraft.flightPlan.alternate = flightPlan.Alternate;
                aircraft.flightPlan.speed = flightPlan.Speed;
                aircraft.flightPlan.altitude = flightPlan.Altitude;
                aircraft.flightPlan.callsign = flightPlan.Callsign;
                aircraft.flightPlan.registration = flightPlan.Registration;
                aircraft.flightPlan.icaoAirline = flightPlan.IcaoAirline;
                aircraft.flightPlan.flightNumber = flightPlan.FlightNumber;
            }
        }

        /// <summary>
        /// Send `flightPlan` to every currently connected peer: JFP2 FlightPlan (direct, or through a
        /// relay - see TryGetJfp2RelayPeer/SendJfp2RelayApplication, Jfp2Bridge design plan Increment 3)
        /// for whichever peers negotiated it, the unchanged legacy message for the rest - same split-
        /// send pattern already used for Position/VariableSync/Identity. Replaces a direct
        /// SendFlightPlanMessage(...) call (which always broadcasts to everyone via legacy) at both of
        /// its call sites.
        ///
        /// This closes FlightPlanCodec.cs's own "third-party relay is out of scope until Jfp2Bridge
        /// exists" note without needing an OwnerNuid field on the wire or a schema version bump:
        /// HandleFlightPlan already takes its owner attribution from the `nuid` argument the dispatch
        /// layer supplies (Node.cs's Jfp2ReceiveMsg resolves that to the relayed message's true origin,
        /// not the relaying hub - see EnvelopeFlags.Forwarded's doc comment), so relaying a peer's
        /// FlightPlan through a hub attributes correctly with zero codec change.
        /// </summary>
        public void BroadcastFlightPlanUpdate(uint netId, Sim.FlightPlan flightPlan)
        {
            // prepared once, reused for every peer that ends up on the legacy path below
            WriteFlightPlanMessage(localNode.GetLocalNuid(), netId, flightPlan);

            var update = new Jfp2.Codecs.FlightPlanUpdate
            {
                ObjectId = netId,
                IcaoType = flightPlan.icaoType,
                Departure = flightPlan.departure,
                Destination = flightPlan.destination,
                Rules = flightPlan.rules,
                Route = flightPlan.route,
                Remarks = flightPlan.remarks,
                Alternate = flightPlan.alternate,
                Speed = flightPlan.speed,
                Altitude = flightPlan.altitude,
                Callsign = flightPlan.callsign,
                Registration = flightPlan.registration,
                IcaoAirline = flightPlan.icaoAirline,
                FlightNumber = flightPlan.flightNumber,
            };
            // allocated once outside the loop below (CA2014 - a stackalloc inside a per-peer loop
            // would grow with session size instead of being freed each iteration)
            byte[] buffer = new byte[512];

            foreach (var peerNuid in localNode.GetNodeList())
            {
                bool direct = localNode.TryGetJfp2AppPeer(peerNuid, Jfp2.MessageClasses.FlightPlan, out byte version);
                LocalNode.Nuid hubNuid = default;
                if (direct || localNode.TryGetJfp2RelayPeer(peerNuid, Jfp2.MessageClasses.FlightPlan, out hubNuid, out version))
                {
                    var codec = Jfp2.Codecs.CodecRegistry.Resolve<Jfp2.Codecs.FlightPlanUpdate>(Jfp2.MessageClasses.FlightPlan, version);
                    int length = codec.Encode(update, buffer);
                    if (direct)
                    {
                        localNode.SendJfp2Application(peerNuid, Jfp2.MessageClasses.FlightPlan, buffer.AsSpan(0, length));
                    }
                    else
                    {
                        localNode.SendJfp2RelayApplication(hubNuid, peerNuid, Jfp2.MessageClasses.FlightPlan, buffer.AsSpan(0, length));
                    }
                }
                else
                {
                    localNode.Send(peerNuid);
                }
            }
        }

        /// <summary>
        /// Reply to a WeatherRequest from `nuid` via JFP2 WeatherReply if negotiated, directly or
        /// through a relay (see TryGetJfp2RelayPeer/SendJfp2RelayApplication - Jfp2Bridge design plan
        /// Increment 3 - relevant here since the requester may well be indirect, reached only via
        /// whatever hub relayed its WeatherRequest to us in the first place), otherwise the unchanged
        /// legacy WriteWeatherReplyMessage()+Send() path. WeatherRequest itself is not ported to JFP2
        /// (see the implementation plan - it has no live callers and its NetId field is never read on
        /// receive), so this only upgrades the outgoing reply half.
        /// </summary>
        void SendWeatherReply(LocalNode.Nuid nuid, string metar)
        {
            bool direct = localNode.TryGetJfp2AppPeer(nuid, Jfp2.MessageClasses.WeatherReply, out byte version);
            LocalNode.Nuid hubNuid = default;
            if (direct || localNode.TryGetJfp2RelayPeer(nuid, Jfp2.MessageClasses.WeatherReply, out hubNuid, out version))
            {
                var report = new Jfp2.Codecs.WeatherReport { Metar = metar };
                var codec = Jfp2.Codecs.CodecRegistry.Resolve<Jfp2.Codecs.WeatherReport>(Jfp2.MessageClasses.WeatherReply, version);
                Span<byte> buffer = stackalloc byte[512];
                int length = codec.Encode(report, buffer);
                // guaranteed: true - matches legacy WriteWeatherReplyMessage, which also sends
                // guaranteed (docs/protocol-v2-implementation-review.md Finding 1).
                if (direct)
                {
                    localNode.SendJfp2Application(nuid, Jfp2.MessageClasses.WeatherReply, buffer[..length], guaranteed: true);
                }
                else
                {
                    localNode.SendJfp2RelayApplication(hubNuid, nuid, Jfp2.MessageClasses.WeatherReply, buffer[..length], guaranteed: true);
                }
            }
            else
            {
                WriteWeatherReplyMessage(metar);
                localNode.Send(nuid);
            }
        }

        /// <summary>
        /// Send `metar` to `peerNuid` via JFP2 Weather if negotiated, directly or through a relay (see
        /// TryGetJfp2RelayPeer/SendJfp2RelayApplication - Jfp2Bridge design plan Increment 3), otherwise
        /// the unchanged legacy WriteWeatherUpdateMessage()+Send() path. Callers loop over
        /// LocalNode.GetNodeList() instead of the old single Broadcast() call.
        /// </summary>
        public void SendWeatherUpdate(LocalNode.Nuid peerNuid, string metar)
        {
            bool direct = localNode.TryGetJfp2AppPeer(peerNuid, Jfp2.MessageClasses.Weather, out byte version);
            LocalNode.Nuid hubNuid = default;
            if (direct || localNode.TryGetJfp2RelayPeer(peerNuid, Jfp2.MessageClasses.Weather, out hubNuid, out version))
            {
                var report = new Jfp2.Codecs.WeatherReport { Metar = metar };
                var codec = Jfp2.Codecs.CodecRegistry.Resolve<Jfp2.Codecs.WeatherReport>(Jfp2.MessageClasses.Weather, version);
                Span<byte> buffer = stackalloc byte[512];
                int length = codec.Encode(report, buffer);
                if (direct)
                {
                    localNode.SendJfp2Application(peerNuid, Jfp2.MessageClasses.Weather, buffer[..length]);
                }
                else
                {
                    localNode.SendJfp2RelayApplication(hubNuid, peerNuid, Jfp2.MessageClasses.Weather, buffer[..length]);
                }
            }
            else
            {
                WriteWeatherUpdateMessage(metar);
                localNode.Send(peerNuid);
            }
        }

        #endregion

        /// <summary>
        /// Write message for weather request
        /// </summary>
        public void WriteSimEventMessage(uint netId, uint eventId, uint data)
        {
            // prepare message
            BinaryWriter message = localNode.PrepareMessage(new LocalNode.Nuid(), true);

            // add header
            message.Write(Sim.VERSION);
            // add message ID
            message.Write((short)MESSAGE_ID.SimEvent);
            // add net ID
            message.Write(netId);
            // add event ID
            message.Write(eventId);
            // add data
            message.Write(data);
        }

        /// <summary>
        /// Write message for user list request
        /// </summary>
        public void SendUserListRequestMessage(IPEndPoint endPoint)
        {
            // prepare message
            BinaryWriter message = localNode.PrepareMessage(new LocalNode.Nuid(), false);

            // add header
            message.Write(Sim.VERSION);
            // add message ID
            message.Write((short)MESSAGE_ID.UserListRequest);
            // send message
            localNode.Send(endPoint);
        }

        /// <summary>
        /// Write message for user list
        /// </summary>
        public void SendUserListMessage(IPEndPoint endPoint)
        {
            try
            {
                // for each user
                foreach (HubUser user in localUserList)
                {
                    // prepare message
                    BinaryWriter message = localNode.PrepareMessage(new LocalNode.Nuid(), false);
                    // add header
                    message.Write(Sim.VERSION);
                    // add message ID
                    message.Write((short)MESSAGE_ID.UserList2);
                    message.Write(user.guid.ToByteArray());
                    byte flags = 0;
                    flags |= (byte)(user.atc ? 0x01 : 0);
                    flags |= (byte)(user.ifr ? 0x02 : 0);
                    message.Write(flags);
                    message.Write(user.flightPlan.callsign);
                    message.Write(user.nickname);
                    message.Write(user.frequency);
                    message.Write(user.latitude);
                    message.Write(user.longitude);
                    message.Write(user.altitude);
                    message.Write(user.speed);
                    message.Write(user.squawk);
                    message.Write(user.level);
                    message.Write(user.range);
                    message.Write(user.heading);
                    message.Write(user.flightPlan.icaoType);
                    message.Write(user.flightPlan.departure);
                    message.Write(user.flightPlan.destination);
                    message.Write(user.flightPlan.rules);
                    message.Write(user.flightPlan.route);
                    message.Write(user.flightPlan.remarks);
                    message.Write(user.flightPlan.alternate);
                    message.Write(user.flightPlan.speed);
                    message.Write(user.flightPlan.altitude);
                    message.Write(user.flightPlan.registration);
                    message.Write(user.flightPlan.icaoAirline);
                    message.Write(user.flightPlan.flightNumber);
                    // send hub list
                    localNode.Send(endPoint);
                }
            }
            catch (Exception ex)
            {
                main.MonitorEvent("ERROR: Failed to write UserList message. " + ex.Message);
            }
        }

        /// <summary>
        /// Write message for user positions request
        /// </summary>
        public void SendUserPositionsRequestMessage(IPEndPoint endPoint)
        {
            // prepare message
            BinaryWriter message = localNode.PrepareMessage(new LocalNode.Nuid(), false);

            // add header
            message.Write(Sim.VERSION);
            // add message ID
            message.Write((short)MESSAGE_ID.UserPositionsRequest);
            // send message
            localNode.Send(endPoint);
        }

        /// <summary>
        /// Write message for user positions
        /// </summary>
        public void SendUserPositionsMessage(IPEndPoint endPoint)
        {
            try
            {
                // for each user
                for (int startIndex = 0; startIndex < localUserList.Count; startIndex += MAX_USER_POSITIONS)
                {
                    // prepare message
                    BinaryWriter message = localNode.PrepareMessage(new LocalNode.Nuid(), false);
                    // add header
                    message.Write(Sim.VERSION);
                    // add message ID
                    message.Write((short)MESSAGE_ID.UserPositions);
                    // get group count
                    int count = localUserList.Count - startIndex;
                    // check count
                    if (count > MAX_USER_POSITIONS)
                    {
                        // limit count
                        count = MAX_USER_POSITIONS;
                    }
                    // write user count
                    message.Write((ushort)count);
                    // for each user
                    for (int index = startIndex; index < startIndex + count; index++)
                    {
                        // get user
                        HubUser user = localUserList[index];
                        message.Write(user.guid.ToByteArray());
                        message.Write(user.latitude);
                        message.Write(user.longitude);
                        message.Write(user.altitude);
                        message.Write(user.speed);
                        message.Write(user.squawk);
                        message.Write(user.heading);
                    }
                    // send hub list
                    localNode.Send(endPoint);
                }
            }
            catch (Exception ex)
            {
                main.MonitorEvent("ERROR: Failed to write UserPositions message. " + ex.Message);
            }
        }

        /// <summary>
        /// Write message for remove object
        /// </summary>
        /// <param name="netId">Object Network ID</param>
        public void SendRemoveObjectMessage(uint netId)
        {
            // prepare message
            BinaryWriter message = localNode.PrepareMessage(new LocalNode.Nuid(), true);

            // add header
            message.Write(Sim.VERSION);
            // add message ID
            message.Write((short)MESSAGE_ID.RemoveObject);
            // add state
            message.Write(netId);
            // broadcast
            localNode.Broadcast();
        }

        /// <summary>
        /// Write message for hub list
        /// </summary>
        public void SendHubListMessage(IPEndPoint endPoint)
        {
            // for each hub
            foreach (var hub in hubList)
            {
                // only add non-ignored online hubs
                if (hub.online && main.log.IgnoreNode(hub.endPoint.Address) == false && main.log.IgnoreNode(ref hub.guid) == false)
                {
                    // add to list
                    tempHubList.Add(hub);
                }
            }

            // for each group of hubs
            for (int startIndex = 0; startIndex < tempHubList.Count; startIndex += MAX_HUB_LIST_MESSAGE)
            {
                // prepare message
                BinaryWriter message = localNode.PrepareMessage(new LocalNode.Nuid(), false);
                // add header
                message.Write(Sim.VERSION);
                // add message ID
                message.Write((short)MESSAGE_ID.HubList);
                // get group count
                int count = tempHubList.Count - startIndex;
                // check count
                if (count > MAX_HUB_LIST_MESSAGE)
                {
                    // limit count
                    count = MAX_HUB_LIST_MESSAGE;
                }
                // write hub count
                message.Write((ushort)count);
                // for each hub
                for (int index = startIndex; index < startIndex + count; index++)
                {
                    // add nuid
                    tempHubList[index].nuid.Write(message);
                    message.Write((ushort)tempHubList[index].endPoint.Port);
                }
                // send hub list
                localNode.Send(endPoint);
            }
            // clear temp hub list
            tempHubList.Clear();
        }

        /// <summary>
        /// Send usage log message to home
        /// </summary>
        public void SendUsageLogMessage(IPEndPoint endPoint)
        {
            // prepare message
            BinaryWriter message = localNode.PrepareMessage(new LocalNode.Nuid(), true);

            // add header
            message.Write(Sim.VERSION);
            // add message ID
            message.Write((short)MESSAGE_ID.UsageLog);
            // add version
            message.Write(Main.Version);
            // add guid
            Guid guid = main.guid;
            message.Write(guid.ToByteArray());
            // send message
            localNode.Send(endPoint);
        }

#if DEBUG
        /// <summary>
        /// Send key log message to home
        /// </summary>
        public void SendShutdownMessage(IPEndPoint endPoint, int reason)
        {
            // prepare message
            BinaryWriter message = localNode.PrepareMessage(new LocalNode.Nuid(), true);

            // add header
            message.Write(Sim.VERSION);
            // add message ID
            message.Write((short)MESSAGE_ID.Shutdown);
            // add reason
            message.Write(reason);
            // send message
            localNode.Send(endPoint);
        }
#endif

        /// <summary>
        /// Post a note
        /// </summary>
        /// <param name="note"></param>
        public void SendSessionCommsRequestMessage(LocalNode.Nuid nuid)
        {
            // prepare message
            BinaryWriter message = localNode.PrepareMessage(new LocalNode.Nuid(), true);
            // add header
            message.Write(Sim.VERSION);
            // add message ID
            message.Write((short)MESSAGE_ID.SessionCommsRequest);
            // send message
            localNode.Send(nuid);
        }

        /// <summary>
        /// Post a note
        /// </summary>
        /// <param name="note"></param>
        public void SendSessionCommsMessage(IPEndPoint endPoint)
        {
            // prepare message
            BinaryWriter message = localNode.PrepareMessage(new LocalNode.Nuid(), true);
            // add header
            message.Write(Sim.VERSION);
            // add message ID
            message.Write((short)MESSAGE_ID.Notes);
            // for each user
            foreach (var user in main.notes.userNotesList)
            {
                // not end
                message.Write((byte)0);
                // add guid
                message.Write(user.Key.ToByteArray());
                // add nickname
                message.Write(user.Value.nickname);
                // add callsign
                message.Write(user.Value.callsign);
                // for each comms note
                foreach (var commsNote in user.Value.commsList)
                {
                    // check for session comms
                    if (commsNote.Value.channel != Notes.GLOBAL_CHANNEL)
                    {
                        // not end
                        message.Write((byte)0);
                        // add ID
                        message.Write(commsNote.Key);
                        // add type
                        message.Write((ushort)Notes.Type.Comms);
                        // add expire time
                        message.Write(Notes.COMMS_EXPIRE);
                        // add length
                        message.Write((ushort)(4 + 2 + 1 + commsNote.Value.text.Length));
                        // add age
                        message.Write((float)(main.ElapsedTime - commsNote.Value.time));
                        // add channel
                        message.Write(commsNote.Value.channel);
                        // add text
                        message.Write(commsNote.Value.text);
                    }
                }
                // end
                message.Write((byte)1);
            }
            // end
            message.Write((byte)1);
            // send message
            localNode.Send(endPoint);
        }

        /// <summary>
        /// Post a note
        /// </summary>
        /// <param name="note"></param>
        public void SendGlobalCommsRequestMessage(IPEndPoint endPoint)
        {
            // prepare message
            BinaryWriter message = localNode.PrepareMessage(new LocalNode.Nuid(), true);
            // add header
            message.Write(Sim.VERSION);
            // add message ID
            message.Write((short)MESSAGE_ID.GlobalCommsRequest);
            // send message
            localNode.Send(endPoint);
        }

        /// <summary>
        /// Post a note
        /// </summary>
        /// <param name="note"></param>
        public void SendGlobalCommsMessage(IPEndPoint endPoint)
        {
            // prepare message
            BinaryWriter message = localNode.PrepareMessage(new LocalNode.Nuid(), true);
            // add header
            message.Write(Sim.VERSION);
            // add message ID
            message.Write((short)MESSAGE_ID.Notes);
            // for each user
            foreach (var user in main.notes.userNotesList)
            {
                // not end
                message.Write((byte)0);
                // add guid
                message.Write(user.Key.ToByteArray());
                // add nickname
                message.Write(user.Value.nickname);
                // add callsign
                message.Write(user.Value.callsign);
                // for each comms note
                foreach (var commsNote in user.Value.commsList)
                {
                    // check for session comms
                    if (commsNote.Value.channel == Notes.GLOBAL_CHANNEL)
                    {
                        // not end
                        message.Write((byte)0);
                        // add ID
                        message.Write(commsNote.Key);
                        // add type
                        message.Write((ushort)Notes.Type.Comms);
                        // add expire time
                        message.Write(Notes.COMMS_EXPIRE);
                        // add length
                        message.Write((ushort)(4 + 2 + 1 + commsNote.Value.text.Length));
                        // add age
                        message.Write((float)(main.ElapsedTime - commsNote.Value.time));
                        // add channel
                        message.Write(commsNote.Value.channel);
                        // add text
                        message.Write(commsNote.Value.text);
                    }
                }
                // end
                message.Write((byte)1);
            }
            // end
            message.Write((byte)1);
            // send message
            localNode.Send(endPoint);
        }

        /// <summary>
        /// Comms update request
        /// </summary>
        /// <param name="note"></param>
        public void SendCommsListenRequestMessage(IPEndPoint endPoint)
        {
            // prepare message
            BinaryWriter message = localNode.PrepareMessage(new LocalNode.Nuid(), true);
            // add header
            message.Write(Sim.VERSION);
            // add message ID
            message.Write((short)MESSAGE_ID.CommsListenRequest);
            // send message
            localNode.Send(endPoint);
        }

        /// <summary>
        /// Post a note
        /// </summary>
        /// <param name="note"></param>
        public void SendCommsNoteMessage(Guid guid, string nickname, string callsign, uint noteId, float age, ushort channel, string text)
        {
            // prepare message
            BinaryWriter message = localNode.PrepareMessage(new LocalNode.Nuid(), true);

            // add header
            message.Write(Sim.VERSION);
            // add message ID
            message.Write((short)MESSAGE_ID.Notes);
            // not end
            message.Write((byte)0);
            // add guid
            message.Write(guid.ToByteArray());
            // add nickname
            message.Write(nickname);
            // add callsign
            message.Write(callsign);
            // not end
            message.Write((byte)0);
            // add ID
            message.Write(noteId);
            // add type
            message.Write((ushort)Notes.Type.Comms);
            // add expire time
            message.Write(Notes.COMMS_EXPIRE);
            // add length
            message.Write((ushort)(4 + 2 + 1 + text.Length));
            // add age
            message.Write(age);
            // add channel
            message.Write(channel);
            // add text
            message.Write(text);
            // end
            message.Write((byte)1);
            // end
            message.Write((byte)1);

            // one send per connected peer instead of one legacy Broadcast() call, so each peer can
            // independently get JFP2 Notes (if negotiated) or the unchanged legacy message - wire-
            // identical to the old broadcast for any peer that ends up on the legacy path
            // (docs/protocol-v2-implementation-plan.md Phase 5; same split-send pattern already used
            // elsewhere in this file)
            var note = new Jfp2.Codecs.NoteUpdate { Guid = guid, Nickname = nickname, Callsign = callsign, NoteId = noteId, Age = age, Channel = channel, Text = text };
            // allocated once outside the loop below (CA2014 - a stackalloc inside a per-peer loop
            // would grow with session size instead of being freed each iteration)
            byte[] noteBuffer = new byte[1024];

            foreach (var peerNuid in localNode.GetNodeList())
            {
                bool direct = localNode.TryGetJfp2AppPeer(peerNuid, Jfp2.MessageClasses.Notes, out byte version);
                LocalNode.Nuid hubNuid = default;
                if (direct || localNode.TryGetJfp2RelayPeer(peerNuid, Jfp2.MessageClasses.Notes, out hubNuid, out version))
                {
                    var codec = Jfp2.Codecs.CodecRegistry.Resolve<Jfp2.Codecs.NoteUpdate>(Jfp2.MessageClasses.Notes, version);
                    int length = codec.Encode(note, noteBuffer);
                    // guaranteed: true - matches legacy SendCommsNoteMessage, which also sends
                    // guaranteed (docs/protocol-v2-implementation-review.md Finding 1). See
                    // TryGetJfp2RelayPeer/SendJfp2RelayApplication - Jfp2Bridge design plan Increment 3.
                    if (direct)
                    {
                        localNode.SendJfp2Application(peerNuid, Jfp2.MessageClasses.Notes, noteBuffer.AsSpan(0, length), guaranteed: true);
                    }
                    else
                    {
                        localNode.SendJfp2RelayApplication(hubNuid, peerNuid, Jfp2.MessageClasses.Notes, noteBuffer.AsSpan(0, length), guaranteed: true);
                    }
                }
                else
                {
                    localNode.Send(peerNuid);
                }
            }
        }

        /// <summary>
        /// Post a note
        /// </summary>
        /// <param name="note"></param>
        public void SendNotesMessage(IPEndPoint endPoint)
        {
            // prepare message
            BinaryWriter message = localNode.PrepareMessage(new LocalNode.Nuid(), true);
            // add header
            message.Write(Sim.VERSION);
            // add message ID
            message.Write((short)MESSAGE_ID.Notes);
            // for each user
            foreach (var user in main.notes.userNotesList)
            {
                // not end
                message.Write((byte)0);
                // add guid
                message.Write(user.Key.ToByteArray());
                // add nickname
                message.Write(user.Value.nickname);
                // add callsign
                message.Write(user.Value.callsign);
                // for each comms note
                foreach (var commsNote in user.Value.commsList)
                {
                    // not end
                    message.Write((byte)0);
                    // add ID
                    message.Write(commsNote.Key);
                    // add type
                    message.Write((ushort)Notes.Type.Comms);
                    // add expire time
                    message.Write(Notes.COMMS_EXPIRE);
                    // add length
                    message.Write((ushort)(2 + commsNote.Value.text.Length + 2));
                    // add age
                    message.Write((float)(main.ElapsedTime - commsNote.Value.time));
                    // add channel
                    message.Write(commsNote.Value.channel);
                    // add text
                    message.Write(commsNote.Value.text);
                }
                // end
                message.Write((byte)1);
            }
            // end
            message.Write((byte)1);
            // send message
            localNode.Send(endPoint);
        }

        /// <summary>
        /// Post a note
        /// </summary>
        /// <param name="note"></param>
        public void SendNotesMessage(ushort type, IPEndPoint endPoint)
        {
            // prepare message
            BinaryWriter message = localNode.PrepareMessage(new LocalNode.Nuid(), true);
            // add header
            message.Write(Sim.VERSION);
            // add message ID
            message.Write((short)MESSAGE_ID.Notes);
            // check type
            switch (type)
            {
                case (ushort)Notes.Type.Comms:
                    // for each user
                    foreach (var user in main.notes.userNotesList)
                    {
                        // not end
                        message.Write((byte)0);
                        // add guid
                        message.Write(user.Key.ToByteArray());
                        // add nickname
                        message.Write(user.Value.nickname);
                        // add callsign
                        message.Write(user.Value.callsign);
                        // for each comms note
                        foreach (var commsNote in user.Value.commsList)
                        {
                            // not end
                            message.Write((byte)0);
                            // add ID
                            message.Write(commsNote.Key);
                            // add type
                            message.Write((ushort)Notes.Type.Comms);
                            // add expire time
                            message.Write(Notes.COMMS_EXPIRE);
                            // add length
                            message.Write((ushort)(2 + commsNote.Value.text.Length + 2));
                            // add age
                            message.Write((float)(main.ElapsedTime - commsNote.Value.time));
                            // add channel
                            message.Write(commsNote.Value.channel);
                            // add text
                            message.Write(commsNote.Value.text);
                        }
                        // end
                        message.Write((byte)1);
                    }
                    // end
                    message.Write((byte)1);
                    break;
            }
            // send message
            localNode.Send(endPoint);
        }

        /// <summary>
        /// Send online message to all hubs
        /// </summary>
        public void SendOnlineMessage()
        {
            // prepare message
            BinaryWriter message = localNode.PrepareMessage(new LocalNode.Nuid(), false);

            // add header
            message.Write(Sim.VERSION);
            // add message ID
            message.Write((short)MESSAGE_ID.Online);
            // add uuid
            message.Write(main.uuid);
            // for all hubs
            foreach (var hub in hubList)
            {
                // check if hub is online
                if (hub.online)
                {
                    // send message
                    localNode.Send(hub.endPoint);
                }
            }
        }

        /// <summary>
        /// Prepare (but do not send) a legacy FlightPlan message into the shared send buffer -
        /// factored out of SendFlightPlanMessage so a caller that needs to fan out per-peer (see
        /// Network.BroadcastFlightPlanUpdate, docs/protocol-v2-implementation-plan.md Phase 5) can
        /// prepare once and reuse the buffer, the same pattern WriteAircraftPositionMessage/
        /// Broadcast() already established for Position.
        /// </summary>
        void WriteFlightPlanMessage(LocalNode.Nuid ownerNuid, uint netId, Sim.FlightPlan flightPlan)
        {
            // if nothing more authoritative (SimBrief, live sim/config data) already supplied a real ICAO
            // airline, try to derive one from the callsign's shape - commercial airline callsigns are an ICAO
            // airline designator + flight number (e.g. "DLH1234"); General Aviation tail-number callsigns
            // ("N12345", "D-EJOE") won't match and are left alone. Mutates the shared FlightPlan object in
            // place, so this also fixes up the icaoAirline seen by the more frequent position-update messages
            // and the WebSocket telemetry feed, not just this message.
            if (flightPlan.icaoAirline.Length == 0)
            {
                flightPlan.icaoAirline = Sim.DeriveIcaoAirlineFromCallsign(flightPlan.callsign);
            }

            // prepare message
            BinaryWriter message = localNode.PrepareMessage(new LocalNode.Nuid(), false);
            // add header
            message.Write(Sim.VERSION);
            // add message ID
            message.Write((short)MESSAGE_ID.FlightPlan);
            // add nuid
            ownerNuid.Write(message);
            // add net id
            message.Write(netId);
            // add version
            message.Write((byte)1);
            // add flight plan
            message.Write(flightPlan.icaoType);
            message.Write(flightPlan.departure);
            message.Write(flightPlan.destination);
            message.Write(flightPlan.rules);
            message.Write(flightPlan.route);
            message.Write(flightPlan.remarks);
            message.Write(flightPlan.alternate);
            message.Write(flightPlan.speed);
            message.Write(flightPlan.altitude);
            message.Write(flightPlan.callsign);
            message.Write(flightPlan.registration);
            message.Write(flightPlan.icaoAirline);
            message.Write(flightPlan.flightNumber);
        }

        /// <summary>
        /// Send online message to all hubs
        /// </summary>
        public void SendFlightPlanMessage(LocalNode.Nuid ownerNuid, uint netId, Sim.FlightPlan flightPlan)
        {
            WriteFlightPlanMessage(ownerNuid, netId, flightPlan);
            // send message
            localNode.Broadcast();
        }

        /// <summary>
        /// Send online message to all hubs
        /// </summary>
        public void SendShowOnRadarMessage(LocalNode.Nuid ownerNuid, uint netId, bool show)
        {
            // prepare message
            BinaryWriter message = localNode.PrepareMessage(new LocalNode.Nuid(), true);
            // add header
            message.Write(Sim.VERSION);
            // add message ID
            message.Write((short)MESSAGE_ID.ShowOnRadar);
            // add nuid
            ownerNuid.Write(message);
            // add net id
            message.Write(netId);
            // add flag
            message.Write(show);
            // send message
            localNode.Broadcast();
        }

        /// <summary>
        /// Internal messages
        /// </summary>
        enum MESSAGE_ID
        {
            ObjectPosition,
            AircraftPosition,
            PlaneState,
            HelicopterState,
            AircraftState,
            PistonEngineState,
            TurbineEngineState,
            AircraftFuel,
            AircraftPayload,
            ObjectSmoke,
            WeatherRequest,
            WeatherReply,
            WeatherUpdate,
            SharedData,
            StatusRequest,
            Status,
            HubList,
            RemoveObject,
            UserListRequest,
            UserList,
            UsageLog,
            SimEvent,
            KeyLog,
            Shutdown,
            SessionCommsRequest,
            GlobalCommsRequest,
            CommsListenRequest,
            AllNotesRequest,
            Notes,
            UserNuidRequest,
            UserNuid,
            Online,
            FlightPlanRequest,
            FlightPlan,
            UserList2,
            UserPositionsRequest,
            UserPositions,
            IntegerVariables,
            FloatVariables,
            String8Variables,
            ShowOnRadar
        }

        void ConnectComplete()
        {
            // message
            main.MonitorEvent("Joined session");
            // reset shared controls
            shareFlightControls = new LocalNode.Nuid();
            shareAncillaryControls = new LocalNode.Nuid();
            shareNavControls = new LocalNode.Nuid();
        }

        void NodeJoin(LocalNode.Nuid nuid, IPEndPoint endPoint)
        {
            // add node
            nodeList[nuid] = new Node();
            // message
            main.MonitorEvent("Added node '" + nuid + "' @ '" + EncodeIP(endPoint.ToString()) + "'");
        }

        void NodeRoute(LocalNode.Nuid nuid, LocalNode.Nuid routeNuid)
        {
            if (nuid == routeNuid)
            {
                // message
                main.MonitorEvent("Routing direct to '" + nuid + "'");
            }
            else
            {
                // message
                main.MonitorEvent("Routing '" + nuid + "' via '" + routeNuid + "'");
            }
        }

        void NodeEstablished(LocalNode.Nuid nuid)
        {
            // message
            main.MonitorEvent("Connected '" + nuid + "'");
            // create message
            SendSharedDataMessage(nuid);
            // check for comms requests
            if (commsRequests > 0)
            {
#if !CONSOLE
                // check if comms is open
                if (main.sessionForm != null && main.sessionForm.Visible)
                {
                    // request comms notes
                    SendSessionCommsRequestMessage(nuid);
                }
#endif
                // update count
                commsRequests--;
            }
        }

        void NodeLeave(LocalNode.Nuid nuid)
        {
            // remove node
            nodeList.Remove(nuid);
            // remove all aircraft owned by the node
            main.sim ?. RemoveObject(nuid);
            // message
            main.MonitorEvent("Removed node '" + nuid + "'");
        }

        /// <summary>
        /// Receive an incoming message
        /// </summary>
        /// <param name="nuid">Sender nuid</param>
        /// <param name="reader">Message reader</param>
        void ReceiveMsg(IPEndPoint endPoint, LocalNode.Nuid nuid, BinaryReader reader)
        {
            try
            {
                // read data version
                short dataVersion = reader.ReadInt16();
                // check version
                if (dataVersion >= 10014)
                {
                    switch ((MESSAGE_ID)reader.ReadInt16())
                    {
                        case MESSAGE_ID.ObjectPosition:
                            try
                            {
                                // update stat
                                Stats.ObjectPosition.Record(reader.BaseStream.Length);
                                // check if connected and multiple objects allowed
                                if (localNode.Connected && (main.log.MultipleObjects(nuid) || main.settingsMultiObjects))
                                {
                                    // get network ID
                                    uint netId = reader.ReadUInt32();

                                    // read model
                                    string model = reader.ReadString();
                                    int typerole = reader.ReadByte();
                                    // read flags
                                    byte flags = reader.ReadByte();

                                    // read network time
                                    double netTime = reader.ReadDouble();
                                    Sim.ObjectPositionVelocity positionVelocity = new();
                                    Sim.Read(dataVersion, reader, ref positionVelocity);

                                    // update position and velocity
                                    string variation = (reader.PeekChar() != -1) ? reader.ReadString() : "";
                                    string icaoType = (reader.PeekChar() != -1) ? reader.ReadString() : "";
                                    string icaoAirline = (reader.PeekChar() != -1) ? reader.ReadString() : "";
                                    string classCode = (reader.PeekChar() != -1) ? reader.ReadString() : "";
                                    string wtc = (reader.PeekChar() != -1) ? reader.ReadString() : "";
                                    bool classCodeConfirmed = (reader.PeekChar() != -1) ? reader.ReadBoolean() : false;
                                    Sim.Obj simObject = main.sim?.UpdateObject(nuid, netId, model, variation, icaoType, icaoAirline, classCode, wtc, classCodeConfirmed, typerole, netTime, ref positionVelocity);

                                    // check for object
                                    if (simObject != null)
                                    {
                                        // update pause state
                                        simObject.paused = (flags & 0x01) != 0;
                                        // check if aircraft is being recorded
                                        if (main.recorder.recording && simObject.record)
                                        {
                                            // record position and velocity
                                            main.recorder.Record(simObject.recorderObj, netTime, ref positionVelocity);
                                        }
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                main.MonitorEvent("ERROR: Failed to read ObjectPosition message. " + ex.Message);
                            }
                            break;

                        case MESSAGE_ID.AircraftPosition:
                            try
                            {
                                // update stat
                                Stats.AircraftPosition.Record(reader.BaseStream.Length);
                                // check if connected
                                if (localNode.Connected)
                                {
                                    // get network ID
                                    uint netId = reader.ReadUInt32();

                                    // read user flag
                                    bool user = reader.ReadBoolean();

                                    // check if allowed
                                    if (user || main.log.MultipleObjects(nuid) || main.settingsMultiObjects)
                                    {
                                        // read plane flag
                                        bool plane = reader.ReadBoolean();
                                        // read callsign
                                        string callsign = reader.ReadString();
                                        // read model
                                        string model = reader.ReadString();
                                        int typerole = reader.ReadByte();
                                        // read flags
                                        byte flags = reader.ReadByte();

                                        // read network time
                                        double netTime = reader.ReadDouble();
                                        Sim.AircraftPosition aircraftPosition = new();
                                        Sim.Read(dataVersion, reader, ref aircraftPosition);

                                        // check for shared cockpit update
                                        if (netId == uint.MaxValue)
                                        {
                                            // check for user aircraft and shared flight controls
                                            if (main.sim != null && main.sim.userAircraft != null && shareFlightControls == nuid)
                                            {
                                                // update position and velocity
                                                main.sim.UpdateAircraft(main.sim.userAircraft, netTime, aircraftPosition);
                                            }
                                        }
                                        else
                                        {
                                            // get nickname
                                            string nickname = user ? main.network.GetNodeName(nuid) : "";
                                            // update position and velocity
                                            string variation = (reader.PeekChar() != -1) ? reader.ReadString() : "";
                                            string icaoType = (reader.PeekChar() != -1) ? reader.ReadString() : "";
                                            string icaoAirline = (reader.PeekChar() != -1) ? reader.ReadString() : "";
                                            string registration = (reader.PeekChar() != -1) ? reader.ReadString() : "";
                                            string flightNumber = (reader.PeekChar() != -1) ? reader.ReadString() : "";
                                            string classCode = (reader.PeekChar() != -1) ? reader.ReadString() : "";
                                            string wtc = (reader.PeekChar() != -1) ? reader.ReadString() : "";
                                            bool classCodeConfirmed = (reader.PeekChar() != -1) ? reader.ReadBoolean() : false;
                                            aircraftPosition.staticCgToGround = (reader.PeekChar() != -1) ? reader.ReadSingle() : float.NaN;
                                            Sim.Aircraft aircraft = main.sim?.UpdateAircraft(nuid, netId, user, plane, callsign, registration, nickname, model, variation, icaoType, icaoAirline, flightNumber, classCode, wtc, classCodeConfirmed, typerole, netTime, ref aircraftPosition);
                                            // check for aircraft
                                            if (aircraft != null)
                                            {
                                                // update pause state
                                                aircraft.paused = (flags & 0x01) != 0;
                                                // check if aircraft is being recorded
                                                if (main.recorder.recording && aircraft.record)
                                                {
                                                    // record position and velocity
                                                    main.recorder.Record(aircraft.recorderObj, netTime, ref aircraftPosition);
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                main.MonitorEvent("ERROR: Failed to read AircraftPosition message. " + ex.Message);
                            }
                            break;

                        case MESSAGE_ID.SimEvent:
                            try
                            {
                                // update stat
                                Stats.SimEvent.Record(reader.BaseStream.Length);
                                // check if connected
                                if (localNode.Connected)
                                {
                                    // get network ID
                                    uint netId = reader.ReadUInt32();
                                    // read event ID
                                    uint eventId = reader.ReadUInt32();
                                    // read event data
                                    uint data = reader.ReadUInt32();

                                    HandleSimEvent(nuid, netId, eventId, data);
                                }
                            }
                            catch (Exception ex)
                            {
                                main.MonitorEvent("ERROR: Failed to read SimEvent message. " + ex.Message);
                            }
                            break;

                        case MESSAGE_ID.WeatherRequest:
                            try
                            {
                                // update stat
                                Stats.WeatherRequest.Record(reader.BaseStream.Length);
                                // check if connected
                                if (localNode.Connected)
                                {
                                    try
                                    {
                                        if (main.sim != null && main.sim.scheduleMetar != null)
                                        {
                                            // reply via JFP2 if this requester negotiated it, else the
                                            // unchanged legacy message (docs/protocol-v2-
                                            // implementation-plan.md Phase 5 - only WeatherRequest's
                                            // reply is JFP2-aware; the request itself isn't ported,
                                            // see SendWeatherReply's own remarks)
                                            SendWeatherReply(nuid, main.sim.scheduleMetar);
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        main.MonitorEvent("Failed to write weather request message: " + ex.Message);
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                main.MonitorEvent("ERROR: Failed to read WeatherRequest message. " + ex.Message);
                            }
                            break;

                        case MESSAGE_ID.WeatherReply:
                            try
                            {
                                // update stat
                                Stats.WeatherReply.Record(reader.BaseStream.Length);
                                // check if connected
                                if (localNode.Connected)
                                {
                                    // get METAR data
                                    string metar = reader.ReadString();
                                    // check for METAR
                                    if (metar.Length > 0)
                                    {
                                        // set weather
                                        main.sim ?. SetWeatherObservation(metar);
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                main.MonitorEvent("ERROR: Failed to read WeatherReply message. " + ex.Message);
                            }
                            break;

                        case MESSAGE_ID.WeatherUpdate:
                            try
                            {
                                // update stat
                                Stats.WeatherUpdate.Record(reader.BaseStream.Length);
                                // check if connected
                                if (localNode.Connected)
                                {
                                    // get METAR data
                                    string metar = reader.ReadString();
                                    // check for METAR
                                    if (metar.Length > 0)
                                    {
                                        // set weather for aircraft
                                        main.sim ?. SetWeatherObservation(nuid, metar);
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                main.MonitorEvent("ERROR: Failed to read WeatherUpdate message. " + ex.Message);
                            }
                            break;

                        case MESSAGE_ID.SharedData:
                            try
                            {
                                // update stat
                                Stats.SharedData.Record(reader.BaseStream.Length);
                                // check if connected
                                if (localNode.Connected)
                                {
                                    // get share cockpit
                                    byte share = reader.ReadByte();
                                    main.sim ?. ShareCockpit(nuid, share);
                                    // check for node
                                    if (nodeList.TryGetValue(nuid, out Node node))
                                    {
                                        // set data version
                                        node.dataVersion = (ushort)dataVersion;
                                        // read nickname
                                        string nickname = reader.ReadString();
                                        // check if nickname has changed
                                        if (nickname.Equals(node.nickname) == false)
                                        {
                                            // update nickname
                                            node.nickname = nickname;
                                            // update ATC ID
                                            main.sim ?. SetAtcId(nuid);
                                        }
                                        // set Guid
                                        node.guid = new Guid(reader.ReadBytes(16));
                                        // read flags
                                        byte flags = reader.ReadByte();
                                        // read hub mode
                                        bool hub = (flags & 0x01) != 0;
                                        // check if hub has changed
                                        if (node.hub == false && hub)
                                        {
                                            // submit hub
                                            SubmitHub(endPoint);
                                        }
                                        // set hub
                                        node.hub = hub;
                                        // read hub mode
                                        bool atc = (flags & 0x02) != 0;
                                        // check if atc has changed
                                        if (node.atc && atc == false)
                                        {
                                            // remove ATC
                                            main.euroscope.RemoveAtc(node.atcAirport, node.atcLevel, node.atcFrequency);
                                        }
                                        // read ATC airport
                                        node.atcAirport = reader.ReadString();
                                        // read ATC level
                                        node.atcLevel = reader.ReadByte();
                                        // read frequency
                                        node.atcFrequency = reader.ReadInt16();
                                        // check if atc has changed
                                        if (node.atc == false && atc)
                                        {
                                            // add ATC
                                            main.euroscope.AddAtc(node.atcAirport, node.atcLevel, node.atcFrequency);
                                        }
                                        // set atc
                                        node.atc = atc;
                                        // set activity circle
                                        node.activityCircle = reader.ReadByte();
                                        // set version
                                        node.version = (dataVersion >= 10019) ? reader.ReadString() : "";
                                        // set simulator
                                        node.simulator = (dataVersion >= 10019) ? reader.ReadString() : "";
                                        // set simulator connected flag
                                        node.simulatorConnected = (dataVersion < 10024 || (flags & 0x04) != 0);
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                main.MonitorEvent("ERROR: Failed to read SharedDate message. " + ex.Message);
                            }
                            break;

                        case MESSAGE_ID.StatusRequest:
                            //mainForm.MonitorNetwork("StatusRequest");
                            try
                            {
                                // update stat
                                Stats.StatusRequest.Record(reader.BaseStream.Length);
                                // check if connected
                                if (localNode.Connected)
                                {
                                    // read fields into the same version-agnostic value the JFP2 codec
                                    // decodes into - see HandleStatusRequest, shared by both protocols
                                    Jfp2.Codecs.StatusRequestUpdate request = new()
                                    {
                                        HubEnabled = reader.ReadByte() != 0,
                                        HubListRequested = reader.ReadByte() != 0,
                                    };
                                    // uuid is the last field on the wire; swallow a short/missing read
                                    // exactly as the pre-refactor code did (nothing reads after it)
                                    try { request.Uuid = reader.ReadUInt32(); } catch { }

                                    HandleStatusRequest(endPoint, nuid, request);
                                }
                            }
                            catch (Exception ex)
                            {
                                main.MonitorEvent("ERROR: Failed to read StatusRequest message. " + ex.Message);
                            }
                            break;

                        case MESSAGE_ID.Status:
                            try
                            {
                                // update stat
                                Stats.Status.Record(reader.BaseStream.Length);

                                // read fields into the same version-agnostic value the JFP2 codec
                                // decodes into - see HandleStatus, shared by both protocols
                                Jfp2.Codecs.StatusUpdate status = new()
                                {
                                    Guid = new Guid(reader.ReadBytes(16)),
                                    AppVersion = reader.ReadString(),
                                    Users = reader.ReadUInt16(),
                                };
                                status.AtcCount = reader.ReadUInt16();
                                status.AtcAirport = (status.AtcCount > 0) ? reader.ReadString() : "";
                                status.AtcLevel = (status.AtcCount > 0) ? reader.ReadByte() : 2;
                                status.Planes = reader.ReadUInt16();
                                status.Helicopters = reader.ReadUInt16();
                                status.Boats = reader.ReadUInt16();
                                status.Vehicles = reader.ReadUInt16();
                                status.HubEnabled = reader.ReadBoolean();

                                if (status.HubEnabled)
                                {
                                    status.Address = reader.ReadString().TrimStart(' ').TrimEnd(' ');
                                    status.Name = reader.ReadString().TrimStart(' ').TrimEnd(' ');
                                    status.About = reader.ReadString().TrimStart(' ').TrimEnd(' ');
                                    status.Voip = reader.ReadString().TrimStart(' ').TrimEnd(' ');
                                    status.NextEvent = reader.ReadString().TrimStart(' ').TrimEnd(' ');
                                    status.Airport = reader.ReadString().TrimStart(' ').TrimEnd(' ');
                                    status.ActivityCircle = reader.ReadInt32();
                                    // read flags
                                    byte flags = (dataVersion >= 10025) ? reader.ReadByte() : (byte)0;
                                    status.GlobalSession = (flags & 0x02) != 0;
                                    status.PasswordRequired = (flags & 0x04) != 0;

                                    // check for unspecified address
                                    if (status.Address.Length <= 0)
                                    {
                                        // use actual end point
                                        status.Address = endPoint.ToString();
                                    }
                                }

                                HandleStatus(endPoint, nuid, status, (ushort)dataVersion);
                            }
                            catch (Exception ex)
                            {
                                main.MonitorEvent("ERROR: Failed to read Status message. " + ex.Message);
                            }
                            break;

                        case MESSAGE_ID.HubList:
                            //mainForm.MonitorNetwork("HubList");
                            try
                            {
                                // update stat
                                Stats.HubList.Record(reader.BaseStream.Length);
                                // get hub count
                                int count = reader.ReadUInt16();
                                // for each hub
                                for (int i = 0; i < count; i++)
                                {
                                    // read nuid
                                    LocalNode.Nuid hubNuid = new(reader);
                                    ushort hubPort = reader.ReadUInt16();
                                    // check if hub is already in list
                                    Hub hub = hubList.Find(h => h.nuid == hubNuid);
                                    // check if hub not found
                                    if (hub == null)
                                    {
                                        // submit new hub
                                        SubmitHub(localNode.MakeEndPoint(hubNuid, hubPort));
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                main.MonitorEvent("ERROR: Failed to read HubList message. " + ex.Message);
                            }
                            break;

                        case MESSAGE_ID.RemoveObject:
                            try
                            {
                                // update stat
                                Stats.RemoveObject.Record(reader.BaseStream.Length);
                                // remove object
                                main.sim ?. RemoveObject(nuid, reader.ReadUInt32());
                            }
                            catch (Exception ex)
                            {
                                main.MonitorEvent("ERROR: Failed to read RemoveObject message. " + ex.Message);
                            }
                            break;

                        case MESSAGE_ID.UserListRequest:
                            //mainForm.MonitorNetwork("UserListRequest");
                            try
                            {
                                // update stat
                                Stats.UserListRequest.Record(reader.BaseStream.Length);
                                // check if connected and this is a hub
                                if (localNode.Connected && main.settingsHub)
                                {
                                    // write reply message
                                    SendUserListMessage(endPoint);
                                }
                            }
                            catch (Exception ex)
                            {
                                main.MonitorEvent("ERROR: Failed to read UserListRequest message. " + ex.Message);
                            }
                            break;

                        case MESSAGE_ID.UserList:
                            try
                            {
                                // update stat
                                Stats.UserList.Record(reader.BaseStream.Length);
                                // get hub
                                Hub hub = hubList.Find(h => h.endPoint.Address.Equals(endPoint.Address));
                                // check if hub found
                                if (hub != null)
                                {
                                    // read count
                                    ushort count = reader.ReadUInt16();
                                    // for each user
                                    for (int index = 0; index < count; index++)
                                    {
                                        // read guid
                                        Guid guid = new(reader.ReadBytes(16));
                                        // read user details
                                        byte flags = reader.ReadByte();
                                        bool atc = (flags & 0x01) != 0;
                                        bool ifr = (flags & 0x02) != 0;
                                        string callsign = reader.ReadString();
                                        string nickname = reader.ReadString();
                                        ushort frequency = reader.ReadUInt16();
                                        float latitude = reader.ReadSingle();
                                        float longitude = reader.ReadSingle();
                                        ushort altitude = reader.ReadUInt16();
                                        ushort speed = reader.ReadUInt16();
                                        string icaoType = reader.ReadString();
                                        string from = reader.ReadString();
                                        string to = reader.ReadString();
                                        ushort squawk = reader.ReadUInt16();
                                        byte level = reader.ReadByte();
                                        byte range = reader.ReadByte();
                                        ushort heading = reader.ReadUInt16();

                                        // reject this user
                                        if (guid.Equals(main.guid) == false)
                                        {
                                            // find existing user
                                            HubUser user = hub.userList.Find(u => u.guid.Equals(guid));
                                            // check if new user
                                            if (user == null)
                                            {
                                                // create new user
                                                user = new HubUser(guid);
                                                hub.userList.Add(user);
                                            }
                                            // update expire time
                                            user.expireTime = main.ElapsedTime + OFFLINE_TIME;
                                            // update user
                                            user.atc = atc;
                                            user.ifr = ifr;
                                            user.flightPlan.callsign = callsign;
                                            user.nickname = nickname;
                                            user.frequency = frequency;
                                            user.latitude = latitude;
                                            user.longitude = longitude;
                                            user.altitude = altitude;
                                            user.speed = speed;
                                            user.flightPlan.icaoType = icaoType;
                                            user.flightPlan.departure = from.ToUpperInvariant();
                                            user.flightPlan.destination = to.ToUpperInvariant();
                                            user.squawk = squawk;
                                            user.level = level;
                                            user.range = range;
                                            user.heading = heading;
                                        }
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                main.MonitorEvent("ERROR: Failed to read UserList message. " + ex.Message);
                            }
                            break;

                        case MESSAGE_ID.UserList2:
                            try
                            {
                                // update stat
                                Stats.UserList2.Record(reader.BaseStream.Length);
                                // get hub
                                Hub hub = hubList.Find(h => h.endPoint.Address.Equals(endPoint.Address));
                                // check if hub found
                                if (hub != null)
                                {
                                    // read guid
                                    Guid guid = new(reader.ReadBytes(16));
                                    // reject this user
                                    if (guid.Equals(main.guid) == false)
                                    {
                                        // find existing user
                                        HubUser user = hub.userList.Find(u => u.guid.Equals(guid));
                                        // check if new user
                                        if (user == null)
                                        {
                                            // create new user
                                            user = new HubUser(guid);
                                            hub.userList.Add(user);
                                        }
                                        // update expire time
                                        user.expireTime = main.ElapsedTime + OFFLINE_TIME;
                                        byte flags = reader.ReadByte();
                                        // update user
                                        user.atc = (flags & 0x01) != 0;
                                        user.ifr = (flags & 0x02) != 0;
                                        user.flightPlan.callsign = reader.ReadString();
                                        user.nickname = reader.ReadString();
                                        user.frequency = reader.ReadUInt16();
                                        user.latitude = reader.ReadSingle();
                                        user.longitude = reader.ReadSingle();
                                        user.altitude = reader.ReadUInt16();
                                        user.speed = reader.ReadUInt16();
                                        user.squawk = reader.ReadUInt16();
                                        user.level = reader.ReadByte();
                                        user.range = reader.ReadByte();
                                        user.heading = reader.ReadUInt16();
                                        user.flightPlan.icaoType = reader.ReadString();
                                        user.flightPlan.departure = reader.ReadString().ToUpperInvariant();
                                        user.flightPlan.destination = reader.ReadString().ToUpperInvariant();
                                        user.flightPlan.rules = reader.ReadString();
                                        user.flightPlan.route = reader.ReadString();
                                        user.flightPlan.remarks = reader.ReadString();
                                        user.flightPlan.alternate = dataVersion >= 21003 ? reader.ReadString() : "";
                                        user.flightPlan.speed = dataVersion >= 21003 ? reader.ReadString() : "";
                                        user.flightPlan.altitude = dataVersion >= 21003 ? reader.ReadString() : "";
                                        user.flightPlan.registration = dataVersion >= 21006 ? reader.ReadString() : "";
                                        user.flightPlan.icaoAirline = dataVersion >= 21006 ? reader.ReadString() : "";
                                        user.flightPlan.flightNumber = dataVersion >= 21006 ? reader.ReadString() : "";
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                main.MonitorEvent("ERROR: Failed to read UserList message. " + ex.Message);
                            }
                            break;

                        case MESSAGE_ID.UserPositionsRequest:
                            try
                            {
                                // update stat
                                Stats.UserPositionRequest.Record(reader.BaseStream.Length);
                                // check if connected and this is a hub
                                if (localNode.Connected && main.settingsHub)
                                {
                                    // write reply message
                                    SendUserPositionsMessage(endPoint);
                                }
                            }
                            catch (Exception ex)
                            {
                                main.MonitorEvent("ERROR: Failed to read UserPositionsRequest message. " + ex.Message);
                            }
                            break;

                        case MESSAGE_ID.UserPositions:
                            try
                            {
                                // update stat
                                Stats.UserPositions.Record(reader.BaseStream.Length);
                                // get hub
                                Hub hub = hubList.Find(h => h.endPoint.Address.Equals(endPoint.Address));
                                // check if hub found
                                if (hub != null)
                                {
                                    // read count
                                    ushort count = reader.ReadUInt16();
                                    // for each user
                                    for (int index = 0; index < count; index++)
                                    {
                                        // read guid
                                        Guid guid = new(reader.ReadBytes(16));
                                        // read user details
                                        float latitude = reader.ReadSingle();
                                        float longitude = reader.ReadSingle();
                                        ushort altitude = reader.ReadUInt16();
                                        ushort speed = reader.ReadUInt16();
                                        ushort squawk = reader.ReadUInt16();
                                        ushort heading = reader.ReadUInt16();

                                        // reject this user
                                        if (guid.Equals(main.guid) == false)
                                        {
                                            // find existing user
                                            HubUser user = hub.userList.Find(u => u.guid.Equals(guid));
                                            // check user exists
                                            if (user != null)
                                            {
                                                // update expire time
                                                user.expireTime = main.ElapsedTime + OFFLINE_TIME;
                                                // update user
                                                user.latitude = latitude;
                                                user.longitude = longitude;
                                                user.altitude = altitude;
                                                user.speed = speed;
                                                user.squawk = squawk;
                                                user.heading = heading;
                                            }
                                        }
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                main.MonitorEvent("ERROR: Failed to read UserPositions message. " + ex.Message);
                            }
                            break;

                        case MESSAGE_ID.SessionCommsRequest:
                            {
                                // update stat
                                Stats.SessionCommsRequest.Record(reader.BaseStream.Length);
                                // send notes
                                SendSessionCommsMessage(endPoint);
                            }
                            break;

                        case MESSAGE_ID.Notes:
                            try
                            {
                                // update stat
                                Stats.Notes.Record(reader.BaseStream.Length);
                                // read end flag
                                byte endUser = reader.ReadByte();
                                // while there is another user
                                while (endUser == 0)
                                {
                                    // read guid
                                    Guid guid = new(reader.ReadBytes(16));
                                    // read nickname
                                    string nickname = reader.ReadString();
                                    // read callsign
                                    string callsign = reader.ReadString();
                                    // read end flag
                                    byte endNote = reader.ReadByte();
                                    // while there is another note
                                    while (endNote == 0)
                                    {
                                        // read note ID
                                        uint noteId = reader.ReadUInt32();
                                        // read note type
                                        ushort type = reader.ReadUInt16();
                                        // read expire time
                                        ushort expire = reader.ReadUInt16();
                                        // read length
                                        ushort length = reader.ReadUInt16();
                                        // check type
                                        switch (type)
                                        {
                                            case (ushort)Notes.Type.Comms:
                                                {
                                                    // read age
                                                    float age = reader.ReadSingle();
                                                    // read channel
                                                    ushort channel = reader.ReadUInt16();
                                                    // read text
                                                    string text = reader.ReadString();
                                                    // store note
                                                    main.notes.ProcessCommsNote(ref guid, nickname, callsign, noteId, age, channel, text);
                                                }
                                                break;
                                            default:
                                                {
                                                    // read content
                                                    reader.ReadBytes(length);
                                                }
                                                break;
                                        }
                                        // read end flag
                                        endNote = reader.ReadByte();
                                    }
                                    // read end flag
                                    endUser = reader.ReadByte();
                                }

#if !CONSOLE
                                // check for comms window
                                if (main.sessionForm != null && main.mainForm != null && main.sessionForm.Visible)
                                {
                                    // refresh window
                                    main.mainForm.refreshComms = true;
                                }
#endif
                            }
                            catch (Exception ex)
                            {
                                main.MonitorEvent("ERROR: Failed to read Notes message. " + ex.Message);
                            }
                            break;

                        case MESSAGE_ID.UserNuidRequest:
                            try
                            {
                                // update stat
                                Stats.UserNuidRequest.Record(reader.BaseStream.Length);
                                // read uuid
                                uint uuid = reader.ReadUInt32();

                                // check if online user is known
                                if (onlineUsers.TryGetValue(uuid, out OnlineUser value))
                                {
                                    // prepare message
                                    BinaryWriter message = localNode.PrepareMessage(new LocalNode.Nuid(), true);

                                    // add header
                                    message.Write(Sim.VERSION);
                                    // add message ID
                                    message.Write((short)MESSAGE_ID.UserNuid);
                                    // add uuid
                                    message.Write(uuid);
                                    value.nuid.Write(message);
                                    // add port
                                    message.Write(value.port);
                                    // send message
                                    localNode.Send(endPoint);
                                    main.MonitorNetwork("UserNuidRequest '" + endPoint.ToString() + "' - '" + UuidToString(uuid) + "' - '" + value.nuid.ToString() + ":" + value.port + "'");
                                }
                                else
                                {
                                    main.MonitorNetwork("UserNuidRequest '" + endPoint.ToString() + "' - '" + UuidToString(uuid) + "' NOT ONLINE");
                                }
                            }
                            catch (Exception ex)
                            {
                                main.MonitorEvent("ERROR: Failed to read RequestEndPoint message. " + ex.Message);
                            }
                            break;

                        case MESSAGE_ID.UserNuid:
                            try
                            {
                                // update stat
                                Stats.UserNuid.Record(reader.BaseStream.Length);
                                // read uuid
                                uint uuid = reader.ReadUInt32();
                                // read end point
                                LocalNode.Nuid userNuid = new(reader);
                                // read port
                                ushort port = reader.ReadUInt16();

                                // register
                                RegisterOnlineUser(uuid, userNuid, port);
                                // monitor
                                main.MonitorNetwork("UserNuid '" + endPoint.ToString() + "' - '" + UuidToString(uuid) + "' - '" + userNuid.ToString() + ":" + port + "'");
                            }
                            catch (Exception ex)
                            {
                                main.MonitorEvent("ERROR: Failed to read UserNuid message. " + ex.Message);
                            }
                            break;

                        case MESSAGE_ID.Online:
                            try
                            {
                                // update stat
                                Stats.Online.Record(reader.BaseStream.Length);
                                // read uuid
                                uint uuid = reader.ReadUInt32();
                                // register
                                RegisterOnlineUser(uuid, nuid, (ushort)endPoint.Port);
                            }
                            catch (Exception ex)
                            {
                                main.MonitorEvent("ERROR: Failed to read Online message. " + ex.Message);
                            }
                            break;

                        case MESSAGE_ID.FlightPlan:
                            try
                            {
                                // update stat
                                Stats.FlightPlan.Record(reader.BaseStream.Length);
                                // get owner nuid
                                LocalNode.Nuid ownerNuid = new(reader);
                                // get network ID
                                uint netId = reader.ReadUInt32();
                                // read version
                                byte flightPlanWireVersion = reader.ReadByte();
                                // read flight plan into the same version-agnostic value the JFP2 side
                                // decodes into - see HandleFlightPlan, shared by both protocols
                                Jfp2.Codecs.FlightPlanUpdate flightPlanUpdate = new()
                                {
                                    ObjectId = netId,
                                    IcaoType = reader.ReadString(),
                                    Departure = reader.ReadString(),
                                    Destination = reader.ReadString(),
                                    Rules = reader.ReadString(),
                                    Route = reader.ReadString(),
                                    Remarks = reader.ReadString(),
                                    Alternate = dataVersion >= 21003 ? reader.ReadString() : "",
                                    Speed = dataVersion >= 21003 ? reader.ReadString() : "",
                                    Altitude = dataVersion >= 21003 ? reader.ReadString() : "",
                                    Callsign = dataVersion >= 21003 ? reader.ReadString() : "",
                                    Registration = dataVersion >= 21006 ? reader.ReadString() : "",
                                    IcaoAirline = dataVersion >= 21006 ? reader.ReadString() : "",
                                    FlightNumber = dataVersion >= 21006 ? reader.ReadString() : "",
                                };

                                HandleFlightPlan(ownerNuid, netId, flightPlanWireVersion, flightPlanUpdate);
                            }
                            catch (Exception ex)
                            {
                                main.MonitorEvent("ERROR: Failed to read FlightPlan message. " + ex.Message);
                            }
                            break;

                        case MESSAGE_ID.IntegerVariables:
                            try
                            {
                                // update stat
                                Stats.IntegerVariables.Record(reader.BaseStream.Length);
                                // check if connected
                                if (localNode.Connected)
                                {
                                    // get owner nuid
                                    LocalNode.Nuid ownerNuid = new(reader);
                                    // get network ID
                                    uint netId = reader.ReadUInt32();
                                    // read variables
                                    Dictionary<uint, int> variables = [];
                                    Sim.Read(dataVersion, reader, variables);

                                    // check for shared cockpit update
                                    if (netId == uint.MaxValue)
                                    {
                                        // check for user aircraft
                                        if (main.sim != null && main.sim.userAircraft != null)
                                        {
                                            // get flight state
                                            bool flight = main.log.ShareCockpit(nuid) && nuid == shareFlightControls;
                                            // update variables
                                            main.sim ?. UpdateAircraft(main.sim.userAircraft.ownerNuid, main.sim.userAircraft.netId, variables);
                                        }
                                    }
                                    else
                                    {
                                        // update aircraft in sim
                                        Sim.Aircraft aircraft = main.sim ?. UpdateAircraft(ownerNuid, netId, variables);

                                        // check for aircraft
                                        if (aircraft != null)
                                        {
                                            // check if aircraft is being recorded
                                            if (main.recorder.recording && aircraft.record)
                                            {
                                                // record position and velocity
                                                main.recorder.Record(aircraft.recorderObj, variables);
                                            }
                                        }
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                main.MonitorEvent("ERROR: Failed to read IntegerVariables message. " + ex.Message);
                            }
                            break;

                        case MESSAGE_ID.FloatVariables:
                            try
                            {
                                // update stat
                                Stats.FloatVariables.Record(reader.BaseStream.Length);
                                // check if connected
                                if (localNode.Connected)
                                {
                                    // get owner nuid
                                    LocalNode.Nuid ownerNuid = new(reader);
                                    // get network ID
                                    uint netId = reader.ReadUInt32();
                                    // read variables
                                    Dictionary<uint, float> variables = [];
                                    Sim.Read(dataVersion, reader, variables);

                                    // check for shared cockpit update
                                    if (netId == uint.MaxValue)
                                    {
                                        // check for user aircraft
                                        if (main.sim != null && main.sim.userAircraft != null)
                                        {
                                            // get flight state
                                            bool flight = main.log.ShareCockpit(nuid) && nuid == shareFlightControls;
                                            // update variables
                                            main.sim ?. UpdateAircraft(main.sim.userAircraft.ownerNuid, main.sim.userAircraft.netId, variables);
                                        }
                                    }
                                    else
                                    {
                                        // update aircraft in sim
                                        Sim.Aircraft aircraft = main.sim ?. UpdateAircraft(ownerNuid, netId, variables);

                                        // check for aircraft
                                        if (aircraft != null)
                                        {
                                            // check if aircraft is being recorded
                                            if (main.recorder.recording && aircraft.record)
                                            {
                                                // record position and velocity
                                                main.recorder.Record(aircraft.recorderObj, variables);
                                            }
                                        }
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                main.MonitorEvent("ERROR: Failed to read FloatVariables message. " + ex.Message);
                            }
                            break;

                        case MESSAGE_ID.String8Variables:
                            try
                            {
                                // update stat
                                Stats.IntegerVariables.Record(reader.BaseStream.Length);
                                // check if connected
                                if (localNode.Connected)
                                {
                                    // get owner nuid
                                    LocalNode.Nuid ownerNuid = new(reader);
                                    // get network ID
                                    uint netId = reader.ReadUInt32();
                                    // read variables
                                    Dictionary<uint, string> variables = [];
                                    Sim.Read(dataVersion, reader, variables);

                                    // check for shared cockpit update
                                    if (netId == uint.MaxValue)
                                    {
                                        // check for user aircraft
                                        if (main.sim != null && main.sim.userAircraft != null)
                                        {
                                            // get flight state
                                            bool flight = main.log.ShareCockpit(nuid) && nuid == shareFlightControls;
                                            // update variables
                                            main.sim ?. UpdateAircraft(main.sim.userAircraft.ownerNuid, main.sim.userAircraft.netId, variables);
                                        }
                                    }
                                    else
                                    {
                                        // update aircraft in sim
                                        Sim.Aircraft aircraft = main.sim ?. UpdateAircraft(ownerNuid, netId, variables);

                                        // check for aircraft
                                        if (aircraft != null)
                                        {
                                            // check if aircraft is being recorded
                                            if (main.recorder.recording && aircraft.record)
                                            {
                                                // record position and velocity
                                                main.recorder.Record(aircraft.recorderObj, variables);
                                            }
                                        }
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                main.MonitorEvent("ERROR: Failed to read String8Variables message. " + ex.Message);
                            }
                            break;
                    }
                }
            }
            catch (Sim.ReadException ex)
            {
                // message
                main.MonitorEvent(ex.Message);
            }
            catch (Exception ex)
            {
                // message
                main.MonitorEvent(ex.Message);
            }
        }

#region DNS

        /// <summary>
        /// List of DNS lookups
        /// </summary>
        readonly Dictionary<string, IPAddress> dnsLookups = [];

        /// <summary>
        /// Time to reset DNS lookups
        /// </summary>
        DateTime dnsResetTime;

        /// <summary>
        /// Lookup a DNS entry
        /// </summary>
        /// <param name="address">Text address</param>
        /// <returns>IP address</returns>
        public bool DnsLookup(string addressText, out IPAddress address)
        {
            // check for existing lookup
            if (dnsLookups.TryGetValue(addressText, out IPAddress value))
            {
                // get address
                address = value;
                // return result
                return !address.Equals(IPAddress.None);
            }
            else
            {
                try
                {
                    // try DNS lookup
                    IPAddress[] list = Dns.GetHostAddresses(addressText);
                    // check for result
                    if (list.Length <= 0)
                    {
                        // add to lookups
                        dnsLookups[addressText] = IPAddress.None;
                        // failed
                        address = IPAddress.None;
                        return false;
                    }
                    else
                    {
                        // add to lookups
                        dnsLookups[addressText] = list[0];
                        // success
                        address = list[0];
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    main.MonitorEvent(ex.Message);
                    // add to lookups
                    dnsLookups[addressText] = IPAddress.None;
                    address = IPAddress.None;
                    return false;
                }
            }
        }

#endregion

#region Nodes

        /// <summary>
        /// Node
        /// </summary>
        public class Node
        {
            /// <summary>
            /// Data version
            /// </summary>
            public ushort dataVersion = 0;
            /// <summary>
            /// Node nickname
            /// </summary>
            public string nickname = "";
            /// <summary>
            /// Guid of this node
            /// </summary>
            public Guid guid = Guid.Empty;
            /// <summary>
            /// Is this node a hub
            /// </summary>
            public bool hub = false;
            /// <summary>
            /// Is this node in ATC mode
            /// </summary>
            public bool atc = false;
            /// <summary>
            /// ATC airport
            /// </summary>
            public string atcAirport = "";
            /// <summary>
            /// ATC level
            /// </summary>
            public int atcLevel = 2;
            /// <summary>
            /// ATC frequency
            /// </summary>
            public int atcFrequency = 22800;
            /// <summary>
            /// Activity circle of the node
            /// </summary>
            public byte activityCircle = 40;
            /// <summary>
            /// JoinFS version
            /// </summary>
            public string version = "";
            /// <summary>
            /// Simulator name
            /// </summary>
            public string simulator = "";
            /// <summary>
            /// Is simulator connected
            /// </summary>
            public bool simulatorConnected = false;
        }

        /// <summary>
        /// List of nodes
        /// </summary>
        public Dictionary<LocalNode.Nuid, Node> nodeList = [];

        /// <summary>
        /// Get the node nickname
        /// </summary>
        /// <param name="nuid">ID of node</param>
        public string GetNodeName(LocalNode.Nuid nuid)
        {
            // check for this node
            if (nuid.Invalid())
            {
                // get nickname
                return main.settingsNickname;
            }

            // check for valid node
            if (nodeList.TryGetValue(nuid, out Node value))
            {
                // return nickname
                return value.nickname;
            }

            // use IP address
            if (localNode.GetNodeEndPoint(nuid, out IPEndPoint endPoint))
            {
                // return address as string
                return endPoint.ToString();
            }

            // invalid
            return "";
        }

        /// <summary>
        /// Get the node guid
        /// </summary>
        /// <param name="nuid">ID of node</param>
        public Guid GetNodeGuid(LocalNode.Nuid nuid)
        {
            // check for this node
            if (nuid.Invalid())
            {
                // get this guid
                return main.guid;
            }

            // check for valid node
            if (nodeList.TryGetValue(nuid, out Node value))
            {
                // return guid
                return value.guid;
            }

            // invalid
            return Guid.Empty;
        }

        public string GetNodeCallsign(LocalNode.Nuid nuid)
        {
            // check for ATC
            if (GetNodeAtc(nuid, out string airport, out int level))
            {
                // use ATC callsign
                return Sim.MakeAtcCallsign(airport, level);
            }
            else if (main.sim != null)
            {
                // check for valid aircraft
                if (main.sim.objectList.Find(o => o.ownerNuid == nuid && o is Sim.Aircraft && (o as Sim.Aircraft).user) is Sim.Aircraft aircraft)
                {
                    // get callsign
                    return aircraft.flightPlan.callsign;
                }
            }

            return "";
        }

        /// <summary>
        /// Get the node activity circle
        /// </summary>
        /// <param name="nuid">ID of node</param>
        public int GetNodeActivityCircle(LocalNode.Nuid nuid)
        {
            // check for this node
            if (nuid.Invalid())
            {
                // get this activity circle
                return main.settingsActivityCircle;
            }

            // check for valid node
            if (nodeList.TryGetValue(nuid, out Node value))
            {
                // return activity circle
                return value.activityCircle;
            }

            // invalid
            return 40;
        }

        /// <summary>
        /// Get the main ATC for the session
        /// </summary>
        /// <param name="airport">Airport</param>
        /// <returns>Number of ATC</returns>
        public int GetMainAtc(out string airport, out int level)
        {
            // initialize
            int atcCount = 0;
            airport = "";
            level = 2;

            // add this ATC
            if (main.settingsAtc && main.settingsAtcAirport.Length > 0)
            {
                // check for first ATC
                if (atcCount == 0)
                {
                    // return airport
                    airport = main.settingsAtcAirport;
                    level = Settings.Default.AtcLevel;
                }
                // increase count
                atcCount++;
            }

            // for each node
            foreach (var node in nodeList.Values)
            {
                // check for an airport
                if (node.atc && node.atcAirport.Length > 0)
                {
                    // check for first ATC
                    if (atcCount == 0)
                    {
                        // return airport
                        airport = node.atcAirport.ToUpper();
                        level = node.atcLevel;
                    }
                    // increase count
                    atcCount++;
                }
            }

            // return count
            return atcCount;
        }

        /// <summary>
        /// Check if a node is doing ATC
        /// </summary>
        /// <param name="nuid"></param>
        /// <returns></returns>
        public bool GetNodeAtc(LocalNode.Nuid nuid, out string airport, out int level)
        {
            // initialize
            airport = "";
            level = 0;

            // add this ATC
            if (nuid.Invalid())
            {
                // get ATC airport
                airport = main.settingsAtcAirport;
                // check if ATC enabled
                if (main.settingsAtc && airport.Length > 0)
                {
                    // get level
                    level = Settings.Default.AtcLevel;
                    // is ATC
                    return true;
                }
                else
                {
                    // is not ATC
                    return false;
                }
            }

            // check for valid node
            if (nodeList.TryGetValue(nuid, out Node value))
            {
                // get airport and level
                airport = value.atcAirport;
                level = value.atcLevel;
                // return ATC flag
                return value.atc;
            }

            // invalid
            return false;
        }

        /// <summary>
        /// Get the node version
        /// </summary>
        /// <param name="nuid">ID of node</param>
        public string GetNodeVersion(LocalNode.Nuid nuid)
        {
            // check for this node
            if (nuid.Invalid())
            {
                // get this version
                return Main.Version;
            }

            // check for valid node
            if (nodeList.TryGetValue(nuid, out Node value))
            {
                // return version
                return value.version;
            }

            // invalid
            return "";
        }

        /// <summary>
        /// Get the node simulator
        /// </summary>
        /// <param name="nuid">ID of node</param>
        public string GetNodeSimulator(LocalNode.Nuid nuid)
        {
            // check for this node
            if (main.sim != null && nuid.Invalid())
            {
                // get this simulator
                return main.sim.GetSimulatorName();
            }

            // check for valid node
            if (nodeList.ContainsKey(nuid))
            {
                // return simulator
                return nodeList[nuid].simulator == "" ? Resources.Strings.NotConnected : nodeList[nuid].simulator;
            }

            // invalid
            return Resources.Strings.NotConnected;
        }

        /// <summary>
        /// Get the node simulator connected flag
        /// </summary>
        /// <param name="nuid">ID of node</param>
        public bool GetNodeSimulatorConnected(LocalNode.Nuid nuid)
        {
            // check for this node
            if (main.sim != null && nuid.Invalid())
            {
                // get this simulator flag
                return main.sim.Connected;
            }

            // check for valid node
            if (nodeList.TryGetValue(nuid, out Node value))
            {
                // return simulator
                return value.simulatorConnected;
            }

            // invalid
            return false;
        }

        public string GetLocalCallsign()
        {
            // check for ATC
            if (GetNodeAtc(new LocalNode.Nuid(), out string airport, out int level))
            {
                // use ATC callsign
                return Sim.MakeAtcCallsign(airport, level);
            }
            else
            {
                // get callsign
                return main.sim != null ? main.sim.userFlightPlan.callsign : "";
            }
        }

        /// <summary>
        /// Share controls with other nodes
        /// </summary>
        public LocalNode.Nuid shareFlightControls = new();
        public LocalNode.Nuid shareAncillaryControls = new();
        public LocalNode.Nuid shareNavControls = new();

#endregion

#region Hubs

        /// <summary>
        /// Hub user
        /// </summary>
        public class HubUser
        {
            public double expireTime;
            public Guid guid;
            public bool atc;
            public string nickname;
            public ushort frequency;
            public float latitude;
            public float longitude;
            public ushort altitude;
            public ushort speed;
            public Sim.FlightPlan flightPlan;
            public ushort squawk;
            public byte level;
            public byte range;
            public bool ifr;
            public ushort heading;

            /// <summary>
            /// constructor
            /// </summary>
            public HubUser(Guid guid)
            {
                this.guid = guid;
                flightPlan = new Sim.FlightPlan();
            }

            /// <summary>
            /// constructor
            /// </summary>
            public HubUser(Guid guid, bool atc, string nickname, int frequency, double latitude, double longitude, double altitude, Sim.Vel velocity, Sim.FlightPlan flightPlan, int squawk, int level, int range, bool ifr, double heading)
            {
                this.guid = guid;
                this.atc = atc;
                this.nickname = nickname;
                this.frequency = (ushort)frequency;
                this.latitude = (float)latitude;
                this.longitude = (float)longitude;
                this.altitude = (ushort)Math.Max(0.0, Math.Min(20000.0, altitude));
                speed = 0;
                // check for valid velocity
                if (velocity != null)
                {
                    // speed
                    speed = (ushort)(Math.Sqrt(velocity.linear.x * velocity.linear.x + velocity.linear.z * velocity.linear.z) * 1.9438444925);
                }
                this.flightPlan = flightPlan;
                this.squawk = (ushort)squawk;
                this.level = (byte)level;
                this.range = (byte)range;
                this.ifr = ifr;
                this.heading = (ushort)(heading * (180.0 / Math.PI));
            }
        }

        /// <summary>
        /// Local hub users
        /// </summary>
        public readonly List<HubUser> localUserList = [];

        /// <summary>
        /// Remote Hub
        /// </summary>
        public class Hub
        {
            public LocalNode.Nuid nuid = new();
            public IPEndPoint endPoint = new(0, 0);
            public string addressText = "";
            public ushort port = DEFAULT_PORT;
            public DateTime dateTime;
            public Guid guid = Guid.Empty;
            public string appVersion = "0.0.0";
            public ushort dataVersion = 0;
            public string name = "";
            public string about = "";
            public string voip = "";
            public string nextEvent = "";
            public string airport = "";
            public int activityCircle = 40;
            public bool online = false;
            public double offlineTime = 0.0;
            public ushort users = 0;
            public ushort atcCount = 0;
            public string atcAirport = "";
            public int atcLevel = 2;
            public ushort planes = 0;
            public ushort helicopters = 0;
            public ushort boats = 0;
            public ushort vehicles = 0;
            public bool globalSession = false;
            public bool password = false;

            /// <summary>
            /// Global hub users
            /// </summary>
            public List<HubUser> userList = [];
        }

        /// <summary>
        /// List of Hubs
        /// </summary>
        public List<Hub> hubList = [];

        /// <summary>
        /// Temporary list of Hubs
        /// </summary>
        public List<Hub> tempHubList = [];

        /// <summary>
        /// Temporary list of online users
        /// </summary>
        public List<uint> tempOnlineUsers = [];

        /// <summary>
        /// Temporary list of end points
        /// </summary>
        public List<IPEndPoint> tempEndPoints = [];

        /// <summary>
        /// List of Hubs waiting to be verified
        /// </summary>
        readonly Dictionary<IPEndPoint, float> pendingHubList = [];

        /// <summary>
        /// Get the total number of hub users
        /// </summary>
        public int HubUserCount
        {
            get
            {
                // count
                int count = 0;
                // for each hub
                foreach (var hub in hubList)
                {
                    // accumulate count
                    count += hub.userList.Count;
                }
                // return result
                return count;
            }
        }

        /// <summary>
        /// Count the number if hubs at an IP address
        /// </summary>
        /// <param name="nuid">Hub nuid</param>
        /// <returns>Number of hubs</returns>
        int HubCount_IP(LocalNode.Nuid nuid)
        {
            // hub count
            int count = 0;
            // for each hub
            foreach (var hub in hubList)
            {
                // check IP address
                if (hub.nuid.ip == nuid.ip)
                {
                    // increment
                    count++;
                }
            }
            // return hub count
            return count;
        }

#endregion

#region Online Users

        /// <summary>
        /// Make a user ID from a GUID
        /// </summary>
        /// <param name="guid"></param>
        /// <returns>uuid</returns>
        static public uint MakeUuid(Guid guid)
        {
            // hash the guid
            uint uuid = LocalNode.HashString(guid.ToString());
            // avoid the null value
            if (uuid == 0) uuid = 1;
            // return
            return uuid;
        }

        /// <summary>
        /// Make a user ID from a string
        /// </summary>
        /// <param name="str"></param>
        /// <returns>uuid</returns>
        static public uint MakeUuid(string str)
        {
            // check for seperator
            string[] parts = str.Split(' ');
            if (parts.Length == 2 && parts[0].Length == 5 && parts[1].Length == 5)
            {
                // get components
                if (uint.TryParse(parts[0], NumberStyles.Number, CultureInfo.InvariantCulture, out uint n1)
                    && uint.TryParse(parts[1], NumberStyles.Number, CultureInfo.InvariantCulture, out uint n2))
                {
                    // return uuid
                    return (n1 << 16) + (n2 & 0xffff);
                }
            }

            // failed
            return 0;
        }

        /// <summary>
        /// Convert address from text to an end point
        /// </summary>
        /// <param name="addressText">Address string</param>
        /// <param name="endPoint">End point</param>
        /// <returns>Success</returns>
        IPEndPoint MakeEndPoint(OnlineUser user)
        {
            // make endpoint
            return localNode.MakeEndPoint(user.nuid, user.port);
        }

        /// <summary>
        /// Convert uuid to a string
        /// </summary>
        /// <returns></returns>
        static public string UuidToString(uint uuid)
        {
            return (uuid >> 16).ToString("D5", CultureInfo.InvariantCulture) + " " + (uuid & 0xffff).ToString("D5", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Check if a string is in uuid format
        /// </summary>
        /// <param name="str"></param>
        /// <returns></returns>
        static public bool IsUuidFormat(string str)
        {
            // check for seperator
            string[] parts = str.Split(' ');
            if (parts.Length == 2 && parts[0].Length == 5 && parts[1].Length == 5)
            {
                // check for ints
                if (uint.TryParse(parts[0], NumberStyles.Number, CultureInfo.InvariantCulture, out uint _)
                    && uint.TryParse(parts[1], NumberStyles.Number, CultureInfo.InvariantCulture, out uint _))
                {
                    // uuid format
                    return true;
                }
            }

            // not uuid format
            return false;
        }

        /// <summary>
        /// Is a user online
        /// </summary>
        /// <returns></returns>
        public bool IsUserOnline(uint uuid)
        {
            return onlineUsers.ContainsKey(uuid);
        }

        /// <summary>
        /// An online user
        /// </summary>
        class OnlineUser
        {
            /// <summary>
            /// Nuid of the user
            /// </summary>
            public LocalNode.Nuid nuid;
            /// <summary>
            /// Port used
            /// </summary>
            public ushort port;

            /// <summary>
            /// Time to remove this user
            /// </summary>
            public double expireTime;
        }

        /// <summary>
        /// list of online users
        /// </summary>
        readonly Dictionary<uint, OnlineUser> onlineUsers = [];

        /// <summary>
        /// Information
        /// </summary>
        public int OnlineUserCount { get { return onlineUsers.Count; } }

        /// <summary>
        /// Register an online user
        /// </summary>
        public void RegisterOnlineUser(uint uuid, LocalNode.Nuid nuid, ushort port)
        {
            OnlineUser user;
            // check for existing user
            if (onlineUsers.TryGetValue(uuid, out OnlineUser value))
            {
                // get user
                user = value;
            }
            else
            {
                // add user
                user = new OnlineUser();
                onlineUsers.Add(uuid, user);
                // monitor
                main.MonitorNetwork("Added Online User '" + UuidToString(uuid) + "'");
            }
            // set details
            user.nuid = nuid;
            user.port = port;
            user.expireTime = main.ElapsedTime + OFFLINE_TIME;
        }

        /// <summary>
        /// random numbers for hub index
        /// </summary>
        readonly Random randomHubIndex = new();

        /// <summary>
        /// Request the nuid of a user
        /// </summary>
        /// <param name="uuid"></param>
        public void RequestNuid(uint uuid)
        {
            // prepare message
            BinaryWriter message = localNode.PrepareMessage(new LocalNode.Nuid(), true);

            // add header
            message.Write(Sim.VERSION);
            // add message ID
            message.Write((short)MESSAGE_ID.UserNuidRequest);
            // add uuid
            message.Write(uuid);

            main.MonitorNetwork("RequestNuid '" + UuidToString(uuid) + "'");

            // get a random start index
            int startIndex = randomHubIndex.Next(hubList.Count);
            // initialize request count
            int requestCount = 0;

            // send request to at 5 online hubs
            for (int i = 0; i < hubList.Count && requestCount < REQUEST_NUID_NUM_SAMPLES; i++)
            {
                // get hub
                Hub hub = hubList[(startIndex + i) % hubList.Count];
                // check if hub is online
                if (hub.online)
                {
                    // send message
                    localNode.Send(hub.endPoint);
                    // update count
                    requestCount++;
                }
            }
        }

        /// <summary>
        /// Schedule a join to another user
        /// </summary>
        public bool scheduleJoinUser = false;
        uint scheduleJoinUuid = 0;

        /// <summary>
        /// Schedule a join to another user
        /// </summary>
        /// <param name="uuid"></param>
        public void ScheduleJoinUser(uint uuid)
        {
            // schedule join
            scheduleJoinUser = true;
            // make a uuid
            scheduleJoinUuid = uuid;
            // request nuid
            RequestNuid(uuid);
        }

#endregion
    }
}
