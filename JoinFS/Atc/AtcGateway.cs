using System;
using System.Collections.Generic;
using System.Text;
using JoinFS.Fsd;

namespace JoinFS
{
    /// <summary>
    /// The station an ATC client has logged in as (docs/ATC/atc-fsd-plan.md §4.2). Immutable: a change replaces the record.
    /// </summary>
    /// <param name="Callsign">The whole callsign the client sent (<c>EDDM_TWR</c>, <c>EDDM_N_TWR</c>).</param>
    /// <param name="PositionKnown">The client has sent its first <c>%</c>, so facility, frequency and position are real.</param>
    public sealed record AtcStationInfo(
        string Callsign, string Name, int Rating, FsdFacility Facility, int VisibilityRange,
        int FrequencyKhz, double Latitude, double Longitude, bool PositionKnown)
    {
        /// <summary>
        /// May this station act as a controller? The facility says so once the client has sent it; until then the
        /// rating does (1 is an observer).
        /// </summary>
        public bool IsController => PositionKnown ? Facility != FsdFacility.Observer : Rating > 1;
    }

    /// <summary>
    /// The FSD server as the ATC client sees it (plan §4.1): login, the local station and the answers given as
    /// <c>SERVER</c>. It runs on the app thread, never blocks and reaches the client only through
    /// <see cref="IFsdConnection"/>, so a test drives it with fakes. Stage 2 is the login and the <c>SERVER</c> row of
    /// §4.3; stage 4 adds the traffic (<see cref="AtcTrafficFeed"/>) and the aircraft row; the mesh and the other rows
    /// come in later stages.
    /// </summary>
    public sealed class AtcGateway : IFsdHandler
    {
        /// <summary>A line longer than this closes the connection (plan §4.2 rule 5).</summary>
        public const int MaxLineBytes = 2048;

        /// <summary>How often a logged-in client gets a keep-alive, ms.</summary>
        public const long HeartbeatIntervalMs = 30000;

        /// <summary>FSD lines are bytes of one code page; Latin-1 keeps every byte the client sent.</summary>
        public static readonly Encoding Wire = Encoding.Latin1;

        const string Server = "SERVER";

        sealed class Client
        {
            public Client(IFsdConnection connection) { Connection = connection; }
            public IFsdConnection Connection { get; }
            public bool IsStation { get; set; }
        }

        readonly Action<string> log;
        readonly AtcTrafficFeed feed;
        readonly List<Client> clients = new List<Client>();
        Client main;
        long nextHeartbeatMs = long.MinValue;
        bool shutdown;

        /// <param name="log">Where monitor and log lines go (<c>Main.MonitorEvent</c>).</param>
        /// <param name="traffic">The aircraft of the session (stage 4); null serves none.</param>
        public AtcGateway(Action<string> log, IAtcTraffic traffic = null)
        {
            this.log = log ?? (_ => { });
            if (traffic != null) feed = new AtcTrafficFeed(traffic);
        }

        /// <summary>The station that is logged in on this instance, or null. Replaced, never changed.</summary>
        public AtcStationInfo Station { get; private set; }

        /// <summary>How many connections are open, logged in or not.</summary>
        public int ConnectionCount => clients.Count;

        // ---- driven by the listener ----

        public void OnConnected(IFsdConnection connection)
        {
            if (shutdown)
            {
                connection.Close();
                return;
            }
            clients.Add(new Client(connection));
            log("ATC client connected from " + connection.RemoteAddress);
        }

        public void OnClosed(IFsdConnection connection)
        {
            Client client = Find(connection);
            if (client == null) return;
            clients.Remove(client);
            if (client == main) LogOff(client, "connection closed");
            else log("ATC client from " + connection.RemoteAddress + " disconnected");
        }

        public void OnLine(IFsdConnection connection, string text)
        {
            Client client = Find(connection);
            if (client == null) return;

            if (text.Length > MaxLineBytes)
            {
                Refuse(client, null, "line too long");
                return;
            }

            if (!FsdLine.TryParse(text, out FsdLine line))
            {
                // garbage from a client that is logged in is ignored; before the login it ends the connection
                if (!client.IsStation) Refuse(client, null, "not an FSD line");
                return;
            }

            if (!client.IsStation)
            {
                switch (line.Verb)
                {
                    case FsdVerb.ClientId:
                        // CID, software and challenge are not looked at
                        break;
                    case FsdVerb.AddAtc:
                        Login(client, line);
                        break;
                    default:
                        Refuse(client, null, "unexpected " + line.Verb + " before login");
                        break;
                }
                return;
            }

            switch (line.Verb)
            {
                case FsdVerb.AtcPosition:
                    UpdatePosition(client, line);
                    break;
                case FsdVerb.DeleteAtc:
                    clients.Remove(client);
                    LogOff(client, "#DA");
                    client.Connection.Close();
                    break;
                case FsdVerb.Ping:
                case FsdVerb.ClientQuery:
                    if (line.To == Server) AnswerServer(client, line);
                    else if (line.Verb == FsdVerb.ClientQuery) AnswerAircraft(client, line);
                    break;
                // everything else is a row of §4.3 that a later stage serves
            }
        }

        /// <summary>Called from <c>Main.DoWork</c>.</summary>
        public void Tick(long nowMs)
        {
            if (main == null)
            {
                nextHeartbeatMs = long.MinValue;
                return;
            }
            if (nextHeartbeatMs == long.MinValue) nextHeartbeatMs = nowMs + HeartbeatIntervalMs;
            if (nowMs >= nextHeartbeatMs)
            {
                nextHeartbeatMs = nowMs + HeartbeatIntervalMs;
                Send(main, new FsdLine("#DL", Server, "*", "0", "0"));
            }

            // traffic goes to a logged-in client only (§4.7)
            Client station = main;
            feed?.Tick(nowMs, line => Send(station, line));
        }

        /// <summary>Close every connection and log the station off. The gateway takes no more connections.</summary>
        public void Shutdown()
        {
            shutdown = true;
            Client[] open = clients.ToArray();
            clients.Clear();
            foreach (Client client in open)
            {
                if (client == main) LogOff(client, "JoinFS closing");
                client.Connection.Close();
            }
        }

        // ---- login ----

        void Login(Client client, FsdLine line)
        {
            if (!FsdAddAtc.TryParse(line, out FsdAddAtc add))
            {
                Refuse(client, null, "malformed #AA");
                return;
            }

            if (!FsdCallsign.IsValidStation(add.Callsign))
            {
                Refuse(client, FsdError.Create(add.Callsign, FsdErrorCode.CallsignInvalid, add.Callsign, "Invalid callsign"),
                    "invalid callsign '" + add.Callsign + "'");
                return;
            }

            if (main != null)
            {
                // one station per instance (D5)
                FsdErrorCode code = Station.Callsign == add.Callsign ? FsdErrorCode.CallsignInUse : FsdErrorCode.AlreadyRegistered;
                Refuse(client, FsdError.Create(add.Callsign, code, add.Callsign,
                        code == FsdErrorCode.CallsignInUse ? "Callsign in use" : "A station is already logged in"),
                    add.Callsign + ": " + Station.Callsign + " is already logged in on this instance");
                return;
            }

            client.IsStation = true;
            main = client;
            feed?.Reset();
            Station = new AtcStationInfo(add.Callsign, add.RealName, add.Rating, FsdFacility.Observer, 0, 0, 0, 0, false);
            log("ATC station " + add.Callsign + " logged in");

            // what the client waits for before it shows "connected" (found in the joinfs-euroscope-bridge, not verified
            // against EuroScope: docs/ATC/euroscope-setup.md): a welcome, then the capabilities exchange
            string callsign = add.Callsign;
            Send(client, new FsdTextMessage { From = "server", To = callsign, Message = "JoinFS ATC gateway" }.ToLine());
            Send(client, FsdQuery.Query(Server, callsign, "CAPS").ToLine());
            Send(client, FsdQuery.Response(Server, callsign, "ATC", Station.IsController ? "Y" : "N", callsign).ToLine());
            Send(client, FsdQuery.Response(Server, callsign, "CAPS", "ATCINFO=1", "SECPOS=1").ToLine());
            Send(client, FsdQuery.Response(Server, callsign, "IP", client.Connection.RemoteAddress).ToLine());
        }

        void UpdatePosition(Client client, FsdLine line)
        {
            if (client != main || !FsdAtcPosition.TryParse(line, out FsdAtcPosition position)) return;
            if (position.Callsign != Station.Callsign) return;

            AtcStationInfo before = Station;
            int frequency = position.FrequenciesKhz.Count > 0 ? position.FrequenciesKhz[0] : 0;
            Station = before with
            {
                Rating = position.Rating,
                Facility = position.Facility,
                VisibilityRange = position.VisibilityRange,
                FrequencyKhz = frequency,
                Latitude = position.Latitude,
                Longitude = position.Longitude,
                PositionKnown = true,
            };

            if (!before.PositionKnown || before.FrequencyKhz != frequency || before.Facility != position.Facility)
            {
                string tuned = frequency > 0 ? " on " + FsdFrequency.ToMegahertz(frequency) : "";
                log("ATC station " + Station.Callsign + " is " + position.Facility + tuned);
            }
        }

        void LogOff(Client client, string why)
        {
            if (client != main) return;
            log("ATC station " + Station.Callsign + " logged off (" + why + ")");
            main = null;
            Station = null;
            feed?.Reset();
        }

        // ---- answers given as SERVER (§4.3 first row) ----

        void AnswerServer(Client client, FsdLine line)
        {
            if (FsdPing.TryParse(line, out FsdPing ping))
            {
                if (!ping.IsPong) Send(client, ping.Answer().ToLine());
                return;
            }

            if (!FsdQuery.TryParse(line, out FsdQuery query) || query.IsResponse) return;
            string asker = query.From;
            switch (query.Type)
            {
                case "ATC":
                    string asked = query.Payload.Length > 0 ? query.Payload[0] : "";
                    bool controller = main != null && Station.Callsign == asked && Station.IsController;
                    Send(client, FsdQuery.Response(Server, asker, "ATC", controller ? "Y" : "N", asked).ToLine());
                    break;
                case "IP":
                    Send(client, FsdQuery.Response(Server, asker, "IP", client.Connection.RemoteAddress).ToLine());
                    break;
                case "FP":
                    string aircraft = query.Payload.Length > 0 ? query.Payload[0] : "";
                    if (feed != null && feed.TryFind(aircraft, out AtcAircraft found))
                        Send(client, AtcTrafficFeed.FlightPlanFor(aircraft, asker, found));
                    else
                        Send(client, FsdError.Create(asker, FsdErrorCode.NoFlightPlan, aircraft, "No flight plan").ToLine());
                    break;
            }
        }

        // ---- queries addressed to an aircraft (§4.3, the aircraft row) ----

        /// <summary>
        /// <c>$CQ</c> RN and FP to an aircraft's callsign are answered here from the mesh data. Anything else, and a
        /// callsign that is no aircraft, is dropped (the rows of a later stage serve the other callsigns).
        /// </summary>
        void AnswerAircraft(Client client, FsdLine line)
        {
            if (feed == null || !FsdQuery.TryParse(line, out FsdQuery query) || query.IsResponse) return;
            if (query.Type != "RN" && query.Type != "FP") return;
            if (!feed.TryFind(query.To, out AtcAircraft aircraft)) return;

            Send(client, query.Type == "RN"
                ? AtcTrafficFeed.RealNameFor(query.To, query.From, aircraft)
                : AtcTrafficFeed.FlightPlanFor(query.To, query.From, aircraft));
        }

        // ---- helpers ----

        Client Find(IFsdConnection connection)
        {
            foreach (Client client in clients)
                if (ReferenceEquals(client.Connection, connection)) return client;
            return null;
        }

        void Send(Client client, FsdLine line) => client.Connection.Send(line.ToString());

        /// <summary>Tell the client why (when it is a case with an <c>$ER</c>), close, and log the reason. A station on it is logged off.</summary>
        void Refuse(Client client, FsdError error, string why)
        {
            log("ATC client from " + client.Connection.RemoteAddress + " refused: " + why);
            if (error != null) Send(client, error.ToLine());
            clients.Remove(client);
            LogOff(client, why);
            client.Connection.Close();
        }
    }
}
