#if CONSOLE
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace JoinFS
{
    public class WebSocketServer
    {
        readonly Main main;

        // VUIDs
        readonly uint vuidCom1;
        readonly uint vuidCom2;
        readonly uint vuidSquawk;
        readonly uint vuidGear;
        readonly uint vuidFlaps;
        readonly uint vuidLights;
        readonly uint vuidEng1;
        readonly uint vuidEng2;
        readonly uint vuidEng3;
        readonly uint vuidEng4;
        readonly uint vuidRotor;

        // WebSocket state
        readonly List<WebSocket> _clients = [];
        readonly object _clientLock = new();
        readonly CancellationTokenSource _cts = new();
        readonly Thread _listenThread;

        // Change detection
        readonly Dictionary<Guid, AircraftSnapshot> _previous = [];

        struct AircraftSnapshot
        {
            public string callsign, nickname, guid;
            public double altitude, speed, latitude, longitude;
            public int heading;
            public string com1, com2, squawk;
            public string icaoType, from, to, rules, route, remarks;
            public int gear;
            public double flaps;
            public int lightNav, lightBeacon, lightLanding, lightTaxi, lightStrobe;
            public bool eng1, eng2, eng3, eng4;
            public double rotorRpm;
        }

        public WebSocketServer(Main main)
        {
            this.main = main;

            vuidCom1   = VariableMgr.CreateVuid("com active frequency:1");
            vuidCom2   = VariableMgr.CreateVuid("com active frequency:2");
            vuidSquawk = VariableMgr.CreateVuid("transponder code:1");
            vuidGear   = VariableMgr.CreateVuid("gear handle position");
            vuidFlaps  = VariableMgr.CreateVuid("trailing edge flaps left percent");
            vuidLights = VariableMgr.CreateVuid("light states");
            vuidEng1   = VariableMgr.CreateVuid("general eng combustion:1");
            vuidEng2   = VariableMgr.CreateVuid("general eng combustion:2");
            vuidEng3   = VariableMgr.CreateVuid("general eng combustion:3");
            vuidEng4   = VariableMgr.CreateVuid("general eng combustion:4");
            vuidRotor  = VariableMgr.CreateVuid("rotor rpm:1");

            _listenThread = new Thread(ListenLoop) { IsBackground = true, Name = "WebSocket-Listener" };
            _listenThread.Start();
        }

        void ListenLoop()
        {
            var listener = new HttpListener();
            string prefix;
            try
            {
                // Try binding to all interfaces first; requires admin or a URL ACL on Windows.
                // Fall back to localhost-only if that fails.
                prefix = $"http://+:{main.settingsWebSocketPort}/ws/";
                listener.Prefixes.Add(prefix);
                listener.Start();
            }
            catch
            {
                listener = new HttpListener();
                prefix = $"http://localhost:{main.settingsWebSocketPort}/ws/";
                try
                {
                    listener.Prefixes.Add(prefix);
                    listener.Start();
                    main.monitor.Write($"WebSocket server listening on {prefix} (localhost only — run as admin or use 'netsh http add urlacl url=http://+:{main.settingsWebSocketPort}/ws/ user=Everyone' for remote access)");
                }
                catch (Exception ex)
                {
                    main.monitor.Write($"WebSocket server failed to start: {ex.Message}");
                    return;
                }
            }
            if (main.settingsWebSocketLog || prefix.Contains("localhost"))
                main.monitor.Write($"WebSocket server listening on port {main.settingsWebSocketPort}");

            while (!_cts.IsCancellationRequested)
            {
                try
                {
                    HttpListenerContext ctx = listener.GetContext();
                    if (ctx.Request.IsWebSocketRequest)
                        Task.Run(() => HandleClient(ctx), _cts.Token);
                    else
                    {
                        ctx.Response.StatusCode = 400;
                        ctx.Response.Close();
                    }
                }
                catch (Exception) when (_cts.IsCancellationRequested) { break; }
                catch (Exception ex)
                {
                    if (main.settingsWebSocketLog)
                        main.monitor.Write($"WebSocket accept error: {ex.Message}");
                }
            }

            listener.Stop();
        }

        async Task HandleClient(HttpListenerContext ctx)
        {
            WebSocket ws;
            try
            {
                var wsCtx = await ctx.AcceptWebSocketAsync(null);
                ws = wsCtx.WebSocket;
            }
            catch (Exception ex)
            {
                if (main.settingsWebSocketLog)
                    main.monitor.Write($"WebSocket upgrade error: {ex.Message}");
                return;
            }

            lock (_clientLock) _clients.Add(ws);
            if (main.settingsWebSocketLog)
                main.monitor.Write($"WebSocket client connected ({_clients.Count} total)");

            try
            {
                // keep connection alive, drain any pings
                var buf = new byte[256];
                while (!_cts.IsCancellationRequested && ws.State == WebSocketState.Open)
                {
                    var result = await ws.ReceiveAsync(buf, _cts.Token);
                    if (result.MessageType == WebSocketMessageType.Close)
                        await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
                }
            }
            catch { /* client disconnected */ }
            finally
            {
                lock (_clientLock) _clients.Remove(ws);
                if (main.settingsWebSocketLog)
                    main.monitor.Write($"WebSocket client disconnected ({_clients.Count} remaining)");
                ws.Dispose();
            }
        }

        static string FormatFreq(int raw)
        {
            string s = raw.ToString();
            return s.Length < 3 ? "" : s[..3] + "." + s[3..];
        }

        AircraftSnapshot SnapshotFromAircraft(Sim.Aircraft aircraft)
        {
            var snap = new AircraftSnapshot();
            snap.guid = main.network.GetNodeGuid(aircraft.ownerNuid).ToString();
            snap.callsign = aircraft.flightPlan.callsign;
            snap.nickname = main.network.GetNodeName(aircraft.ownerNuid);

            var pos = aircraft.Position;
            if (pos != null)
            {
                snap.latitude  = pos.geo.z * (180.0 / Math.PI);
                snap.longitude = pos.geo.x * (180.0 / Math.PI);
                snap.altitude  = pos.geo.y * Sim.FEET_PER_METRE;
                snap.heading   = (int)(pos.angles.y * 180.0 / Math.PI);
            }

            snap.speed = Math.Sqrt(
                aircraft.netVelocity.linear.x * aircraft.netVelocity.linear.x +
                aircraft.netVelocity.linear.z * aircraft.netVelocity.linear.z) * 1.9438444925;

            snap.icaoType = aircraft.flightPlan.icaoType;
            snap.from     = aircraft.flightPlan.departure;
            snap.to       = aircraft.flightPlan.destination;
            snap.rules    = aircraft.flightPlan.rules;
            snap.route    = aircraft.flightPlan.route;
            snap.remarks  = aircraft.flightPlan.remarks;

            if (aircraft.variableSet != null)
            {
                snap.com1   = FormatFreq(aircraft.variableSet.GetInteger(vuidCom1));
                snap.com2   = FormatFreq(aircraft.variableSet.GetInteger(vuidCom2));
                snap.squawk = aircraft.variableSet.GetInteger(vuidSquawk).ToString();
                snap.gear   = aircraft.variableSet.GetInteger(vuidGear);
                snap.flaps  = aircraft.variableSet.GetFloat(vuidFlaps);
                int lights  = aircraft.variableSet.GetInteger(vuidLights);
                snap.lightNav     = (lights >> 0) & 1;
                snap.lightBeacon  = (lights >> 1) & 1;
                snap.lightLanding = (lights >> 2) & 1;
                snap.lightTaxi    = (lights >> 3) & 1;
                snap.lightStrobe  = (lights >> 4) & 1;
                snap.eng1 = aircraft.variableSet.GetInteger(vuidEng1) != 0;
                snap.eng2 = aircraft.variableSet.GetInteger(vuidEng2) != 0;
                snap.eng3 = aircraft.variableSet.GetInteger(vuidEng3) != 0;
                snap.eng4 = aircraft.variableSet.GetInteger(vuidEng4) != 0;
                snap.rotorRpm = aircraft.variableSet.GetFloat(vuidRotor);
            }

            return snap;
        }

        AircraftSnapshot SnapshotFromHubUser(Network.HubUser user)
        {
            var snap = new AircraftSnapshot();
            snap.guid     = user.guid.ToString();
            snap.callsign = user.flightPlan.callsign;
            snap.nickname = user.nickname;
            snap.latitude  = user.latitude;
            snap.longitude = user.longitude;
            snap.altitude  = user.altitude;
            snap.speed     = user.speed;
            snap.heading   = user.heading;
            snap.squawk    = user.squawk.ToString();
            snap.icaoType  = user.flightPlan.icaoType;
            snap.from      = user.flightPlan.departure;
            snap.to        = user.flightPlan.destination;
            snap.rules     = user.flightPlan.rules.Length > 0 ? user.flightPlan.rules : (user.ifr ? "IFR" : "VFR");
            snap.route     = user.flightPlan.route;
            snap.remarks   = user.flightPlan.remarks;
            // hub users only carry a single ATC frequency; regular aircraft have freq=0
            string freqStr = user.frequency.ToString();
            snap.com1 = freqStr.Length >= 4 ? "1" + freqStr[..2] + "." + freqStr.Substring(2, 2) : "";
            snap.com2 = "";
            return snap;
        }

        static bool SnapshotsEqual(in AircraftSnapshot a, in AircraftSnapshot b) =>
            a.callsign == b.callsign && a.nickname == b.nickname &&
            a.altitude == b.altitude && a.speed == b.speed &&
            a.latitude == b.latitude && a.longitude == b.longitude &&
            a.heading == b.heading &&
            a.com1 == b.com1 && a.com2 == b.com2 && a.squawk == b.squawk &&
            a.icaoType == b.icaoType && a.from == b.from && a.to == b.to &&
            a.rules == b.rules && a.route == b.route && a.remarks == b.remarks &&
            a.gear == b.gear && a.flaps == b.flaps &&
            a.lightNav == b.lightNav && a.lightBeacon == b.lightBeacon &&
            a.lightLanding == b.lightLanding && a.lightTaxi == b.lightTaxi && a.lightStrobe == b.lightStrobe &&
            a.eng1 == b.eng1 && a.eng2 == b.eng2 && a.eng3 == b.eng3 && a.eng4 == b.eng4 &&
            a.rotorRpm == b.rotorRpm;

        static object ToJson(in AircraftSnapshot s) => new
        {
            callsign = s.callsign,
            nickname = s.nickname,
            guid     = s.guid,
            altitude = Math.Round(s.altitude, 0),
            speed    = Math.Round(s.speed, 1),
            heading  = s.heading,
            latitude = Math.Round(s.latitude, 6),
            longitude= Math.Round(s.longitude, 6),
            com1     = s.com1,
            com2     = s.com2,
            squawk   = s.squawk,
            icaoType = s.icaoType,
            from     = s.from,
            to       = s.to,
            rules    = s.rules,
            route    = s.route,
            remarks  = s.remarks,
            gear     = s.gear,
            flaps    = Math.Round(s.flaps, 3),
            lights   = new { nav = s.lightNav, beacon = s.lightBeacon, landing = s.lightLanding, taxi = s.lightTaxi, strobe = s.lightStrobe },
            engines  = new { eng1Running = s.eng1, eng2Running = s.eng2, eng3Running = s.eng3, eng4Running = s.eng4 },
            rotorRpm = Math.Round(s.rotorRpm, 1)
        };

        // Called from DoWork() inside conch lock
        public void DoWork()
        {
            var changedSnaps = new List<AircraftSnapshot>();

            // --- sim aircraft ---
            if (main.sim != null)
            {
                foreach (var obj in main.sim.objectList)
                {
                    if (obj is not Sim.Aircraft aircraft) continue;

                    Guid key = main.network.GetNodeGuid(aircraft.ownerNuid);
                    if (key == Guid.Empty) key = new Guid(aircraft.simId, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

                    var snap = SnapshotFromAircraft(aircraft);

                    bool existed = _previous.TryGetValue(key, out var prev);
                    _previous[key] = snap;

                    if (main.settingsWebSocketLog && !existed)
                    {
                        if (aircraft.variableSet == null)
                            main.monitor.Write($"[WS-DBG] {aircraft.flightPlan.callsign}: variableSet=NULL");
                        else
                            main.monitor.Write($"[WS-DBG] {aircraft.flightPlan.callsign}: variableSet OK, integers.Count={aircraft.variableSet.integers.Count}, com1_raw={aircraft.variableSet.GetInteger(vuidCom1)}, gear_raw={aircraft.variableSet.GetInteger(vuidGear)}");
                    }

                    if (existed && !SnapshotsEqual(snap, prev))
                        changedSnaps.Add(snap);
                    else if (!existed)
                        changedSnaps.Add(snap); // send initial state on first appearance
                }
            }

            // --- global hub users ---
            if (main.settingsWhazzupPublic)
            {
                foreach (var hub in main.network.hubList)
                {
                    foreach (var user in hub.userList)
                    {
                        Guid key = user.guid;
                        var snap = SnapshotFromHubUser(user);

                        bool existed = _previous.TryGetValue(key, out var prev);
                        _previous[key] = snap;

                        if (main.settingsWebSocketLog && !existed)
                            main.monitor.Write($"[WS-DBG-HUB] {user.flightPlan.callsign} ({user.nickname}): hub user, no variableSet, squawk={user.squawk}, freq={user.frequency}");

                        if (existed && !SnapshotsEqual(snap, prev))
                            changedSnaps.Add(snap);
                        else if (!existed)
                            changedSnaps.Add(snap);
                    }
                }
            }

            if (changedSnaps.Count == 0) return;

            // serialize outside the lock, broadcast off the work thread
            var jsonObjs = new List<object>(changedSnaps.Count);
            foreach (var s in changedSnaps) jsonObjs.Add(ToJson(s));
            string message = JsonConvert.SerializeObject(new { type = "aircraft_update", aircraft = jsonObjs });

            List<WebSocket> snapshot;
            lock (_clientLock) snapshot = [.. _clients];

            if (snapshot.Count == 0) return;

            bool log = main.settingsWebSocketLog;
            Task.Run(async () =>
            {
                var bytes = Encoding.UTF8.GetBytes(message);
                var segment = new ArraySegment<byte>(bytes);
                foreach (var ws in snapshot)
                {
                    if (ws.State != WebSocketState.Open) continue;
                    try
                    {
                        await ws.SendAsync(segment, WebSocketMessageType.Text, true, CancellationToken.None);
                    }
                    catch (Exception ex)
                    {
                        if (log) main.monitor.Write($"WebSocket send error: {ex.Message}");
                    }
                }
            });
        }

        public void Close()
        {
            _cts.Cancel();

            List<WebSocket> snapshot;
            lock (_clientLock) snapshot = [.. _clients];

            foreach (var ws in snapshot)
            {
                try { ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Server shutting down", CancellationToken.None).Wait(1000); }
                catch { /* ignore */ }
                ws.Dispose();
            }

            _listenThread.Join(3000);
        }
    }
}
#endif
