using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using JoinFS.Properties;

namespace JoinFS
{
    public class Whazzup
    {
        Main main;

        /// <summary>
        /// Vuids
        /// </summary>
        uint vuidCom1;
        uint vuidCom2;
        uint vuidSquawk;
        uint vuidIfr;
        uint vuidFrom;
        uint vuidTo;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="mainForm"></param>
        public Whazzup(Main main)
        {
            this.main = main;

            // get vuids
            vuidCom1 = VariableMgr.CreateVuid("com active frequency:1");
            vuidCom2 = VariableMgr.CreateVuid("com active frequency:2");
            vuidSquawk = VariableMgr.CreateVuid("transponder code:1");
            vuidIfr = VariableMgr.CreateVuid("ai traffic isifr");
            vuidFrom = VariableMgr.CreateVuid("ai traffic fromairport");
            vuidTo = VariableMgr.CreateVuid("ai traffic toairport");
        }

        /// <summary>
        /// Output file
        /// </summary>
        const string OUTPUT_FILE = "whazzup.txt";

        /// <summary>
        /// Update timer
        /// </summary>
        const float UPDATE_TIME = 1.0f;
        double updateTime = 0.0f;

        /// <summary>
        /// Update
        /// </summary>
        public void DoWork()
        {
            // update euroscope
            if (main.ElapsedTime > updateTime)
            {
                // next update
                updateTime = main.ElapsedTime + UPDATE_TIME;

                // check if whazzup enabled
                if (main.settingsWhazzup)
                {
                    // update
                    Update();
                }
            }
        }

        /// <summary>
        /// Convert velocity to speed
        /// </summary>
        /// <param name="velocity">Velocity</param>
        int GetSpeed(Sim.Vel velocity)
        {
            // check for valid velocity
            return (velocity == null) ? 0 : (int)(Math.Sqrt(velocity.linear.x * velocity.linear.x + velocity.linear.z * velocity.linear.z) * 1.9438444925);
        }

        /// <summary>
        /// Write client to output
        /// </summary>
        string WriteClient(bool atc, string callsign, string name, string frequency, double latitude, double longitude, double altitude, int speed, Sim.FlightPlan flightPlan, string from, string to, int squawk, int level, int range, bool ifr, ushort heading)
        {
            // frequency for display
            string frequencyText = (frequency.Length < 4) ? "" : "1" + frequency.Substring(0, 2) + "." + frequency.Substring(2, 2);
            // set altitude
            int alt = (int)(altitude * Sim.FEET_PER_METRE);
            // set heading
            string headingText = heading.ToString("D3", CultureInfo.InvariantCulture);
            // rules
            string rules = ifr ? "IFR" : "VFR";
            if (flightPlan.rules.Length > 0) rules = flightPlan.rules;
            // callsign
            callsign = callsign.Replace("-", "").Replace(":", "");
            // name
            name = name.Replace("-", "").Replace(":", "");
            // route
            if (flightPlan.departure.Length > 0) from = flightPlan.departure;
            from = from.Replace("-", "").Replace(":", "");
            if (flightPlan.destination.Length > 0) to = flightPlan.destination;
            to = to.Replace("-", "").Replace(":", "");
            // type
            string type = flightPlan.icaoType.Replace("-", "").Replace(":", "");
            if (type.Length == 0) type = "C172";
            string alternate = flightPlan.alternate.Replace("-", "").Replace(":", "");
            string cruise = flightPlan.speed.Replace("-", "").Replace(":", "");
            string altLevel = flightPlan.altitude.Replace("-", "").Replace(":", "");
            // network
            string network = "JoinFS";
            string typePrefix = "/";

            // check for ATC
            if (atc)
            {
                // ATC client
                return callsign + ":" + name + ":" + name + ":ATC:" + frequencyText + ":" + latitude + ":" + longitude + ":" + alt + ":0::::::::::" + (level + 2) + ":" + range + ":::::::::::::::::::::::::::";
            }
            else
            {
                // pilot client
                return callsign + ":" + name + ":" + name + ":PILOT:" + frequencyText + ":" + latitude + ":" + longitude + ":" + alt + ":" + speed + ":" + typePrefix + type + ":" + cruise + ":" + from + ":" + altLevel + ":" + to + ":" + network + ":::" + squawk.ToString("", CultureInfo.InvariantCulture) + "::::" + rules + ":::::::" + alternate + ":" + flightPlan.remarks + ":" + flightPlan.route + "::::::::" + headingText + ":::";
            }
        }

        /// <summary>
        /// Write server to output
        /// </summary>
        /// <param name="writer"></param>
        /// <param name="endPoint"></param>
        /// <param name="name"></param>
        void WriteServer(StreamWriter writer, string ipAddressText, string addressText, string name)
        {
            // remove characters
            name = name.Replace("-", "").Replace(":", "");
            // Server
            writer.WriteLine(ipAddressText + ":" + addressText + ":Internet:" + name + ":true:999");
        }

        /// <summary>
        /// List of guids
        /// </summary>
        List<Guid> guidList = new List<Guid>();

        /// <summary>
        /// List of clients
        /// </summary>
        List<string> clientList = new List<string>();

        /// <summary>
        /// Update Euroscope file
        /// </summary>
        void Update()
        {
            // get full path
            string path = Path.Combine(main.documentsPath, OUTPUT_FILE);

            StreamWriter writer = null;
            try
            {
                // open file
                writer = new StreamWriter(path);
                if (writer != null)
                {
                    // reset lists
                    guidList.Clear();
                    clientList.Clear();

                    // check for local ATC
                    if (main.settingsAtc && main.settingsAtcAirport.Length > 0)
                    {
                        double latitude = 0.0;
                        double longitude = 0.0;

                        // check if airport is listed
                        if (main.airportList.ContainsKey(main.settingsAtcAirport))
                        {
                            // convert to radians
                            latitude = Math.Min(90.0, Math.Max(-90.0, main.airportList[main.settingsAtcAirport].latitude));
                            longitude = Math.Min(180.0, Math.Max(-180.0, main.airportList[main.settingsAtcAirport].longitude));
                        }
                        // get ATC level
                        int level = Settings.Default.AtcLevel;
                        // write client entry for ATC
                        clientList.Add(WriteClient(true, Sim.MakeAtcCallsign(main.settingsAtcAirport, level), main.settingsNickname, Settings.Default.AtcFrequency.ToString(CultureInfo.InvariantCulture), latitude, longitude, 0.0, 0, new Sim.FlightPlan(), "", "", 0, level, main.settingsActivityCircle, true, 0));
                    }

                    // check for simulator
                    if (main.sim != null)
                    {
                        // for each object
                        foreach (var obj in main.sim.objectList)
                        {
                            // check for aircraft
                            if (obj is Sim.Aircraft aircraft && aircraft.showOnRadar)
                            {
                                // nickname
                                string nickname = "";
                                // check for node
                                if (main.network.nodeList.ContainsKey(aircraft.ownerNuid))
                                {
                                    // get nickname
                                    nickname = main.network.nodeList[aircraft.ownerNuid].nickname;
                                }

                                // default info
                                string com1 = "122.800";
                                string from = "";
                                string to = "";
                                int squawk = 1200;
                                bool ifr = true;
                                double latitude = 0.0, longitude = 0.0, altitude = 0.0;
                                ushort heading = 0;

                                // get variables
                                if (aircraft.variableSet != null)
                                {
                                    com1 = aircraft.variableSet.GetInteger(vuidCom1).ToString(CultureInfo.InvariantCulture);
                                    com1 = (com1.Length < 4) ? "" : com1.Substring(0, 3) + "." + com1.Substring(3, 2);
                                    from = aircraft.variableSet.GetString8(vuidFrom);
                                    to = aircraft.variableSet.GetString8(vuidTo);
                                    squawk = aircraft.variableSet.GetInteger(vuidSquawk);
                                    ifr = aircraft.variableSet.GetInteger(vuidIfr) != 0;
                                }

                                // get position
                                Sim.Pos position = aircraft.Position;
                                if (position != null)
                                {
                                    latitude = position.geo.z * (180.0 / Math.PI);
                                    longitude = position.geo.x * (180.0 / Math.PI);
                                    altitude = position.geo.y;
                                    heading = (ushort)(position.angles.y * 180.0 / Math.PI);
                                }

                                // write client entry for pilot
                                clientList.Add(WriteClient(false, aircraft.flightPlan.callsign, nickname, com1, latitude, longitude, altitude, GetSpeed(aircraft.netVelocity), aircraft.flightPlan, from, to, squawk, 0, 0, ifr, heading));
                            }
                        }

                        // for each node
                        foreach (var node in main.network.nodeList)
                        {
                            // check if seen guid
                            if (guidList.Exists(g => g.Equals(node.Value.guid)) == false)
                            {
                                // save guid
                                guidList.Add(node.Value.guid);
                            }

                            // check for ATC
                            if (node.Value.atc && node.Value.atcAirport.Length > 0)
                            {
                                double latitude = 0.0;
                                double longitude = 0.0;
                                // check if airport is listed
                                if (main.airportList.ContainsKey(node.Value.atcAirport))
                                {
                                    // convert to radians
                                    latitude = Math.Min(90.0, Math.Max(-90.0, main.airportList[node.Value.atcAirport].latitude));
                                    longitude = Math.Min(180.0, Math.Max(-180.0, main.airportList[node.Value.atcAirport].longitude));
                                }
                                // write client entry for ATC
                                clientList.Add(WriteClient(true, Sim.MakeAtcCallsign(node.Value.atcAirport, node.Value.atcLevel), node.Value.nickname, node.Value.atcFrequency.ToString(CultureInfo.InvariantCulture), latitude, longitude, 0.0, 0, new Sim.FlightPlan(), "", "", 0, node.Value.atcLevel, node.Value.activityCircle, true, 0));
                            }
                        }
                    }

                    // check for global list
                    if (main.settingsWhazzupPublic)
                    {
                        // for each hub
                        foreach (var hub in main.network.hubList)
                        {
                            // for each user
                            foreach (var user in hub.userList)
                            {
                                // check that user hasn't been checked
                                if (guidList.Exists(g => g.Equals(user.guid)) == false)
                                {
                                    // save guid
                                    guidList.Add(user.guid);
                                    // check for ATC
                                    if (user.atc && user.flightPlan.departure.Length > 0)
                                    {
                                        double latitude = 0.0;
                                        double longitude = 0.0;
                                        // check if airport is listed
                                        if (main.airportList.ContainsKey(user.flightPlan.departure))
                                        {
                                            // convert to radians
                                            latitude = Math.Min(90.0, Math.Max(-90.0, main.airportList[user.flightPlan.departure].latitude));
                                            longitude = Math.Min(180.0, Math.Max(-180.0, main.airportList[user.flightPlan.departure].longitude));
                                        }
                                        // write client entry for ATC
                                        clientList.Add(WriteClient(true, Sim.MakeAtcCallsign(user.flightPlan.departure, user.level), user.nickname, user.frequency.ToString(CultureInfo.InvariantCulture), latitude, longitude, 0.0, 0, new Sim.FlightPlan(), "", "", 0, user.level, user.range, true, 0));
                                    }
                                    else
                                    {
                                        // write client entry for pilot
                                        clientList.Add(WriteClient(false, user.flightPlan.callsign, user.nickname, user.frequency.ToString(CultureInfo.InvariantCulture), user.latitude, user.longitude, user.altitude, user.speed, user.flightPlan, user.flightPlan.departure, user.flightPlan.destination, user.squawk, 0, 0, user.ifr, user.heading));
                                    }
                                }
                            }
                        }
                    }

                    // hub count
                    int hubCount = main.settingsHub ? 1 : 0;
                    // for each hub
                    foreach (var hub in main.network.hubList)
                    {
                        // check if hub is online
                        if (hub.online)
                        {
                            // increment hub count
                            hubCount++;
                        }
                    }

                    // general section
                    writer.WriteLine("!GENERAL");
                    writer.WriteLine("VERSION = 1");
                    writer.WriteLine("RELOAD = 1");
                    writer.WriteLine("UPDATE = " + DateTime.Now.ToString("yyyyMMddHHmmss"));
                    writer.WriteLine("CONNECTED CLIENTS = " + clientList.Count);
                    writer.WriteLine("CONNECTED SERVERS = " + hubCount);

                    // clients section
                    writer.WriteLine("!CLIENTS");

                    // for each client
                    foreach(var client in clientList)
                    {
                        // write client
                        writer.WriteLine(client);
                    }

                    // clients section
                    writer.WriteLine("!SERVERS");

                    // check for this hub
                    if (main.settingsHub)
                    {
                        WriteServer(writer, Settings.Default.MyIp, main.settingsHubDomain, main.settingsHubName);
                    }

                    // for each hub
                    foreach (var hub in main.network.hubList)
                    {
                        // check if hub is online
                        if (hub.online)
                        {
                            WriteServer(writer, hub.endPoint.Address.ToString(), hub.addressText, hub.name);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                main.MonitorEvent("ERROR: Updating Whazzup. " + ex.Message);
            }

            // close writer
            if (writer != null)
            {
                writer.Close();
            }

            // get old whazzup path
            string oldPath = Path.Combine(main.storagePath, OUTPUT_FILE);
            // check if old path is different
            if (oldPath.Equals(path, StringComparison.OrdinalIgnoreCase) == false)
            {
                try
                {
                    // copy to old whazzup path
                    File.Copy(path, oldPath, true);
                }
                catch (Exception ex)
                {
                    main.MonitorEvent("ERROR: Updating old Whazzup path. " + ex.Message);
                }
            }
        }
    }
}
