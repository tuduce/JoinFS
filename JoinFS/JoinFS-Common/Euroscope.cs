using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System.Globalization;
using JoinFS.Properties;

namespace JoinFS
{
    public class Euroscope
    {
        Main main;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="mainForm"></param>
        public Euroscope(Main main)
        {
            this.main = main;

            // get vuids
            vuidSquawk = VariableMgr.CreateVuid("transponder code:1");
        }

        /// <summary>
        /// Output file
        /// </summary>
        const string OUTPUT_FILE = "euroscope.txt";

        /// <summary>
        /// Maximum number of samples
        /// </summary>
        const int MAX_SAMPLE_COUNT = 1;

        /// <summary>
        /// Update timer
        /// </summary>
        const float UPDATE_TIME = 1.0f;
        double updateTime = 0.0f;

        /// <summary>
        /// Is there a local ATC
        /// </summary>
        bool localAtc = false;

        /// <summary>
        /// Vuids
        /// </summary>
        uint vuidSquawk;

        /// <summary>
        /// Update
        /// </summary>
        public void DoWork()
        {
            // update euroscope
            if (main.ElapsedTime > updateTime)
            {
                // check if euroscope enabled
                if (main.settingsAtc && Settings.Default.Euroscope)
                {
                    // update
                    Update();
                }
                // next update
                updateTime = main.ElapsedTime + UPDATE_TIME;
            }
        }

        /// <summary>
        /// A sample instance
        /// </summary>
        class Sample
        {
            /// <summary>
            /// List of current euroscope entries
            /// </summary>
            public List<string> lines = new List<string>();

            /// <summary>
            /// Constructor
            /// </summary>
            public Sample()
            {
            }
        }

        /// <summary>
        /// List of samples
        /// </summary>
        List<Sample> sampleList = new List<Sample>();

        /// <summary>
        /// new ATC
        /// </summary>
        struct Atc
        {
            public string airport;
            public int level;
            public int frequency;

            public Atc(string airport, int level, int frequency)
            {
                this.airport = airport;
                this.level = level;
                this.frequency = frequency;
            }
        }

        /// <summary>
        /// List of ATC to add
        /// </summary>
        List<Atc> addList = new List<Atc>();

        /// <summary>
        /// List of ATC to remove
        /// </summary>
        List<Atc> removeList = new List<Atc>();

        /// <summary>
        /// Update Euroscope file
        /// </summary>
        void Update()
        {
            try
            {
                // open file
                StreamWriter writer = new StreamWriter(Path.Combine(main.storagePath, OUTPUT_FILE), false);
                if (writer != null)
                {
                    // create new sample
                    Sample newSample = new Sample();

                    // check for sim
                    if (main.sim != null)
                    {
                        // for all objects
                        foreach (var obj in main.sim.objectList)
                        {
                            // check for aircraft
                            if (obj is Sim.Aircraft aircraft && aircraft.variableSet != null)
                            {
                                // empty line
                                newSample.lines.Add("");
                                // write time
                                //                                newSample.lines.Add("[" + DateTime.UtcNow.ToString("HH:mm:ss") + " >>>> *A]");
                                // write aircraft entry
                                newSample.lines.Add("@N:" + aircraft.flightPlan.callsign + ":" + aircraft.variableSet.GetInteger(vuidSquawk).ToString("X", CultureInfo.InvariantCulture) + ":1:" + (aircraft.netPosition.geo.z * (180.0 / Math.PI)).ToString(CultureInfo.InvariantCulture) + ":" + (aircraft.netPosition.geo.x * (180.0 / Math.PI)).ToString(CultureInfo.InvariantCulture) + ":" + (aircraft.netPosition.geo.y * Sim.FEET_PER_METRE).ToString("N0", CultureInfo.InvariantCulture).Replace(",", "") + ":" + (aircraft.netPosition.angles.y * (180.0 / Math.PI)).ToString("N0", CultureInfo.InvariantCulture) + ":62905944:5");
                            }
                        }
                    }

                    // check for adding local ATC
                    if (localAtc == false && main.settingsAtc)
                    {
                        // get airport
                        string airport = main.settingsAtcAirport;
                        // check if airport is listed
                        if (main.airportList.ContainsKey(airport))
                        {
                            // convert to radians
                            double latitude = Math.Min(90.0, Math.Max(-90.0, main.airportList[airport].latitude));
                            double longitude = Math.Min(180.0, Math.Max(-180.0, main.airportList[airport].longitude));
                            // empty line
                            newSample.lines.Add("");
                            // write time
                            newSample.lines.Add("[" + DateTime.UtcNow.ToString("HH:mm:ss") + " <<<2 " + airport + "]");
                            // write ATC
                            newSample.lines.Add("%" + airport + ":" + Settings.Default.AtcFrequency + ":6:0:3:" + latitude + ":" + longitude + ":5");
                        }
                        else
                        {
                            // warning
                            main.MonitorEvent("Unknown airport code, " + airport);
                        }
                        // update flag
                        localAtc = true;
                    }

                    // check for removing local ATC
                    if (localAtc && main.settingsAtc == false)
                    {
                        // get airport
                        string airport = main.settingsAtcAirport;
                        // empty line
                        newSample.lines.Add("");
                        // write time
                        newSample.lines.Add("[" + DateTime.UtcNow.ToString("HH:mm:ss") + " >>>> *A]");
                        // write ATC
                        newSample.lines.Add("#DA" + Sim.MakeAtcCallsign(airport, Settings.Default.AtcLevel) + ":" + airport);
                        // update flag
                        localAtc = false;
                    }

                    // for each ATC to add
                    foreach (var atc in addList)
                    {
                        // check if airport is listed
                        if (main.airportList.ContainsKey(atc.airport))
                        {
                            // convert to radians
                            double latitude = Math.Min(90.0, Math.Max(-90.0, main.airportList[atc.airport].latitude));
                            double longitude = Math.Min(180.0, Math.Max(-180.0, main.airportList[atc.airport].longitude));
                            // empty line
                            newSample.lines.Add("");
                            // write time
                            newSample.lines.Add("[" + DateTime.UtcNow.ToString("HH:mm:ss") + " >>>> *A]");
                            // write ATC
                            newSample.lines.Add("%" + Sim.MakeAtcCallsign(atc.airport, atc.level) + ":" + atc.frequency + ":6:0:3:" + latitude + ":" + longitude + ":5");
                        }
                        else
                        {
                            // warning
                            main.MonitorEvent("Unknown airport code, " + atc.airport);
                        }
                    }
                    // clear list
                    addList.Clear();

                    // for each ATC to remove
                    foreach (var atc in removeList)
                    {
                        // empty line
                        newSample.lines.Add("");
                        // write time
                        newSample.lines.Add("[" + DateTime.UtcNow.ToString("HH:mm:ss") + " >>>> *A]");
                        // write ATC
                        newSample.lines.Add("#DA" + Sim.MakeAtcCallsign(atc.airport, atc.level) + ":" + atc.airport);
                    }
                    // clear list
                    removeList.Clear();

                    // add sample to list
                    sampleList.Add(newSample);

                    // check for maximum samples
                    if (sampleList.Count > MAX_SAMPLE_COUNT)
                    {
                        // remove earliest sample
                        sampleList.Remove(sampleList[0]);
                    }

                    // for each sample
                    foreach (var sample in sampleList)
                    {
                        // for each stored line
                        foreach (var line in sample.lines)
                        {
                            // write line
                            writer.WriteLine(line);
                        }
                    }

                    // close file
                    writer.Close();
                }
            }
            catch (Exception ex)
            {
                main.MonitorEvent("Updating EuroScope - " + ex.Message);
            }
        }

        /// <summary>
        /// Add ATC to euroscope
        /// </summary>
        /// <param name="airport">Airport</param>
        /// <param name="level">Level</param>
        /// <param name="frequency">Frequency</param>
        public void AddAtc(string airport, int level, int frequency)
        {
            // add ATC
            addList.Add(new Atc(airport, level, frequency));
        }

        /// <summary>
        /// Remove ATC from euroscope
        /// </summary>
        /// <param name="airport">Airport</param>
        /// <param name="level">Level</param>
        /// <param name="frequency">Frequency</param>
        public void RemoveAtc(string airport, int level, int frequency)
        {
            // remove ATC
            removeList.Add(new Atc(airport, level, frequency));
        }
    }
}
