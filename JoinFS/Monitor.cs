using System;
using System.Collections.Generic;
using System.IO;
using JoinFS.Net;

namespace JoinFS
{
    public class Monitor
    {
        public const string LOG_FILE = "log";

        Main main;

        /// <summary>
        /// Log files
        /// </summary>
        public string logName = "";
        public string previousName = "";

        /// <summary>
        /// displayed lines of text
        /// </summary>
        readonly List<string> lines = [];

        StreamWriter writer;

        /// <summary>
        /// Guards lines and the log file. The monitor is written from several threads (app, sim,
        /// network services), so it has its own lock instead of relying on Main.conch.
        /// </summary>
        readonly object sync = new();

        /// <summary>
        /// Keep track of repeated lines
        /// </summary>
        int repeatCount = 1;

        /// <summary>
        /// Show network events
        /// </summary>
        public bool network = false;

        /// <summary>
        /// Show variable events
        /// </summary>
        public bool variables = false;

        /// <summary>
        /// Open log file
        /// </summary>
        public void OpenLog()
        {
            lock (sync)
            {
                OpenLogLocked();
            }
        }

        void OpenLogLocked()
        {
            // check that log file is currently closed
            if (writer == null)
            {
                // check if file exists
                if (File.Exists(logName))
                {
                    // rename current log file
                    File.Delete(previousName);
                    File.Move(logName, previousName);
                }

                try
                {
                    // check for auto log
//                    if (Settings.Default.AutoLog)
                    if (true)
                    {
                            // open file
                            writer = new StreamWriter(logName)
                        {
                            // auto flush
                            AutoFlush = true
                        };
                        // write current lines
                        foreach (var line in lines)
                        {
                            // save line to log file
                            writer.WriteLine(line);
                        }
                    }
                }
                catch (Exception ex)
                {
                    main.ShowMessage(ex.Message);
                }
            }
        }

        /// <summary>
        /// Close log
        /// </summary>
        public void CloseLog()
        {
            lock (sync)
            {
                // close log file
                writer?.Close();
                writer = null;
            }
        }

        /// <summary>
        /// Copy of the last <paramref name="max"/> lines, and the total number of lines
        /// </summary>
        public string[] CopyLines(int max, out int total)
        {
            lock (sync)
            {
                total = lines.Count;
                int count = Math.Min(max, lines.Count);
                return lines.GetRange(lines.Count - count, count).ToArray();
            }
        }

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="mainForm"></param>
        public Monitor(Main main)
        {
            this.main = main;

            // get port
            ushort port = main.ActivePort;

            // make file name
            logName = main.storagePath + Path.DirectorySeparatorChar + LOG_FILE + "-" + port + ".txt";
            previousName = main.storagePath + Path.DirectorySeparatorChar + LOG_FILE + "-" + port + "-previous.txt";

            // check for auto log
//            if (Settings.Default.AutoLog)
            if (true)
            {
                    // open log
                    OpenLog();
            }
        }

        /// <summary>
        /// Process repeated lines
        /// </summary>
        void ProcessRepeat()
        {
            // check for repeated lines
            if (repeatCount > 1)
            {
                // make repeat line
                string repeatText = DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss.fff") + " - " + "[" + repeatCount.ToString() + " times]";
                // add line
                lines.Add(repeatText);
                // check for log file
                // save line to log file
                writer?.WriteLine(repeatText);
#if CONSOLE
                Console.WriteLine(repeatText);
#endif
                repeatCount = 1;
            }
        }

        /// <summary>
        /// Write what the network knows of its nodes
        /// </summary>
        public void WriteNodeStatistics()
        {
            Write("== NODE STATS ==");
            Write("Session ID : " + main.network.Snapshot.Suid);
            Write("Node Count : " + main.network.Snapshot.PeerCount);
            Write("Routing Nodes : " + main.network.Snapshot.RelayCount);
            Write("Guaranteed Incoming : " + main.network.Snapshot.GuaranteedInCount);
            Write("Guaranteed Outgoing : " + main.network.Snapshot.GuaranteedOutCount);
            // check for hub
            if (main.settingsHub)
            {
                Write("Online Users : " + main.network.Users.OnlineUserCount);
            }
        }

        /// <summary>
        /// Write how many of each kind of packet were received
        /// </summary>
        public void WritePacketStatistics()
        {
            Write("== RECEIVED PACKETS ==");
            Write("ID : Minute : Hour : Day : Total (count b/s)");
            Write("Join : " + Stats.Join);
            Write("JoinReply : " + Stats.JoinReply);
            Write("JoinFail : " + Stats.JoinFail);
            Write("Login : " + Stats.Login);
            Write("LoginFail : " + Stats.LoginFail);
            Write("Leave : " + Stats.Leave);
            Write("AddNode : " + Stats.AddNode);
            Write("Pulse : " + Stats.Pulse);
            Write("PulseResponse : " + Stats.PulseResponse);
            Write("GuaranteedDone : " + Stats.GuaranteedDone);
            Write("Pathfinder : " + Stats.Pathfinder);
            Write("PathfinderResponse : " + Stats.PathfinderResponse);
            Write("ObjectPosition : " + Stats.ObjectPosition);
            Write("AircraftPosition : " + Stats.AircraftPosition);
            Write("SimEvent : " + Stats.SimEvent);
            Write("WeatherRequest : " + Stats.WeatherRequest);
            Write("WeatherReply : " + Stats.WeatherReply);
            Write("WeatherUpdate : " + Stats.WeatherUpdate);
            Write("SharedData : " + Stats.SharedData);
            Write("StatusRequest : " + Stats.StatusRequest);
            Write("Status : " + Stats.Status);
            Write("HubList : " + Stats.HubList);
            Write("RemoveObject : " + Stats.RemoveObject);
            Write("UserListRequest : " + Stats.UserListRequest);
            Write("UserList : " + Stats.UserList);
            Write("UserList2 : " + Stats.UserList2);
            Write("UserPositionRequest : " + Stats.UserPositionRequest);
            Write("UserPositions : " + Stats.UserPositions);
            Write("SessionCommsRequest : " + Stats.SessionCommsRequest);
            Write("Notes : " + Stats.Notes);
            Write("UserNuidRequest : " + Stats.UserNuidRequest);
            Write("UserNuid : " + Stats.UserNuid);
            Write("Online : " + Stats.Online);
            Write("FlightPlanRequest : " + Stats.FlightPlanRequest);
            Write("FlightPlan : " + Stats.FlightPlan);
            Write("WrongVersion : " + Stats.WrongVersion);
            Write("Total : " + Stats.Total);
        }

        /// <summary>
        /// Output some text to the event window
        /// </summary>
        /// <param name="text">Output text</param>
        public void Write(String text)
        {
            lock (sync)
            {
                WriteLocked(text);
            }
        }

        void WriteLocked(String text)
        {
            // don't display previous line
            if (lines.Count == 0 || lines[lines.Count - 1].Length <= 26 || text.Equals(lines[lines.Count - 1].Substring(26)) == false)
            {
                ProcessRepeat();

                // include time
                string line = DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss.fff") + " - " + text;
                // add line
                lines.Add(line);
                // check for log file
                // save line to log file
                writer?.WriteLine(line);
#if CONSOLE
                Console.WriteLine(line);
#endif
            }
            else
            {
                // increment repeat
                repeatCount++;
            }
        }
    }
}
