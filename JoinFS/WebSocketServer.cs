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
        // One send at a time per client: WebSocket.SendAsync throws InvalidOperationException if a
        // second call overlaps a still-pending one, which the broadcast loop's catch previously
        // treated as a dead client and evicted - even though it was healthy, just slow to drain a
        // prior tick's send. Keyed by the same WebSocket instance kept in _clients.
        readonly Dictionary<WebSocket, SemaphoreSlim> _sendLocks = [];
        readonly object _clientLock = new();
        readonly CancellationTokenSource _cts = new();
        readonly Thread _listenThread;

        // Change detection. The feed flushes at most once per interval (one batched message carrying every
        // aircraft that changed), and re-sends a silent aircraft every keep-alive - kept well below the
        // map's default 60 s stale-timeout, which purges aircraft that stop updating.
        const double FlushIntervalSeconds = 0.05;   // 20 Hz
        const double KeepAliveSeconds = 15.0;
        readonly AircraftFeedFilter _filter;

        // Clients that connected since the last tick; they get every known aircraft once, because the
        // delta feed alone would show them an aircraft only when it next changes.
        readonly System.Collections.Concurrent.ConcurrentQueue<WebSocket> _newClients = new();

        // Aircraft currently skipped for an implausible position, so the warning logs once per
        // aircraft per bad streak instead of every tick (which would flood the log for a peer/hub
        // stuck sending garbled positions) - cleared on recovery or disappearance, same pass as
        // the filter's own stale cleanup below.
        readonly HashSet<Guid> _warnedImplausible = [];

        public WebSocketServer(Main main)
        {
            this.main = main;
            _filter = new AircraftFeedFilter(() => main.ElapsedTime, FlushIntervalSeconds, KeepAliveSeconds);

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
                    // a real accept failure, not routine chatter - no silent errors
                    main.monitor.Write($"WebSocket accept error: {ex.Message}");
                    // don't spin a core if GetContext() is in a persistently faulting state
                    Thread.Sleep(1000);
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
                // a real upgrade failure, not routine chatter - no silent errors
                main.monitor.Write($"WebSocket upgrade error: {ex.Message}");
                return;
            }

            lock (_clientLock)
            {
                _clients.Add(ws);
                _sendLocks[ws] = new SemaphoreSlim(1, 1);
            }
            _newClients.Enqueue(ws);
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
                SemaphoreSlim sendLock;
                lock (_clientLock)
                {
                    _clients.Remove(ws);
                    _sendLocks.Remove(ws, out sendLock);
                }
                sendLock?.Dispose();
                if (main.settingsWebSocketLog)
                    main.monitor.Write($"WebSocket client disconnected ({_clients.Count} remaining)");
                ws.Dispose();
            }
        }

        static string FormatFreq(float value) => value == 0 ? "" : value.ToString("F3");

        AircraftSnapshot SnapshotFromAircraft(Sim.Aircraft aircraft)
        {
            var snap = new AircraftSnapshot();
            snap.guid = main.network.Peers.GetAircraftIdentityGuid(aircraft.ownerNuid, aircraft.netId, aircraft.simId).ToString();
            snap.callsign = aircraft.flightPlan.callsign;
            snap.registration = aircraft.flightPlan.registration;
            snap.icaoAirline = aircraft.flightPlan.icaoAirline;
            snap.flightNumber = aircraft.flightPlan.flightNumber;
            snap.nickname = main.network.Peers.GetNodeName(aircraft.ownerNuid);
            // mirrors the desktop Aircraft Dialog's (R)/(A) distinction, so a map consumer can
            // distinguish a replayed/AI aircraft from a real pilot without a separate lookup
            snap.trafficType = aircraft.user ? "pilot" : (aircraft.owner == Sim.Obj.Owner.Recorder ? "recorded" : "ai");

            var pos = aircraft.Position;
            if (pos != null)
            {
                snap.latitude  = pos.geo.z * (180.0 / Math.PI);
                snap.longitude = pos.geo.x * (180.0 / Math.PI);
                snap.altitude  = pos.geo.y * Sim.FEET_PER_METRE;
                // compass 0-359: playback's running heading is unwrapped (361, 722, ...) and would fail Vector.IsPlausibleHeading
                snap.heading   = Vector.HeadingDegrees(pos.angles.y);
                snap.onGround  = pos.ground != 0;
            }

            snap.speed = Math.Sqrt(
                aircraft.netVelocity.linear.x * aircraft.netVelocity.linear.x +
                aircraft.netVelocity.linear.z * aircraft.netVelocity.linear.z) * 1.9438444925; // calculated m/s * 1.9438444925 = knots 

            snap.icaoType = aircraft.flightPlan.icaoType;
            snap.from     = aircraft.flightPlan.departure;
            snap.to       = aircraft.flightPlan.destination;
            snap.rules    = aircraft.flightPlan.rules;
            snap.route    = aircraft.flightPlan.route;
            snap.remarks  = aircraft.flightPlan.remarks;
            snap.livery   = aircraft.ModelLivery;

            if (aircraft.variableSet != null)
            {
                snap.com1 = FormatFreq(aircraft.variableSet.GetFrequency(vuidCom1));
                snap.com2 = FormatFreq(aircraft.variableSet.GetFrequency(vuidCom2));
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

        AircraftSnapshot SnapshotFromHubUser(HubDirectory.HubUser user)
        {
            var snap = new AircraftSnapshot();
            snap.guid     = user.guid.ToString();
            snap.callsign = user.flightPlan.callsign;
            snap.registration = user.flightPlan.registration;
            snap.icaoAirline = user.flightPlan.icaoAirline;
            snap.flightNumber = user.flightPlan.flightNumber;
            snap.nickname = user.nickname;
            // global hub users are always real connected pilots
            snap.trafficType = "pilot";
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

        static object ToJson(in AircraftSnapshot snapshot)
        {
            // rounded here and nowhere else, so the wire values and the change detection cannot drift apart
            AircraftSnapshot s = snapshot.Rounded();
            return new
            {
                callsign = s.callsign,
                registration = s.registration,
                // "" for General Aviation (no explicit airline data and the callsign doesn't shape-match a
                // commercial flight) - see CallsignRules.DeriveIcaoAirline, applied in
                // Network.SendFlightPlanMessage before this snapshot is taken, so this already reflects a
                // callsign-derived fallback when nothing more authoritative supplied one.
                icaoAirline = s.icaoAirline,
                flightNumber = s.flightNumber,
                nickname = s.nickname,
                trafficType = s.trafficType,
                guid     = s.guid,
                altitude = s.altitude,
                speed    = s.speed,
                heading  = s.heading,
                latitude = s.latitude,
                longitude= s.longitude,
                com1     = s.com1,
                com2     = s.com2,
                squawk   = s.squawk,
                icaoType = s.icaoType,
                from     = s.from,
                to       = s.to,
                rules    = s.rules,
                route    = s.route,
                remarks  = s.remarks,
                livery   = s.livery,
                gear     = s.gear,
                flaps    = s.flaps,
                lights   = new { nav = s.lightNav, beacon = s.lightBeacon, landing = s.lightLanding, taxi = s.lightTaxi, strobe = s.lightStrobe },
                engines  = new { eng1Running = s.eng1, eng2Running = s.eng2, eng3Running = s.eng3, eng4Running = s.eng4 },
                rotorRpm = s.rotorRpm,
                onGround = s.onGround
            };
        }

        // Newtonsoft throws on NaN/Infinity by default; a single stray non-finite value in one
        // aircraft record would otherwise take down the whole feed (and, via the work-thread
        // failure streak, the process). Emit 0 for those instead.
        static readonly JsonSerializerSettings _jsonSettings = new() { FloatFormatHandling = FloatFormatHandling.DefaultValue };

        // A snapshot whose position didn't survive decoding (wire-format mismatch upstream): skip
        // it rather than publish 1e202 / int.MinValue to every client.
        static bool PlausibleSnapshot(in AircraftSnapshot s) =>
            double.IsFinite(s.latitude) && double.IsFinite(s.longitude) && double.IsFinite(s.altitude)
            && Math.Abs(s.latitude) <= 90.0 && Math.Abs(s.longitude) <= 180.0
            && s.altitude >= -2000.0 && s.altitude <= 300000.0
            && Vector.IsPlausibleHeading(s.heading);

        // Called from DoWork() inside conch lock
        public void DoWork()
        {
            try
            {
                DoWorkInner();
            }
            catch (Exception ex)
            {
                // never let the feed feed the work-thread failure streak (Program.cs escalates
                // 5 throws in 5 s to a full shutdown) - but never swallow it silently either: an
                // exception escaping DoWorkInner is always unexpected, so this logs regardless of
                // the websocketlog setting (no silent errors).
                main.monitor.Write($"WebSocket DoWork error: {ex.Message}");
            }
        }

        void DoWorkInner()
        {
            // runs every work-loop pass (~200/s): only evaluate and publish on the flush tick
            if (!_filter.FlushDue()) return;

            var due = new List<AircraftSnapshot>();
            var seen = new HashSet<Guid>();

            CollectSimAircraft(due, seen);
            CollectHubUsers(due, seen);

            // drop change-detection/warned-implausible state for aircraft/users that are gone (was
            // an unbounded leak) - _warnedImplausible can hold keys the filter never did (an
            // aircraft that's never once been plausible never entered it), so it's pruned
            // against the same "seen this tick" set independently.
            _filter.Retain(seen);
            if (_warnedImplausible.Count > 0)
            {
                _warnedImplausible.RemoveWhere(k => !seen.Contains(k));
            }

            SendEverythingToNewClients();

            if (due.Count == 0) return;
            List<WebSocket> clients;
            lock (_clientLock) clients = [.. _clients];
            Send(BuildMessage(due), clients);
        }

        void CollectSimAircraft(List<AircraftSnapshot> due, HashSet<Guid> seen)
        {
            if (main.sim == null) return;

            foreach (var obj in main.sim.View.Objects)
            {
                if (obj is not Sim.Aircraft aircraft) continue;

                Guid key = main.network.Peers.GetAircraftIdentityGuid(aircraft.ownerNuid, aircraft.netId, aircraft.simId);
                // tracked here (not just on the plausible path below) so a transient bad streak
                // doesn't make the stale-cleanup pass mistake this aircraft for one that
                // disappeared and drop its filter state out from under it
                seen.Add(key);

                var snap = SnapshotFromAircraft(aircraft);
                if (!PlausibleSnapshot(in snap))
                {
                    if (_warnedImplausible.Add(key))
                        main.monitor.Write($"[WS] skipping {aircraft.flightPlan.callsign}: implausible position (peer/hub version mismatch?)");
                    continue;
                }
                _warnedImplausible.Remove(key);

                bool known = _filter.Contains(key);
                if (main.settingsWebSocketLog && !known)
                {
                    if (aircraft.variableSet == null)
                        main.monitor.Write($"[WS-DBG] {aircraft.flightPlan.callsign}: variableSet=NULL");
                    else
                        main.monitor.Write($"[WS-DBG] {aircraft.flightPlan.callsign}: variableSet OK, integers.Count={aircraft.variableSet.integers.Count}, com1_raw={aircraft.variableSet.GetInteger(vuidCom1)}, gear_raw={aircraft.variableSet.GetInteger(vuidGear)}");
                }

                if (_filter.ShouldSend(key, in snap))
                    due.Add(snap);
            }
        }

        void CollectHubUsers(List<AircraftSnapshot> due, HashSet<Guid> seen)
        {
            if (!main.settingsWhazzupPublic) return;

            foreach (var hub in main.network.Hubs.List)
            {
                foreach (var user in hub.userList)
                {
                    Guid key = user.guid;
                    var snap = SnapshotFromHubUser(user);
                    if (!PlausibleSnapshot(in snap)) continue;
                    seen.Add(key);

                    if (main.settingsWebSocketLog && !_filter.Contains(key))
                        main.monitor.Write($"[WS-DBG-HUB] {user.flightPlan.callsign} ({user.nickname}): hub user, no variableSet, squawk={user.squawk}, freq={user.frequency}");

                    if (_filter.ShouldSend(key, in snap))
                        due.Add(snap);
                }
            }
        }

        // A client that connected since the last tick gets every aircraft the filter knows, sent to it
        // alone: the delta feed would otherwise show it a parked aircraft only when that next changes.
        void SendEverythingToNewClients()
        {
            List<WebSocket> newClients = null;
            while (_newClients.TryDequeue(out var ws))
                (newClients ??= []).Add(ws);
            if (newClients == null) return;

            var known = new List<AircraftSnapshot>(_filter.Known());
            if (known.Count == 0) return;
            Send(BuildMessage(known), newClients);
        }

        static string BuildMessage(List<AircraftSnapshot> snapshots)
        {
            // serialize outside the lock, broadcast off the work thread
            var jsonObjs = new List<object>(snapshots.Count);
            foreach (var s in snapshots) jsonObjs.Add(ToJson(s));
            return JsonConvert.SerializeObject(new { type = "aircraft_update", aircraft = jsonObjs }, _jsonSettings);
        }

        void Send(string message, List<WebSocket> snapshot)
        {
            if (snapshot.Count == 0) return;

            Task.Run(async () =>
            {
                var bytes = Encoding.UTF8.GetBytes(message);
                var segment = new ArraySegment<byte>(bytes);
                var dead = new List<WebSocket>();
                foreach (var ws in snapshot)
                {
                    if (ws.State != WebSocketState.Open) { dead.Add(ws); continue; }

                    SemaphoreSlim sendLock;
                    lock (_clientLock)
                    {
                        if (!_sendLocks.TryGetValue(ws, out sendLock)) continue; // disconnected since the snapshot was taken
                    }

                    // One send in flight per client at a time: a second DoWork tick can fire
                    // before a slow client's previous SendAsync has finished, and WebSocket.SendAsync
                    // throws InvalidOperationException on an overlapping call - which used to evict
                    // a perfectly healthy, just-slow client as if it had errored.
                    try
                    {
                        await sendLock.WaitAsync(_cts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        break; // server shutting down
                    }
                    try
                    {
                        // bound every send: a client that stops draining must not stall delivery
                        // to the others, and must actually get evicted
                        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                        await ws.SendAsync(segment, WebSocketMessageType.Text, true, cts.Token);
                    }
                    catch (Exception ex)
                    {
                        dead.Add(ws);
                        // a genuine send failure (the per-client lock above rules out the
                        // overlapping-send false positive) - a client silently disappearing with no
                        // trace is exactly what no-silent-errors is meant to prevent
                        main.monitor.Write($"WebSocket send error (dropping client): {ex.Message}");
                    }
                    finally
                    {
                        sendLock.Release();
                    }
                }
                if (dead.Count > 0)
                {
                    lock (_clientLock)
                    {
                        foreach (var ws in dead)
                        {
                            _clients.Remove(ws);
                            if (_sendLocks.Remove(ws, out var removedLock)) removedLock.Dispose();
                        }
                    }
                    foreach (var ws in dead)
                        try { ws.Dispose(); } catch { }
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
            lock (_clientLock)
            {
                foreach (var sendLock in _sendLocks.Values) sendLock.Dispose();
                _sendLocks.Clear();
            }

            _listenThread.Join(3000);
        }
    }
}
#endif
