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
                receiveNotify = ReceiveMsg
            };

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
                        // prepare message
                        WriteStatusRequestMessage(false);
                        // send status request to node
                        localNode.Send(entry.endPoint);
                    }
                    // check if user is unknown
                    else if (onlineUsers.TryGetValue(entry.uuid, out var user))
                    {
                        // update end point
                        entry.endPoint = MakeEndPoint(user);
                        // prepare message
                        WriteStatusRequestMessage(false);
                        // send status request to node
                        localNode.Send(entry.endPoint);
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

                    // prepare message
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
                // request status
                WriteStatusRequestMessage(true);
                // send request
                localNode.Send(endPoint);
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
#if FS2024
            message.Write(simObject.ownerLivery);
#endif
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
#if FS2024
            message.Write(aircraft.ownerLivery);
#endif
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
            // broadcast
            localNode.Broadcast();
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
        /// Send online message to all hubs
        /// </summary>
        public void SendFlightPlanMessage(LocalNode.Nuid ownerNuid, uint netId, Sim.FlightPlan flightPlan)
        {
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
#if FS2024
                                    string variation = (reader.PeekChar() != -1) ? reader.ReadString() : "";
                                    Sim.Obj simObject = main.sim?.UpdateObject(nuid, netId, model, variation, typerole, netTime, ref positionVelocity);
#else
                                    Sim.Obj simObject = main.sim ?. UpdateObject(nuid, netId, model, typerole, netTime, ref positionVelocity);
#endif

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
#if FS2024
                                            string variation = (reader.PeekChar() != -1) ? reader.ReadString() : "";
                                            Sim.Aircraft aircraft = main.sim?.UpdateAircraft(nuid, netId, user, plane, callsign, nickname, model, variation, typerole, netTime, ref aircraftPosition);
#else
                                            Sim.Aircraft aircraft = main.sim ?. UpdateAircraft(nuid, netId, user, plane, callsign, nickname, model, typerole, netTime, ref aircraftPosition);
#endif
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

                                    // check for shared cockpit update
                                    if (netId == uint.MaxValue)
                                    {
                                        // check for user aircraft
                                        if (main.sim != null && main.sim.userAircraft != null)
                                        {
                                            // get allowed controls
                                            bool flight = main.log.ShareCockpit(nuid) && nuid == shareFlightControls;
                                            // update aircraft in sim
                                            main.sim.UpdateAircraft(main.sim.userAircraft.ownerNuid, main.sim.userAircraft.netId, eventId, data, flight);
                                        }
                                    }
                                    else
                                    {
                                        // update aircraft in sim
                                        Sim.Aircraft aircraft = main.sim ?. UpdateAircraft(nuid, netId, eventId, data, true);

                                        // check for aircraft
                                        if (aircraft != null)
                                        {
                                            // check if aircraft is being recorded
                                            if (main.recorder.recording && aircraft.record)
                                            {
                                                // record payload
                                                main.recorder.Record(aircraft.recorderObj, eventId, data);
                                            }
                                        }
                                    }
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
                                            // write reply message
                                            WriteWeatherReplyMessage(main.sim.scheduleMetar);
                                            // send reply
                                            localNode.Send(nuid);
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
                                    // get hub flag
                                    bool hubEnabled = reader.ReadByte() != 0;
                                    // get hub request flag
                                    bool hubListRequest = reader.ReadByte() != 0;

                                    // check for hub
                                    if (hubEnabled)
                                    {
                                        // submit new hub
                                        SubmitHub(endPoint);
                                    }

                                    try
                                    {
                                        // write reply message
                                        WriteStatusMessage();
                                        // send reply
                                        localNode.Send(endPoint);
                                    }
                                    catch (Exception ex)
                                    {
                                        main.MonitorEvent("Failed to write status reply message: " + ex.Message);
                                    }

                                    // check if this is a hub
                                    if (main.settingsHub)
                                    {
                                        // check for request
                                        if (hubListRequest)
                                        {
                                            // write hub list
                                            SendHubListMessage(endPoint);
                                        }

                                        try
                                        {
                                            // get uuid
                                            uint uuid = reader.ReadUInt32();

                                            // register
                                            RegisterOnlineUser(uuid, nuid, (ushort)endPoint.Port);
                                        }
                                        catch { }
                                    }
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
                                // read guid
                                Guid guid = new(reader.ReadBytes(16));
                                // read application version
                                string appVersion = reader.ReadString();
                                // read node count
                                ushort users = reader.ReadUInt16();
                                // read ATC count
                                ushort atcCount = reader.ReadUInt16();
                                // read airport
                                string atcAirport = (atcCount > 0) ? reader.ReadString() : "";
                                // read level
                                int atcLevel = (atcCount > 0) ? reader.ReadByte() : 2;
                                // read planes
                                ushort planes = reader.ReadUInt16();
                                // read helicopters
                                ushort helicopters = reader.ReadUInt16();
                                // read boats
                                ushort boats = reader.ReadUInt16();
                                // read vehicles
                                ushort vehicles = reader.ReadUInt16();
                                // read hub enabled
                                bool hubEnabled = reader.ReadBoolean();

                                // check for hub
                                if (hubEnabled)
                                {
                                    // read address
                                    string addressText = reader.ReadString().TrimStart(' ').TrimEnd(' ');
                                    // read name
                                    string name = reader.ReadString().TrimStart(' ').TrimEnd(' ');
                                    // read about
                                    string about = reader.ReadString().TrimStart(' ').TrimEnd(' ');
                                    // read voip
                                    string voip = reader.ReadString().TrimStart(' ').TrimEnd(' ');
                                    // read next event
                                    string nextEvent = reader.ReadString().TrimStart(' ').TrimEnd(' ');
                                    // read airport
                                    string airport = reader.ReadString().TrimStart(' ').TrimEnd(' ');
                                    // read activity circle
                                    int activityCircle = reader.ReadInt32();
                                    // read flags
                                    byte flags = (dataVersion >= 10025) ? reader.ReadByte() : (byte)0;
                                    bool globalSession = (flags & 0x02) != 0;
                                    bool password = (flags & 0x04) != 0;

                                    // check for unspecified address
                                    if (addressText.Length <= 0)
                                    {
                                        // use actual end point
                                        addressText = endPoint.ToString();
                                    }

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
                                            main.MonitorEvent("Added new hub '" + name + "' - '" + UuidToString(MakeUuid(guid)) + "'");
                                        }
                                        // check if hub changed
                                        else if (
                                            hub.guid.Equals(guid) == false ||
                                            hub.appVersion.Equals(appVersion) == false ||
                                            hub.addressText.Equals(addressText) == false ||
                                            hub.name.Equals(name) == false ||
                                            hub.about.Equals(about) == false ||
                                            hub.voip.Equals(voip) == false ||
                                            hub.nextEvent.Equals(nextEvent) == false ||
                                            hub.airport.Equals(airport) == false ||
                                            hub.activityCircle != activityCircle ||
                                            hub.globalSession != globalSession ||
                                            hub.password != password
                                            )
                                        {
                                            // hub has changed
                                            hubListChanged = true;
                                        }

                                        // check for global enabled
                                        if (localNode.GlobalSession)
                                        {
                                            // check for first contact with global hub
                                            if (hub.globalSession == false && globalSession)
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
                                        hub.guid = guid;
                                        hub.appVersion = appVersion;
                                        hub.dataVersion = (ushort)dataVersion;
                                        hub.online = true;
                                        hub.offlineTime = main.ElapsedTime + OFFLINE_TIME;
                                        hub.users = users;
                                        hub.atcCount = atcCount;
                                        hub.atcAirport = atcAirport;
                                        hub.atcLevel = atcLevel;
                                        hub.planes = planes;
                                        hub.helicopters = helicopters;
                                        hub.boats = boats;
                                        hub.vehicles = vehicles;
                                        hub.addressText = addressText;
                                        hub.name = name;
                                        hub.about = about;
                                        hub.voip = voip;
                                        hub.nextEvent = nextEvent;
                                        hub.airport = airport;
                                        hub.activityCircle = activityCircle;
                                        hub.globalSession = globalSession;
                                        hub.password = password;

                                        //mainForm.MonitorNetwork("Status: " + name);
                                    }
                                }
                                else
                                {
                                    // check if this used to be a hub
                                    Hub hub = hubList.Find(h => h.nuid == nuid);
                                    if (hub != null)
                                    {
                                        main.MonitorEvent("Removed hub '" + hub.name + "' - '" + UuidToString(MakeUuid(guid)) + "'");
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
                                            user.flightPlan.departure = from;
                                            user.flightPlan.destination = to;
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
                                        user.flightPlan.departure = reader.ReadString();
                                        user.flightPlan.destination = reader.ReadString();
                                        user.flightPlan.rules = reader.ReadString();
                                        user.flightPlan.route = reader.ReadString();
                                        user.flightPlan.remarks = reader.ReadString();
                                        user.flightPlan.alternate = dataVersion >= 21003 ? reader.ReadString() : "";
                                        user.flightPlan.speed = dataVersion >= 21003 ? reader.ReadString() : "";
                                        user.flightPlan.altitude = dataVersion >= 21003 ? reader.ReadString() : "";
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
                                // check for forced updated
                                if (main.sim != null && localNode.GetLocalNuid().Equals(ownerNuid))
                                {
                                    // read version
                                    reader.ReadByte();
                                    // read flight plan
                                    main.sim.userFlightPlan.icaoType = reader.ReadString();
                                    main.sim.userFlightPlan.departure = reader.ReadString();
                                    main.sim.userFlightPlan.destination = reader.ReadString();
                                    main.sim.userFlightPlan.rules = reader.ReadString();
                                    main.sim.userFlightPlan.route = reader.ReadString();
                                    main.sim.userFlightPlan.remarks = reader.ReadString();
                                    main.sim.userFlightPlan.alternate = dataVersion >= 21003 ? reader.ReadString() : "";
                                    main.sim.userFlightPlan.speed = dataVersion >= 21003 ? reader.ReadString() : "";
                                    main.sim.userFlightPlan.altitude = dataVersion >= 21003 ? reader.ReadString() : "";
                                    main.sim.userFlightPlan.callsign = dataVersion >= 21003 ? reader.ReadString() : "";
                                    // message
                                    main.MonitorEvent("Flight Plan Update");
                                    // check for user aircraft
                                    if (main.sim.userAircraft != null)
                                    {
                                        // update version
                                        main.sim.userAircraft.flightPlanVersion++;
                                        if (main.sim.userAircraft.flightPlanVersion == 0) main.sim.userAircraft.flightPlanVersion = 1;
                                    }
                                }
                                // check for valid aircraft
                                else if (main.sim != null && main.sim.objectList.Find(o => o.ownerNuid == ownerNuid && o.netId == netId) is Sim.Aircraft aircraft)
                                {
                                    // read version
                                    aircraft.flightPlanVersion = reader.ReadByte();
                                    // read flight plan
                                    aircraft.flightPlan.icaoType = reader.ReadString();
                                    aircraft.flightPlan.departure = reader.ReadString();
                                    aircraft.flightPlan.destination = reader.ReadString();
                                    aircraft.flightPlan.rules = reader.ReadString();
                                    aircraft.flightPlan.route = reader.ReadString();
                                    aircraft.flightPlan.remarks = reader.ReadString();
                                    aircraft.flightPlan.alternate = dataVersion >= 21003 ? reader.ReadString() : "";
                                    aircraft.flightPlan.speed = dataVersion >= 21003 ? reader.ReadString() : "";
                                    aircraft.flightPlan.altitude = dataVersion >= 21003 ? reader.ReadString() : "";
                                    aircraft.flightPlan.callsign = dataVersion >= 21003 ? reader.ReadString() : "";
                                }
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
